using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using MixerDeAudio.Pc;
using MixerDeAudio.Services;

namespace MixerDeAudio.Remote;

/// <summary>
/// Servidor HTTP/1.1 próprio e pequeno (Fase 6, D5 a D7, D10): um <see cref="TcpListener"/> em <c>0.0.0.0:8750</c> (ou
/// em <c>127.0.0.1</c> com <c>--celular-local</c>), sem ASP.NET e sem HttpListener (que pede administrador para escutar na
/// rede). Uma requisição por conexão com <c>Connection: close</c>; só o <c>/ws</c> e o <c>/tela/ws</c> viram WebSocket. Ligado e parado, fica
/// num <c>AcceptTcpClientAsync</c>, sem timer. A rede roda no pool de threads; o que mexe no áudio ou na configuração é
/// enfileirado na thread da tela.
/// </summary>
internal sealed class RemoteServer : IDisposable
{
    public const int FirstPort = RemoteRules.FirstPort;
    public const int LastPort = RemoteRules.LastPort;
    private static readonly TimeSpan PortWait = TimeSpan.FromSeconds(1); // espaço entre tentativas na porta salva (D12)
    private const int PortAttempts = 10; // 10 tentativas, uma por segundo: até 10 s
    private const int MaxOpenRequests = 32;
    private const string NewPageResource = "MixerDeAudio.Web/app/index.html";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> AssetETags = new(); // D5/D21: só o ETag fica na memória
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
    private bool _starting; // esperando a porta salva ficar livre (D12)
    private CancellationTokenSource? _startCts;
    private int _openRequests;
    private int _connections;
    private int _nextNumber;
    private int _screenBusy; // 1 enquanto há um canal /tela/ws aberto (Fase 10, D1: no máximo um por vez)

    public RemoteServer(AudioHub hub, SettingsStore settings, DispatcherQueue ui, bool localOnly)
    {
        _hub = hub;
        _settings = settings;
        _ui = ui;
        _localOnly = localOnly;
    }

    public bool IsRunning => _listener is not null;
    /// <summary>Esperando a porta salva ficar livre (D12).</summary>
    public bool IsStarting => _starting;

    /// <summary>Porta em que está escutando; 0 se parado.</summary>
    public int Port { get; private set; }

    /// <summary>Celulares conectados agora.</summary>
    public int ConnectionCount => Volatile.Read(ref _connections);

    /// <summary>A quantidade de celulares conectados mudou (thread qualquer).</summary>
    public event Action? ConnectionsChanged;

    /// <summary>O servidor terminou de ligar (ou desistiu) depois de esperar pela porta (D12). Thread da tela.</summary>
    public event Action? StateChanged;

    /// <summary>Liga ou desliga conforme <c>CelularLigado</c> na configuração (D10). Thread da tela.</summary>
    public void Apply()
    {
        if (_busy) return;
        var want = _settings.Current.CelularLigado;
        if (want && !IsRunning && !_starting) Start();
        else if (!want && (IsRunning || _starting)) Stop();
    }

    private void Start()
    {
        _busy = true;
        try
        {
            // O código nasce na primeira vez que o recurso é ligado (D11)
            if (_settings.Current.CodigoCelular is null)
                _settings.Update(s => s with { CodigoCelular = RemoteRules.NewCode() });

            // D12: a porta salva primeiro. Se estiver ocupada (TIME_WAIT de um fechamento recente), espera até 10 s
            // sem travar a tela e só então passa às outras da faixa.
            var order = RemoteRules.PortOrder(_settings.Current.PortaCelular);
            if (TryBind(order[0])) return;
            _starting = true;
            _startCts = new CancellationTokenSource();
            _ = StartRetryAsync(order, _startCts.Token);
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

    private bool TryBind(int port)
    {
        var address = _localOnly ? IPAddress.Loopback : IPAddress.Any;
        var listener = new TcpListener(address, port) { ExclusiveAddressUse = true };
        try
        {
            listener.Start(16);
        }
        catch (SocketException)
        {
            listener.Dispose();
            return false; // porta ocupada
        }
        _listener = listener;
        Port = port;
        _cts = new CancellationTokenSource();
        if (port != _settings.Current.PortaCelular) _settings.Update(s => s with { PortaCelular = port });
        Log.Write($"celular: servidor ligado em {address}:{port}");
        _ = AcceptLoopAsync(listener, _cts.Token);
        return true;
    }

    /// <summary>Continua na thread da tela (sem ConfigureAwait): tentativas 2 a 10 na porta salva, depois o resto da faixa.</summary>
    private async Task StartRetryAsync(IReadOnlyList<int> order, CancellationToken ct)
    {
        try
        {
            Log.Write($"celular: porta {order[0]} ocupada, tentando por ate {PortAttempts} s");
            for (var i = 1; i < PortAttempts; i++)
            {
                await Task.Delay(PortWait, ct);
                if (TryBind(order[0])) return;
            }
            for (var i = 1; i < order.Count; i++)
                if (TryBind(order[i])) return;
            Log.Write($"ERRO celular: nenhuma porta livre de {FirstPort} a {LastPort}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: {ex.Message}");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _starting = false;
                _startCts?.Dispose();
                _startCts = null;
            }
            StateChanged?.Invoke();
        }
    }

    private void Stop()
    {
        _busy = true;
        try
        {
            if (_starting)
            {
                _starting = false;
                try { _startCts?.Cancel(); } catch (ObjectDisposedException) { }
                _startCts?.Dispose();
                _startCts = null;
            }
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

                if (path == "/tela/ws")
                {
                    // Fase 10, D1: o canal da imagem vive além do tempo do cabeçalho e não conta como requisição aberta
                    Interlocked.Decrement(ref _openRequests);
                    counted = false;
                    await HandleScreenSocketAsync(stream, head, serverCt).ConfigureAwait(false);
                    return;
                }

                if (RemoteRules.IsUpload(head))
                {
                    // Fase 9d, D26: o corpo é lido em blocos direto para o disco, com prazo de 30 s sem dados (não os 10 s da leitura comum)
                    await ServeUploadAsync(stream, head, q >= 0 ? head.Path.Substring(q + 1) : null, body, serverCt).ConfigureAwait(false);
                    return;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
                cts.CancelAfter(HeadTimeout);
                var ct = cts.Token;
                if (head.Method == "GET" && path == "/")
                    await ServePageAsync(stream, ct).ConfigureAwait(false);
                else if (head.Method == "GET" && (path == "/novo" || path == "/novo/"))
                    await ServeNewPageAsync(stream, ct).ConfigureAwait(false);
                else if (head.Method == "GET" && (path.StartsWith("/app/", StringComparison.Ordinal) || path.StartsWith("/fontes/", StringComparison.Ordinal)))
                    await ServeAssetAsync(stream, head, path, ct).ConfigureAwait(false);
                else if (head.Method == "GET" && path == "/sessao")
                    // Fase 10b, D11: só diz se o cookie é de um celular pareado; não exige o celular conectado (sem 409)
                    await WriteAsync(stream, Authenticate(head) is null ? 401 : 204, "text/plain; charset=utf-8", Array.Empty<byte>(), null, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Lê o cabeçalho (máx. 8 KB) e o corpo (máx. 1 KB). Status 0 = ok; positivo = responder com ele; negativo = só fechar.
    /// No <c>POST /arquivo</c> (D26) o corpo não é lido: <c>Body</c> traz só os bytes que já vieram junto do cabeçalho.
    /// </summary>
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
        if (RemoteRules.IsUpload(head)) return new RequestRead(0, head, buffer.AsSpan(end, total - end).ToArray());
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
        204 => "No Content",
        304 => "Not Modified",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        411 => "Length Required",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        431 => "Request Header Fields Too Large",
        503 => "Service Unavailable",
        507 => "Insufficient Storage",
        _ => "Error",
    };

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body, string[]? extraHeaders, CancellationToken ct, string cacheControl = "no-store")
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
        if (status != 304) // D6: 304 sem Content-Type e sem Content-Length
        {
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        }
        sb.Append("Connection: close\r\n");
        sb.Append("Cache-Control: ").Append(cacheControl).Append("\r\n");
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
        // D7: a página antiga é a concatenação das partes, na ordem de WebAssets.OldPageParts
        using var ms = new MemoryStream();
        var asm = Assembly.GetExecutingAssembly();
        foreach (var part in WebAssets.OldPageParts)
        {
            using var res = asm.GetManifestResourceStream(WebAssets.OldPagePrefix + part);
            if (res is null)
            {
                await WriteAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("pagina indisponivel"), null, ct).ConfigureAwait(false);
                return;
            }
            await res.CopyToAsync(ms, ct).ConfigureAwait(false);
        }
        await WriteAsync(stream, 200, "text/html; charset=utf-8", ms.ToArray(), null, ct).ConfigureAwait(false);
    }

    // ---- GET /novo (Fase 14, D1) ----

    private static async Task ServeNewPageAsync(NetworkStream stream, CancellationToken ct)
    {
        var bytes = await ReadResourceAsync(NewPageResource, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            await WriteAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("pagina indisponivel"), null, ct).ConfigureAwait(false);
            return;
        }
        await WriteAsync(stream, 200, "text/html; charset=utf-8", bytes, null, ct).ConfigureAwait(false);
    }

    // ---- GET /app/* e /fontes/* (Fase 14, D1, D5, D6) ----

    private static async Task ServeAssetAsync(NetworkStream stream, HttpHead head, string path, CancellationToken ct)
    {
        var asset = WebAssets.Resolve(path);
        var bytes = asset is null ? null : await ReadResourceAsync(asset.Resource, ct).ConfigureAwait(false);
        if (asset is null || bytes is null)
        {
            await WriteAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("nao encontrado"), null, ct).ConfigureAwait(false);
            return;
        }
        if (!AssetETags.TryGetValue(asset.Resource, out var etag))
        {
            etag = WebAssets.ETagFor(bytes);
            AssetETags[asset.Resource] = etag;
        }
        var extra = new[] { "ETag: " + etag };
        if (asset.CacheControl == WebAssets.NoCache && WebAssets.IfNoneMatchMatches(head.Header("If-None-Match"), etag))
        {
            await WriteAsync(stream, 304, asset.ContentType, Array.Empty<byte>(), extra, ct, asset.CacheControl).ConfigureAwait(false);
            return;
        }
        await WriteAsync(stream, 200, asset.ContentType, bytes, extra, ct, asset.CacheControl).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadResourceAsync(string name, CancellationToken ct)
    {
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (res is null) return null;
        using var ms = new MemoryStream();
        await res.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
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
                    if (RemoteProtocol.IconId(g.Key) == id) return _hub.GroupIconKey(g.Key, g.ExePath);
                foreach (var m in _hub.Media)
                    if (m.Key is null && RemoteProtocol.IconId(RemoteSession.MediaKeyPrefix + m.Aumid) == id) return _hub.MediaIconKey(m.Aumid);
                return null;
            }).ConfigureAwait(false);
            // Fase 9c, D21: ícone de uma janela da última lista enviada ao celular (o mapa só existe com o grupo janelas ligado)
            exe ??= WindowList.IconPathFor(id);
        }
        var png = exe is null ? Array.Empty<byte>() : await AppIconCache.GetPngAsync(exe).ConfigureAwait(false);
        if (png.Length == 0)
        {
            await WriteAsync(stream, 404, "text/plain; charset=utf-8", Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        await WriteAsync(stream, 200, "image/png", png, new[] { "Cache-Control: private, max-age=3600" }, ct).ConfigureAwait(false);
    }

    // ---- GET /tela/ws (Fase 10, D1 a D4, D9) ----

    /// <summary>Configuração atual do canal da tela (troca atômica da referência).</summary>
    private sealed record ScreenConfig(int Monitor, int Width, ScreenQuality Quality, double Cursor = 1.0, bool Pointer = false);

    /// <summary>Estado compartilhado entre o recebimento e o envio de um canal da tela.</summary>
    private sealed class ScreenChannel
    {
        public ScreenConfig? Config;
        public readonly SemaphoreSlim Credit = new(0, 1);
        public bool Started;
        public string LastQuality = "normal";

        /// <summary>Libera um quadro (o primeiro depois da configuração, os outros a cada "ok"); nunca acumula.</summary>
        public void GrantFrame()
        {
            try { Credit.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    /// <summary>0 se o celular ainda pode ver a tela; senão o status HTTP da recusa (409 outro celular, 403 grupo desligado).</summary>
    private int ScreenAccess(string phoneHash)
    {
        string? connected;
        lock (_gate) connected = _sessions.Count > 0 ? _sessions[0].KeyHash : null;
        if (!string.Equals(connected, phoneHash, StringComparison.Ordinal)) return 409;
        var cfg = _settings.Current;
        if (!cfg.CelularLigado || !RemoteTools.IsEnabled(cfg.FerramentasCelular, RemoteTools.Screen)) return 403;
        return 0;
    }

    /// <summary>
    /// Canal WebSocket da imagem da tela. 401 sem cookie válido; 409 se a chave não é a do celular conectado agora; 403 com o
    /// grupo "tela" desligado ou <c>Origin</c> diferente do <c>Host</c>; 400 se não é um pedido de WebSocket; 503 se já há um
    /// canal aberto. Depois do upgrade, só quadros binários (JPEG), um por vez (a página confirma com <c>{"ok":1}</c>).
    /// </summary>
    private async Task HandleScreenSocketAsync(NetworkStream stream, HttpHead head, CancellationToken serverCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        cts.CancelAfter(HeadTimeout);
        var ct = cts.Token;
        const string text = "text/plain; charset=utf-8";
        var phone = Authenticate(head);
        if (phone is null)
        {
            await WriteAsync(stream, 401, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        var denied = ScreenAccess(phone.Hash);
        if (denied != 0)
        {
            await WriteAsync(stream, denied, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        if (!RemoteRules.IsWebSocketUpgrade(head))
        {
            await WriteAsync(stream, 400, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        if (!RemoteRules.OriginMatchesHost(head.Header("Origin"), head.Header("Host")))
        {
            await WriteAsync(stream, 403, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        if (Interlocked.CompareExchange(ref _screenBusy, 1, 0) != 0)
        {
            await WriteAsync(stream, 503, text, Array.Empty<byte>(), null, ct).ConfigureAwait(false);
            return;
        }
        WebSocket? socket = null;
        ScreenCapture? capture = null;
        var channel = new ScreenChannel();
        try
        {
            var accept = RemoteRules.ComputeWebSocketAccept(head.Header("Sec-WebSocket-Key")!);
            var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            cts.CancelAfter(Timeout.InfiniteTimeSpan);

            socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                KeepAliveInterval = TimeSpan.FromSeconds(20),
            });
            capture = new ScreenCapture();
            Log.Write("celular: tela compartilhada");
            using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            var receive = ScreenReceiveLoopAsync(socket, channel, loopCts);
            var send = ScreenSendLoopAsync(socket, capture, channel, phone.Hash, loopCts.Token);
            await Task.WhenAny(receive, send).ConfigureAwait(false);
            if (socket.State == WebSocketState.CloseReceived) await CloseScreenAsync(socket, 1000, "").ConfigureAwait(false);
            loopCts.Cancel();
            try { await Task.WhenAll(receive, send).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException or WebSocketException) { }
        }
        finally
        {
            if (capture is not null)
            {
                var n = capture.Sent;
                var ms = n > 0 ? capture.TotalMs / n : 0;
                Log.Write($"celular: tela encerrada ({n} quadros, {capture.Skipped} iguais, {ms:0} ms por quadro, {channel.LastQuality})");
                capture.Dispose();
            }
            socket?.Dispose();
            channel.Credit.Dispose();
            Interlocked.Exchange(ref _screenBusy, 0);
        }
    }

    /// <summary>Lê as mensagens de texto da página: configuração (tela, largura, qualidade) e "ok" (quadro desenhado).</summary>
    private static async Task ScreenReceiveLoopAsync(WebSocket socket, ScreenChannel channel, CancellationTokenSource loopCts)
    {
        var ct = loopCts.Token;
        var buffer = new byte[512];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var filled = 0;
                var tooBig = false;
                ValueWebSocketReceiveResult r;
                do
                {
                    r = await socket.ReceiveAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (filled + r.Count >= buffer.Length) { tooBig = true; filled = 0; }
                    else filled += r.Count;
                } while (!r.EndOfMessage);
                if (tooBig || r.MessageType != WebSocketMessageType.Text) continue;
                var msg = ScreenStreamRules.Parse(buffer.AsSpan(0, filled));
                switch (msg.Kind)
                {
                    case StreamMessageKind.Config:
                    {
                        var q = ScreenRules.QualityFor(msg.Quality);
                        channel.LastQuality = q.Id;
                        Volatile.Write(ref channel.Config, new ScreenConfig(msg.Monitor, msg.Width, q, msg.Cursor, msg.Pointer));
                        if (!channel.Started) { channel.Started = true; channel.GrantFrame(); }
                        break;
                    }
                    case StreamMessageKind.Ok:
                        if (channel.Started) channel.GrantFrame();
                        break;
                }
            }
        }
        finally
        {
            loopCts.Cancel();
        }
    }

    /// <summary>
    /// Laço de captura: com o "ok" do quadro anterior e o intervalo da qualidade vencido, captura; quadro igual ao anterior
    /// não vai e o laço tenta de novo depois do intervalo (D2 e D4). No máximo um quadro em trânsito.
    /// </summary>
    private async Task ScreenSendLoopAsync(WebSocket socket, ScreenCapture capture, ScreenChannel channel, string phoneHash, CancellationToken ct)
    {
        long lastStart = 0;
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await channel.Credit.WaitAsync(ct).ConfigureAwait(false);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (ScreenAccess(phoneHash) != 0)
                {
                    await CloseScreenAsync(socket, 4003, "recusado").ConfigureAwait(false);
                    return;
                }
                var cfg = Volatile.Read(ref channel.Config)!;
                var since = lastStart == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(lastStart);
                if (!ScreenStreamRules.CanCapture(true, since, cfg.Quality.Interval))
                    await Task.Delay(ScreenStreamRules.RemainingDelay(since, cfg.Quality.Interval), ct).ConfigureAwait(false);
                lastStart = Stopwatch.GetTimestamp();
                var frame = await capture.CaptureAsync(cfg.Monitor, cfg.Width, cfg.Quality, cfg.Cursor).ConfigureAwait(false);
                if (frame.Failed)
                {
                    await CloseScreenAsync(socket, 4004, "indisponivel").ConfigureAwait(false);
                    return;
                }
                if (frame.Jpeg is null) continue; // igual ao anterior: não vai, tenta de novo no próximo intervalo
                // Fase 15c, D16 e D21: só com p:1 e junto do quadro (nenhum timer); sem p:1 o canal manda só o quadro, como sempre
                if (cfg.Pointer && capture.LastPointer is { } ptr)
                    await socket.SendAsync(Encoding.UTF8.GetBytes(ScreenStreamRules.PointerMessage(ptr.X, ptr.Y)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                await socket.SendAsync(frame.Jpeg, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
                break;
            }
        }
    }

    private static async Task CloseScreenAsync(WebSocket socket, int code, string reason)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await socket.CloseOutputAsync((WebSocketCloseStatus)code, reason, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // a página já saiu
        }
    }

    // ---- POST /arquivo?nome=<nome> (Fase 9d, D20 e D26) ----

    /// <summary>
    /// Recebe um arquivo do celular. Ordem: 401 (sem cookie válido), 409 (a chave não é a do celular conectado agora), 403 (grupo
    /// desligado ou <c>Origin</c> diferente do <c>Host</c>), 411/400 (sem <c>Content-Length</c> ou vazio), 413 (acima do limite),
    /// 409 (já há um envio) e 507 (sem espaço). Resposta de erro não lê o corpo: o que o celular ainda manda é descartado (até 16 MB).
    /// </summary>
    private async Task ServeUploadAsync(NetworkStream stream, HttpHead head, string? query, byte[] prefetched, CancellationToken serverCt)
    {
        const string json = "application/json; charset=utf-8";
        const string fail = "{\"ok\":false}";
        var length = RemoteRules.UploadLength(head);

        async Task Reply(int status, string text)
        {
            using var c = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            c.CancelAfter(HeadTimeout);
            await WriteAsync(stream, status, json, Encoding.UTF8.GetBytes(text), null, c.Token).ConfigureAwait(false);
            if (status != 200) await DrainAsync(stream, length - prefetched.Length, serverCt).ConfigureAwait(false);
        }

        var phone = Authenticate(head);
        if (phone is null) { await Reply(401, fail).ConfigureAwait(false); return; }
        string? connected;
        lock (_gate) connected = _sessions.Count > 0 ? _sessions[0].KeyHash : null;
        if (!string.Equals(connected, phone.Hash, StringComparison.Ordinal)) { await Reply(409, fail).ConfigureAwait(false); return; }
        var cfg = _settings.Current;
        var origin = head.Header("Origin");
        if (!cfg.CelularLigado || !RemoteTools.IsEnabled(cfg.FerramentasCelular, RemoteTools.Transfer)
            || (origin is not null && !RemoteRules.OriginMatchesHost(origin, head.Header("Host"))))
        {
            await Reply(403, fail).ConfigureAwait(false);
            return;
        }
        if (length < 0) { await Reply(411, fail).ConfigureAwait(false); return; }
        if (length == 0) { await Reply(400, fail).ConfigureAwait(false); return; }

        var receiver = await OnUi(() => App.Pc.Files).ConfigureAwait(false);
        var result = await receiver.ReceiveAsync(stream, prefetched, length, FileNameRules.NameFromQuery(query), serverCt).ConfigureAwait(false);
        if (result.Status == 0) return; // o celular desistiu ou ficou 30 s sem mandar nada: não há quem responda
        if (result.Status != 200)
        {
            Log.Write($"celular: arquivo recusado ({result.Status})");
            await Reply(result.Status, fail).ConfigureAwait(false);
            return;
        }
        Log.Write($"celular: arquivo recebido ({result.Bytes} bytes)");
        var name = result.Name ?? "";
        await OnUi(() => { App.Pc.Balloon($"Arquivo recebido: {name}", "Arquivo recebido"); return true; }).ConfigureAwait(false);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteBoolean("ok", true);
            w.WriteString("nome", name);
            w.WriteNumber("bytes", result.Bytes);
            w.WriteEndObject();
        }
        await Reply(200, Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)).ConfigureAwait(false);
    }

    /// <summary>Descarta o que o celular ainda manda depois de uma resposta de erro (até 16 MB, 3 s sem dados), para o navegador ler a resposta.</summary>
    private static async Task DrainAsync(NetworkStream stream, long remaining, CancellationToken serverCt)
    {
        const long MaxDrain = 16L * 1024 * 1024;
        if (remaining <= 0) return;
        var left = Math.Min(remaining, MaxDrain);
        var buffer = new byte[16 * 1024];
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            while (left > 0)
            {
                idle.CancelAfter(TimeSpan.FromSeconds(3));
                var n = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), idle.Token).ConfigureAwait(false);
                if (n == 0) break;
                left -= n;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
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
            // Fase 9, D3: um celular por vez, decidido dentro do lock. A recusa vem depois do upgrade (o navegador não lê
            // o status HTTP de um WebSocket recusado): a página recebe "ocupado" e o código 4009.
            RemoteSession? replaced = null;
            string? busyName = null;
            lock (_gate)
            {
                var current = _sessions.Count > 0 ? _sessions[0] : null;
                switch (SingleSessionPolicy.Decide(current is null ? null : current.KeyHash ?? "", phone.Hash))
                {
                    case SessionDecision.Accept:
                    case SessionDecision.Replace:
                        if (current is not null) { replaced = current; _sessions.Remove(current); }
                        session = new RemoteSession(this, _hub, socket, _ui, Interlocked.Increment(ref _nextNumber))
                        {
                            KeyHash = phone.Hash,
                            PhoneName = phone.Nome,
                        };
                        _sessions.Add(session);
                        break;
                    default:
                        busyName = current!.PhoneName;
                        break;
                }
                Volatile.Write(ref _connections, _sessions.Count);
            }
            if (session is null)
            {
                Log.Write("celular: outro celular recusado (ocupado)");
                await RefuseAsync(socket, busyName ?? "").ConfigureAwait(false);
                return;
            }
            if (replaced is not null)
            {
                Log.Write("celular: conexao antiga substituida (mesma chave)");
                replaced.Finish(RemoteProtocol.BuildDisconnected(), 4001, "substituida");
            }
            total = ConnectionCount;
            Log.Write($"celular conectado ({total} no total)");
            ConnectionsChanged?.Invoke();
            _ui.TryEnqueue(() => TouchPhone(phone.Hash));
            await session.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            if (session is not null)
            {
                bool removed;
                int left;
                lock (_gate)
                {
                    removed = _sessions.Remove(session);
                    left = _sessions.Count;
                    Volatile.Write(ref _connections, left);
                }
                if (removed)
                {
                    Log.Write($"celular desconectado ({left})");
                    ConnectionsChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>Manda "ocupado" e fecha com 4009; espera um instante o fecho do celular para a mensagem chegar inteira.</summary>
    private static async Task RefuseAsync(WebSocket socket, string name)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var bytes = Encoding.UTF8.GetBytes(RemoteProtocol.BuildBusy(name));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
            await socket.CloseOutputAsync((WebSocketCloseStatus)4009, "ocupado", cts.Token).ConfigureAwait(false);
            var buffer = new byte[256];
            while (socket.State is WebSocketState.CloseSent or WebSocketState.Open)
            {
                var r = await socket.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch
        {
            // o celular já saiu
        }
        finally
        {
            socket.Dispose();
        }
    }

    /// <summary>Nome do celular conectado agora, ou nulo se não há nenhum.</summary>
    public string? ConnectedPhoneName
    {
        get
        {
            lock (_gate) return _sessions.Count > 0 ? _sessions[0].PhoneName : null;
        }
    }

    /// <summary>"Desconectar" (D4): manda "desconectado" e fecha com 4000. A chave continua pareada. Qualquer thread.</summary>
    public void DisconnectAll()
    {
        RemoteSession[] all;
        lock (_gate) all = _sessions.ToArray();
        if (all.Length == 0) return;
        Log.Write("celular: desconectado pelo PC");
        foreach (var s in all) s.Finish(RemoteProtocol.BuildDisconnected(), 4000, "desconectado");
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
