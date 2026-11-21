using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Audio;
using MixerDeAudio.Services;

namespace MixerDeAudio.Remote;

/// <summary>
/// Servidor HTTP/1.1 próprio e pequeno (Fase 6, D5 a D7, D10): um <see cref="TcpListener"/> em <c>0.0.0.0:8750</c> (ou
/// em <c>127.0.0.1</c> com <c>--celular-local</c>), sem ASP.NET e sem HttpListener (que pede administrador para escutar na
/// rede). Uma requisição por conexão com <c>Connection: close</c>; só o <c>/ws</c> vira WebSocket. Ligado e parado, fica
/// num <c>AcceptTcpClientAsync</c>, sem timer. A rede roda no pool de threads; o que mexe no áudio ou na configuração é
/// enfileirado na thread da tela.
/// </summary>
internal sealed class RemoteServer : IDisposable
{
    public const int FirstPort = 8750;
    public const int LastPort = 8759;
    public const int MaxPhones = 4;
    private const int MaxOpenRequests = 32;
    private const string PageResource = "MixerDeAudio.Web.index.html";
    private static readonly TimeSpan HeadTimeout = TimeSpan.FromSeconds(10);

    private readonly AudioHub _hub;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _ui;
    private readonly bool _localOnly;
    private readonly PairingLimiter _limiter = new();
    private readonly object _gate = new();
    private readonly List<RemoteSession> _sessions = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private int _openRequests;
    private int _connections;
    private int _nextNumber;

    public RemoteServer(AudioHub hub, SettingsStore settings, DispatcherQueue ui, bool localOnly)
    {
        _hub = hub;
        _settings = settings;
        _ui = ui;
        _localOnly = localOnly;
    }

    public bool IsRunning => _listener is not null;

    /// <summary>Porta em que está escutando; 0 se parado.</summary>
    public int Port { get; private set; }

    /// <summary>Celulares conectados agora.</summary>
    public int ConnectionCount => Volatile.Read(ref _connections);

    /// <summary>A quantidade de celulares conectados mudou (thread qualquer).</summary>
    public event Action? ConnectionsChanged;

    /// <summary>Liga ou desliga conforme <c>CelularLigado</c> na configuração (D10). Thread da tela.</summary>
    public void Apply()
    {
        if (_busy) return;
        var want = _settings.Current.CelularLigado;
        if (want && !IsRunning) Start();
        else if (!want && IsRunning) Stop();
    }

    private void Start()
    {
        _busy = true;
        try
        {
            // O código nasce na primeira vez que o recurso é ligado (D11)
            if (_settings.Current.CodigoCelular is null)
                _settings.Update(s => s with { CodigoCelular = RemoteRules.NewCode() });

            var address = _localOnly ? IPAddress.Loopback : IPAddress.Any;
            for (var port = FirstPort; port <= LastPort; port++)
            {
                var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
                try
                {
                    listener.Start(16);
                }
                catch (SocketException)
                {
                    continue; // porta ocupada: tenta a seguinte
                }
                _listener = listener;
                Port = port;
                _cts = new CancellationTokenSource();
                Log.Write($"celular: servidor ligado em {address}:{port}");
                _ = AcceptLoopAsync(listener, _cts.Token);
                return;
            }
            Log.Write($"ERRO celular: nenhuma porta livre de {FirstPort} a {LastPort}");
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void Stop()
    {
        _busy = true;
        try
        {
            var listener = _listener;
            _listener = null;
            Port = 0;
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            try { listener?.Stop(); } catch { }
            CloseAllSessions();
            _cts?.Dispose();
            _cts = null;
            if (listener is not null) Log.Write("celular: servidor desligado");
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        if (IsRunning) Stop();
    }

    /// <summary>
    /// "Gerar novo código" (D7): troca o código, apaga todas as chaves e fecha os WebSockets abertos. Thread da tela.
    /// </summary>
    public void RegenerateCode()
    {
        _settings.Update(s => s with { CodigoCelular = RemoteRules.NewCode(), ChavesCelular = new List<PairedPhone>() });
        CloseAllSessions();
        Log.Write("celular: novo codigo gerado");
    }

    private void CloseAllSessions()
    {
        RemoteSession[] all;
        lock (_gate) all = _sessions.ToArray();
        foreach (var s in all) s.Close();
    }

    // ---- Rede ----

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                Log.Write($"ERRO celular: {ex.Message}");
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken serverCt)
    {
        var counted = false;
        try
        {
            using (client)
            {
                // Só rede local (D7): o resto é recusado sem resposta
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (!RemoteRules.IsPrivateNetwork(remote)) return;
                if (Interlocked.Increment(ref _openRequests) > MaxOpenRequests) { Interlocked.Decrement(ref _openRequests); return; }
                counted = true;
                client.NoDelay = true;
                var stream = client.GetStream();

                HttpHead? head;
                byte[] body;
                using (var headCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt))
                {
                    headCts.CancelAfter(HeadTimeout);
                    var read = await ReadRequestAsync(stream, headCts.Token).ConfigureAwait(false);
                    if (read.Status != 0)
                    {
                        if (read.Status > 0) await WriteAsync(stream, read.Status, "text/plain; charset=utf-8", Array.Empty<byte>(), null, headCts.Token).ConfigureAwait(false);
                        return;
                    }
                    head = read.Head!;
                    body = read.Body;
                }

                var path = head.Path;
                var q = path.IndexOf('?');
                if (q >= 0) path = path.Substring(0, q);

                if (path == "/ws")
                {
                    // O WebSocket vive além do tempo do cabeçalho e não conta como requisição aberta
                    Interlocked.Decrement(ref _openRequests);
                    counted = false;
                    await HandleWebSocketAsync(client, stream, head, serverCt).ConfigureAwait(false);
                    return;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
                cts.CancelAfter(HeadTimeout);
                var ct = cts.Token;
                if (head.Method == "GET" && path == "/")
                    await ServePageAsync(stream, ct).ConfigureAwait(false);
                else if (head.Method == "POST" && path == "/parear")
                    await PairAsync(stream, head, body, remote, ct).ConfigureAwait(false);
                else if (head.Method == "GET" && path.StartsWith("/icone/", StringComparison.Ordinal))
                    await ServeIconAsync(stream, head, path.Substring("/icone/".Length), ct).ConfigureAwait(false);
                else
                    await WriteAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("nao encontrado"), null, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or WebSocketException) { }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: {ex.Message}");
        }
        finally
        {
            if (counted) Interlocked.Decrement(ref _openRequests);
        }
    }

    private readonly record struct RequestRead(int Status, HttpHead? Head, byte[] Body);

    /// <summary>Lê o cabeçalho (máx. 8 KB) e o corpo (máx. 1 KB). Status 0 = ok; positivo = responder com ele; negativo = só fechar.</summary>
    private static async Task<RequestRead> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[RemoteRules.MaxHeadBytes];
        var total = 0;
        var end = -1;
        while (end < 0)
        {
            if (total == buffer.Length) return new RequestRead(431, null, Array.Empty<byte>());
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) return new RequestRead(-1, null, Array.Empty<byte>());
            total += n;
            end = RemoteRules.FindHeadEnd(buffer.AsSpan(0, total));
        }
        var head = RemoteRules.ParseHead(Encoding.Latin1.GetString(buffer, 0, end));
        if (head is null) return new RequestRead(400, null, Array.Empty<byte>());
        var length = RemoteRules.BodyLength(head);
        if (length < 0) return new RequestRead(413, null, Array.Empty<byte>());
        var body = new byte[length];
        var have = Math.Min(length, total - end);
        Buffer.BlockCopy(buffer, end, body, 0, have);
        while (have < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(have), ct).ConfigureAwait(false);
            if (n == 0) return new RequestRead(-1, null, Array.Empty<byte>());
            have += n;
        }
        return new RequestRead(0, head, body);
    }

    private static string Reason(int status) => status switch
    {
        101 => "Switching Protocols",
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        _ => "Error",
    };

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body, string[]? extraHeaders, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
        sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("X-Content-Type-Options: nosniff\r\n");
        sb.Append("Referrer-Policy: no-referrer\r\n");
        if (extraHeaders is not null)
            foreach (var h in extraHeaders) sb.Append(h).Append("\r\n");
        sb.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    // ---- GET / ----

    private static async Task ServePageAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] html;
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(PageResource))
        {
            if (res is null)
            {
                await WriteAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("pagina indisponivel"), null, ct).ConfigureAwait(false);
                return;
            }
            using var ms = new MemoryStream();
            await res.CopyToAsync(ms, ct).ConfigureAwait(false);
            html = ms.ToArray();
        }
        await WriteAsync(stream, 200, "text/html; charset=utf-8", html, null, ct).ConfigureAwait(false);
    }

    // ---- POST /parear (D6, D7) ----

    private async Task PairAsync(NetworkStream stream, HttpHead head, byte[] body, IPAddress? remote, CancellationToken ct)
    {
        const string json = "application/json; charset=utf-8";
        // A origem da página (quando o navegador a informa) tem que ser o próprio servidor
        var origin = head.Header("Origin");
        if (origin is not null && !RemoteRules.OriginMatchesHost(origin, head.Header("Host")))
        {
            await WriteAsync(stream, 403, json, Encoding.UTF8.GetBytes("{\"ok\":false}"), null, ct).ConfigureAwait(false);
            return;
        }
        if (_limiter.IsBlocked)
        {
            await WriteAsync(stream, 429, json, Encoding.UTF8.GetBytes("{\"ok\":false,\"bloqueado\":true}"), new[] { "Retry-After: 60" }, ct).ConfigureAwait(false);
            return;
        }

        string? given = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("codigo", out var c) && c.ValueKind == JsonValueKind.String)
                given = c.GetString();
        }
        catch (JsonException) { }
        if (given is null)
        {
            await WriteAsync(stream, 400, json, Encoding.UTF8.GetBytes("{\"ok\":false}"), null, ct).ConfigureAwait(false);
            return;
        }

        if (!RemoteRules.CodeMatches(_settings.Current.CodigoCelular, given))
        {
            Log.Write($"celular: codigo errado de {remote}");
            if (_limiter.RegisterFailure()) Log.Write("celular: pareamento bloqueado por 60 s");
            await WriteAsync(stream, 401, json, Encoding.UTF8.GetBytes("{\"ok\":false}"), null, ct).ConfigureAwait(false);
            return;
        }

        _limiter.RegisterSuccess();
        var key = RemoteRules.NewKey();
        var name = RemoteRules.PhoneName(head.Header("User-Agent"));
        var phone = new PairedPhone { Hash = RemoteRules.HashKey(key), Nome = name, UltimoUso = DateTime.UtcNow };
        await OnUi(() =>
        {
            _settings.Update(s => s with { ChavesCelular = new List<PairedPhone>(s.ChavesCelular) { phone } });
            return true;
        }).ConfigureAwait(false);
        Log.Write($"celular: pareado ({name})");
        var cookie = $"Set-Cookie: {RemoteRules.CookieName}={key}; Max-Age=31536000; SameSite=Strict; Path=/; HttpOnly";
        await WriteAsync(stream, 200, json, Encoding.UTF8.GetBytes("{\"ok\":true}"), new[] { cookie }, ct).ConfigureAwait(false);
    }

    /// <summary>O celular dono do cookie <c>mx</c>, se a chave for válida (compara o SHA-256 com o do config.json).</summary>
    private PairedPhone? Authenticate(HttpHead head)
    {
        var cookie = RemoteRules.GetCookie(head.Header("Cookie"));
        if (string.IsNullOrEmpty(cookie)) return null;
        var hash = RemoteRules.HashKey(cookie);
        foreach (var p in _settings.Current.ChavesCelular)
            if (string.Equals(p.Hash, hash, StringComparison.Ordinal)) return p;
        return null;
    }

    // ---- GET /icone/<id> ----

    private async Task ServeIconAsync(NetworkStream stream, HttpHead head, string id, CancellationToken ct)
    {
        if (Authenticate(head) is null)
        {
            await WriteAsync(stream, 401, "text/plain; charset=utf-8", Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        string? exe = null;
        if (RemoteProtocol.IsIconId(id))
        {
            exe = await OnUi(() =>
            {
                if (!_hub.IsActive) return null;
                foreach (var g in _hub.Sessions.Groups)
                    if (RemoteProtocol.IconId(g.Key) == id) return g.ExePath;
                return null;
            }).ConfigureAwait(false);
        }
        var png = exe is null ? Array.Empty<byte>() : await AppIconCache.GetPngAsync(exe).ConfigureAwait(false);
        if (png.Length == 0)
        {
            await WriteAsync(stream, 404, "text/plain; charset=utf-8", Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        await WriteAsync(stream, 200, "image/png", png, new[] { "Cache-Control: private, max-age=3600" }, ct).ConfigureAwait(false);
    }

    // ---- GET /ws (D5, D6, D10) ----

    private async Task HandleWebSocketAsync(TcpClient client, NetworkStream stream, HttpHead head, CancellationToken serverCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        cts.CancelAfter(HeadTimeout);
        var ct = cts.Token;
        const string text = "text/plain; charset=utf-8";
        if (!RemoteRules.IsWebSocketUpgrade(head))
        {
            await WriteAsync(stream, 400, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        var phone = Authenticate(head);
        if (phone is null)
        {
            await WriteAsync(stream, 401, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        if (!RemoteRules.OriginMatchesHost(head.Header("Origin"), head.Header("Host")))
        {
            await WriteAsync(stream, 403, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        if (Interlocked.Increment(ref _connections) > MaxPhones)
        {
            Interlocked.Decrement(ref _connections);
            await WriteAsync(stream, 503, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }

        RemoteSession? session = null;
        var total = 0;
        try
        {
            var accept = RemoteRules.ComputeWebSocketAccept(head.Header("Sec-WebSocket-Key")!);
            var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            cts.CancelAfter(Timeout.InfiniteTimeSpan);

            var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                KeepAliveInterval = TimeSpan.FromSeconds(20),
            });
            var number = Interlocked.Increment(ref _nextNumber);
            session = new RemoteSession(this, _hub, socket, _ui, number) { KeyHash = phone.Hash };
            lock (_gate) _sessions.Add(session);
            total = ConnectionCount;
            Log.Write($"celular conectado ({total} no total)");
            ConnectionsChanged?.Invoke();
            _ui.TryEnqueue(() => TouchPhone(phone.Hash));
            await session.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            if (session is not null) lock (_gate) _sessions.Remove(session);
            var left = Interlocked.Decrement(ref _connections);
            if (session is not null)
            {
                Log.Write($"celular desconectado ({left})");
                ConnectionsChanged?.Invoke();
            }
        }
    }

    /// <summary>Atualiza o "último uso" do celular na configuração. Thread da tela.</summary>
    private void TouchPhone(string hash)
    {
        try
        {
            _settings.Update(s => s with
            {
                ChavesCelular = s.ChavesCelular.ConvertAll(p => p.Hash == hash ? p with { UltimoUso = DateTime.UtcNow } : p),
            });
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: {ex.Message}");
        }
    }

    // ---- Thread da tela ----

    /// <summary>Executa na thread da tela e devolve o resultado (fora dela, enfileira).</summary>
    public Task<T> OnUi<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run()
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }
        if (_ui.HasThreadAccess) Run();
        else if (!_ui.TryEnqueue(Run)) tcs.SetException(new InvalidOperationException("fila da tela encerrada"));
        return tcs.Task;
    }
}
