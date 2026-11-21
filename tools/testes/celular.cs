#:property TargetFramework=net10.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618;SYSLIB1096

// Celular de teste (Fase 6): cliente HTTP e WebSocket do servidor do app. Só fala com 127.0.0.1 (o app roda com --celular-local).
//   celular.exe parear <porta> <codigo>              -> "<status HTTP>|<chave>" (a chave só vem no 200; vazia nos outros)
//   celular.exe estado <porta> <chave>               -> conecta, imprime a primeira mensagem de estado em uma linha e desconecta
//   celular.exe enviar <porta> <chave> <json>        -> envia um comando (ver SEGURANCA) e imprime "enviado"
//   celular.exe ciclos <porta> <chave> <n>           -> conecta e desconecta n vezes (espera o estado a cada conexão); imprime "ciclos=<ok>"
//   celular.exe get <porta> <caminho> [chave|-] [origin|-]
//                                                    -> GET cru; imprime só o código HTTP. Em /ws manda os cabeçalhos de upgrade.
//   celular.exe segurar <porta> <chave> <segundos> [arquivo-parar]
//                                                    -> fica conectado e imprime "PRONTO" e depois "<epoch ms>\t<estado>" a cada
//                                                       mensagem; sai ao passar o tempo ou quando o arquivo-parar existir
// SEGURANÇA: "enviar" só aceita volume, mudo e rota para apps cujo nome comece com "MX-teste" (confere no estado recebido). O comando
// "midia" (Fase 7) vale só para apps "MX-teste*" ou para linhas só de mídia (soMidia) cujo nome comece com "tocar"; o resto é recusado. Também
// RECUSA SEMPRE (código 3) geral, geralMudo e padrao, e qualquer tipo desconhecido. Nenhum comando mexe na saída padrão nem no
// volume ou mudo gerais. Recusa qualquer porta que não seja a do 127.0.0.1.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

const string Host = "127.0.0.1";
if (args.Length == 0) return Uso();

try
{
    switch (args[0])
    {
        case "parear" when args.Length >= 3:
            return await Parear(int.Parse(args[1]), args[2]);
        case "estado" when args.Length >= 3:
        {
            using var c = await Conectar(int.Parse(args[1]), args[2]);
            Console.WriteLine(await ProximoEstado(c, 5000) ?? "");
            return 0;
        }
        case "enviar" when args.Length >= 4:
            return await Enviar(int.Parse(args[1]), args[2], args[3]);
        case "ciclos" when args.Length >= 4:
        {
            var porta = int.Parse(args[1]);
            var n = int.Parse(args[3]);
            var ok = 0;
            for (var i = 0; i < n; i++)
            {
                using var c = await Conectar(porta, args[2]);
                if (await ProximoEstado(c, 5000) is not null) ok++;
            }
            Console.WriteLine($"ciclos={ok}");
            return ok == n ? 0 : 1;
        }
        case "get" when args.Length >= 3:
            return await Get(int.Parse(args[1]), args[2], args.Length > 3 && args[3] != "-" ? args[3] : null, args.Length > 4 && args[4] != "-" ? args[4] : null);
        case "segurar" when args.Length >= 4:
            return await Segurar(int.Parse(args[1]), args[2], int.Parse(args[3]), args.Length > 4 ? args[4] : null);
        default:
            return Uso();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("erro: " + ex.Message);
    return 2;
}

static int Uso()
{
    Console.Error.WriteLine("uso: parear <porta> <codigo> | estado <porta> <chave> | enviar <porta> <chave> <json> | ciclos <porta> <chave> <n> | get <porta> <caminho> [chave|-] [origin|-] | segurar <porta> <chave> <segundos> [arquivo-parar]");
    return 2;
}

static async Task<int> Parear(int porta, string codigo)
{
    using var http = new HttpClient(new HttpClientHandler { UseCookies = false });
    using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{Host}:{porta}/parear")
    {
        Content = new StringContent(JsonSerializer.Serialize(new { codigo }), Encoding.UTF8, "application/json"),
    };
    req.Headers.UserAgent.ParseAdd("celular-de-teste (Android)");
    using var resp = await http.SendAsync(req);
    var chave = "";
    if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
    {
        foreach (var c in cookies)
        {
            var primeiro = c.Split(';')[0].Trim();
            if (primeiro.StartsWith("mx=", StringComparison.Ordinal)) chave = primeiro.Substring(3);
        }
    }
    Console.WriteLine($"{(int)resp.StatusCode}|{chave}");
    return 0;
}

static async Task<ClientWebSocket> Conectar(int porta, string chave)
{
    var ws = new ClientWebSocket();
    ws.Options.Cookies = new CookieContainer();
    ws.Options.Cookies.Add(new Uri($"http://{Host}:{porta}/"), new Cookie("mx", chave));
    ws.Options.SetRequestHeader("Origin", $"http://{Host}:{porta}");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await ws.ConnectAsync(new Uri($"ws://{Host}:{porta}/ws"), cts.Token);
    return ws;
}

// Lê mensagens até chegar uma de estado (ou passar o tempo). Devolve o JSON em uma linha.
static async Task<string?> ProximoEstado(ClientWebSocket ws, int timeoutMs)
{
    using var cts = new CancellationTokenSource(timeoutMs);
    while (ws.State == WebSocketState.Open)
    {
        var texto = await Ler(ws, cts.Token);
        if (texto is null) return null;
        if (EhEstado(texto)) return texto;
    }
    return null;
}

static bool EhEstado(string json)
{
    try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("t", out var t) && t.GetString() == "estado"; }
    catch (JsonException) { return false; }
}

static async Task<string?> Ler(ClientWebSocket ws, CancellationToken ct)
{
    var buffer = new byte[64 * 1024];
    var len = 0;
    try
    {
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, len, buffer.Length - len), ct);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            len += r.Count;
        } while (!r.EndOfMessage);
    }
    catch (OperationCanceledException) { return null; }
    return Encoding.UTF8.GetString(buffer, 0, len);
}

static async Task<int> Enviar(int porta, string chave, string json)
{
    string tipo;
    string? app = null;
    try
    {
        using var cmd = JsonDocument.Parse(json);
        tipo = cmd.RootElement.GetProperty("t").GetString() ?? "";
        if (cmd.RootElement.TryGetProperty("app", out var a)) app = a.GetString();
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
    {
        Console.Error.WriteLine("RECUSADO: comando invalido");
        return 3;
    }
    if (tipo is "geral" or "geralMudo" or "padrao")
    {
        Console.Error.WriteLine($"RECUSADO: '{tipo}' mexeria no volume geral ou na saida padrao; o celular de teste nunca faz isso.");
        return 3;
    }
    if (tipo is not ("volume" or "mudo" or "rota" or "midia") || string.IsNullOrEmpty(app))
    {
        Console.Error.WriteLine("RECUSADO: so volume, mudo, rota e midia de uma fonte de teste.");
        return 3;
    }

    using var c = await Conectar(porta, chave);
    var estado = await ProximoEstado(c, 5000);
    if (estado is null) { Console.Error.WriteLine("erro: sem estado"); return 2; }
    string? nome = null;
    var soMidia = false;
    using (var doc = JsonDocument.Parse(estado))
    {
        foreach (var item in doc.RootElement.GetProperty("apps").EnumerateArray())
            if (item.GetProperty("chave").GetString() == app)
            {
                nome = item.GetProperty("nome").GetString();
                soMidia = item.TryGetProperty("soMidia", out var sm) && sm.ValueKind == JsonValueKind.True;
            }
    }
    var ehTeste = nome is not null && (nome.StartsWith("MX-teste", StringComparison.Ordinal)
        || (tipo == "midia" && soMidia && nome.StartsWith("tocar", StringComparison.Ordinal)));
    if (!ehTeste)
    {
        Console.Error.WriteLine($"RECUSADO: '{nome ?? app}' nao e um tocador de teste (MX-teste).");
        return 3;
    }
    await c.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
    // Dá tempo ao app de processar antes de a conexão fechar.
    await Task.Delay(400);
    Console.WriteLine("enviado");
    return 0;
}

static async Task<int> Get(int porta, string caminho, string? chave, string? origin)
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(Host, porta);
    var stream = tcp.GetStream();
    var sb = new StringBuilder();
    sb.Append($"GET {caminho} HTTP/1.1\r\nHost: {Host}:{porta}\r\nConnection: close\r\n");
    if (chave is not null) sb.Append($"Cookie: mx={chave}\r\n");
    if (origin is not null) sb.Append($"Origin: {origin}\r\n");
    if (caminho == "/ws") sb.Append("Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
    sb.Append("\r\n");
    await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
    var buffer = new byte[256];
    using var cts = new CancellationTokenSource(5000);
    var n = await stream.ReadAsync(buffer, cts.Token);
    var linha = Encoding.ASCII.GetString(buffer, 0, n).Split("\r\n")[0];
    var partes = linha.Split(' ');
    Console.WriteLine(partes.Length >= 2 ? partes[1] : "0");
    return 0;
}

static async Task<int> Segurar(int porta, string chave, int segundos, string? arquivoParar)
{
    using var c = await Conectar(porta, chave);
    var relogio = Stopwatch.StartNew();
    using var parar = new CancellationTokenSource();
    var leitor = Task.Run(async () =>
    {
        while (c.State == WebSocketState.Open && !parar.IsCancellationRequested)
        {
            var texto = await Ler(c, parar.Token);
            if (texto is null) return;
            if (EhEstado(texto))
            {
                if (relogio.ElapsedMilliseconds >= 0) Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\t{texto}");
                Console.Out.Flush();
            }
        }
    });
    Console.WriteLine("PRONTO");
    Console.Out.Flush();
    while (relogio.Elapsed.TotalSeconds < segundos && !leitor.IsCompleted)
    {
        if (arquivoParar is not null && File.Exists(arquivoParar)) break;
        await Task.Delay(100);
    }
    parar.Cancel();
    try { await leitor; } catch { }
    try { await c.CloseAsync(WebSocketCloseStatus.NormalClosure, "fim", new CancellationTokenSource(2000).Token); } catch { }
    return 0;
}
