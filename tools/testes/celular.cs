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
//   celular.exe cabecalhos <porta> <caminho> [etag|-] [chave|-]
//                                                    -> (Fase 14) GET sem cookie (paginas e arquivos estaticos) com If-None-Match opcional; le a resposta ate fechar e imprime
//                                                       status|content-type|cache-control|etag|sha256-do-corpo|bytes|sha256-do-corpo-sem-CR (cabecalho ausente = vazio)
//                                                       (Fase 16) mais dois campos no fim: |x-frame-options|content-security-policy
//                                                       (Fase 17) mais tres: |location|quantidade-de-Cache-Control|1-se-o-corpo-cita-/app/js/app.js ; o 4o argumento manda o cookie mx
//   celular.exe hostget <porta> <caminho> <host> [chave|-]
//                                                    -> (Fase 16) GET cru com o cabecalho Host que voce mandar (so conecta no 127.0.0.1); imprime so o codigo HTTP.
//                                                       Em /ws e /tela/ws manda os cabecalhos de upgrade, com Origin igual ao Host.
//   celular.exe rajada <porta> <n> [codigo]          -> (Fase 16) abre n conexoes ao mesmo tempo, manda o cabecalho de POST /parear de todas e SEGURA o corpo;
//                                                       depois de todas conectadas le as respostas que ja vieram (as bloqueadas, sem esperar o corpo), solta o corpo
//                                                       ({"codigo":"<codigo>"}, padrao 000000, que tem de ser ERRADO) e le o resto. Imprime "401=<a> 429=<b> outros=<c>".
//   celular.exe ferramenta <porta> <chave> <json>    -> (Fase 9) conecta, confere que o estado traz "teste":true (o app esta com --teste-pc) e so
//                                                       entao envia o comando de ferramenta (energia, energiaCancelar, projecao, telas, atualizar,
//                                                       desconectar); sem "teste":true RECUSA (codigo 3) sem enviar nada
//                                                       (Fase 9c: tambem janelas, janela, atalho, sono e assinar; as mensagens avulsas recebidas logo depois do envio,
//                                                       como a lista de janelas, saem como "MSG<TAB><json>")
//   celular.exe tela <porta> <chave|-> <passos> -> (Fase 10) abre UM canal WebSocket /tela/ws e roda os passos em sequencia, no mesmo canal.
//                                                       <passos> = "<qualidade>:<mon>:<w>:<segundos>:<ok|semok>" separados por ';'. Cada passo manda a configuracao
//                                                       {"mon":..,"w":..,"q":".."}, le quadros binarios por <segundos> e, com "ok", responde {"ok":1} a cada quadro.
//                                                       Imprime por passo "PASSO<TAB>i<TAB>q<TAB>quadros<TAB>bytes-medios<TAB>ultimoLxA<TAB>segundos<TAB>jpeg|nao-jpeg<TAB>textos<TAB>pares<TAB>fora<TAB>sem-texto" e, no fim, "FIM".
//                                                       (Fase 15c) Depois do ok|semok, dois campos opcionais: ":<cur>" (tamanho do ponteiro; "-" = nao manda) e ":p" (manda "p":1).
//                                                       Os quatro ultimos numeros contam os textos {"p":[x,y]} recebidos, os quadros que vieram logo depois de um texto,
//                                                       os textos com x ou y fora de 0..1 e os quadros sem texto antes (sem "p", textos e pares sao 0).
//                                                       Se o servidor fechar o canal: "FECHOU<TAB>codigo" (codigo de saida 5). Recusa no upgrade: "RECUSADO<TAB>msg" (codigo 4).
//                                                       "-" no lugar da chave = sem cookie. NUNCA grava a imagem em disco (so o cabecalho e as dimensoes do JPEG).
//   celular.exe segundo <porta> <chave>              -> (Fase 9) conecta como OUTRO celular (outra chave), nao envia nada, imprime
//                                                       "MSG<TAB><json>" por mensagem e "FECHOU<TAB><codigo>" ao fechar
//   celular.exe arquivo <porta> <chave> <caminho> [nome]
//                                                    -> (Fase 9d) conecta, confere que o estado traz "teste":true (senao RECUSA, codigo 3, sem enviar nada)
//                                                       e entao faz POST /arquivo?nome=<nome> com o corpo cru do arquivo (so de %TEMP%\mixer-check\);
//                                                       imprime "<status HTTP><TAB><corpo da resposta>". [nome] vazio = o nome do arquivo.
//   celular.exe info <porta> <chave> <seg-ligado> <seg-depois>
//                                                    -> (Fase 9d) confere "teste":true, assina o estado do PC (assinar info on), imprime "INFO<TAB><json>"
//                                                       por mensagem durante seg-ligado s, cancela a assinatura (off) e imprime "DEPOIS<TAB><json>" das
//                                                       mensagens info que ainda chegarem em seg-depois s; termina com "fim"
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
using System.Threading.Channels;

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
        case "cabecalhos" when args.Length >= 3:
            return await Cabecalhos(int.Parse(args[1]), args[2], args.Length > 3 ? args[3] : null, args.Length > 4 && args[4] != "-" ? args[4] : null);
        case "get" when args.Length >= 3:
            return await Get(int.Parse(args[1]), args[2], args.Length > 3 && args[3] != "-" ? args[3] : null, args.Length > 4 && args[4] != "-" ? args[4] : null);
        case "hostget" when args.Length >= 4:
            return await HostGet(int.Parse(args[1]), args[2], args[3], args.Length > 4 && args[4] != "-" ? args[4] : null);
        case "rajada" when args.Length >= 3:
            return await Rajada(int.Parse(args[1]), int.Parse(args[2]), args.Length > 3 ? args[3] : "000000");
        case "ferramenta" when args.Length >= 4:
            return await Ferramenta(int.Parse(args[1]), args[2], args[3]);
        case "tela" when args.Length >= 4:
            return await Tela(int.Parse(args[1]), args[2], args[3]);
        case "arquivo" when args.Length >= 4:
            return await Arquivo(int.Parse(args[1]), args[2], args[3], args.Length > 4 ? args[4] : null);
        case "info" when args.Length >= 5:
            return await Info(int.Parse(args[1]), args[2], int.Parse(args[3]), int.Parse(args[4]));
        case "brilhorajada" when args.Length >= 6:
            return await BrilhoRajada(int.Parse(args[1]), args[2], int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]));
        case "segundo" when args.Length >= 3:
            return await Segundo(int.Parse(args[1]), args[2]);
        case "segurar" when args.Length >= 4:
            return await Segurar(int.Parse(args[1]), args[2], int.Parse(args[3]), args.Length > 4 ? args[4] : null);
        case "reconectar" when args.Length >= 4:
            return await Reconectar(int.Parse(args[1]), args[2], int.Parse(args[3]), args.Length > 4 ? args[4] : null);
        default:
            return Uso();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("erro: " + ex.Message);
    return 2;
}

// Fase 10b: o celular "lembrado". Segura a conexao e, ao cair, tenta de novo na MESMA porta e com a MESMA chave, com a espera
// crescente da pagina (0,5 / 1 / 2 / 4 / 5 s). Depois de cada falha consulta GET /sessao: so 401 viraria pareamento (aqui vira PAREAR e sai);
// qualquer outra coisa continua tentando. NUNCA chama /parear. Saida: PRONTO, "CONECTOU<TAB>epoch" a cada conexao com estado,
// "CAIU<TAB>epoch", "SESSAO<TAB>status|erro" e "PAREAR".
static async Task<int> Reconectar(int porta, string chave, int segundos, string? arquivoParar)
{
    var relogio = Stopwatch.StartNew();
    var tentativa = 0;
    Console.WriteLine("PRONTO");
    Console.Out.Flush();
    bool Parar() => relogio.Elapsed.TotalSeconds >= segundos || (arquivoParar is not null && File.Exists(arquivoParar));
    while (!Parar())
    {
        try
        {
            using var c = await Conectar(porta, chave);
            var conectou = false;
            using var cts = new CancellationTokenSource();
            while (c.State == WebSocketState.Open && !Parar())
            {
                var leitura = Ler(c, cts.Token);
                while (!leitura.IsCompleted && !Parar()) await Task.Delay(50);
                if (!leitura.IsCompleted) { cts.Cancel(); try { await leitura; } catch { } break; }
                var texto = await leitura;
                if (texto is null) break;
                if (!conectou && EhEstado(texto))
                {
                    conectou = true;
                    tentativa = 0;
                    Console.WriteLine($"CONECTOU\t{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                    Console.Out.Flush();
                }
            }
            if (Parar()) { try { await c.CloseAsync(WebSocketCloseStatus.NormalClosure, "fim", new CancellationTokenSource(2000).Token); } catch { } break; }
        }
        catch (Exception) { }
        Console.WriteLine($"CAIU\t{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        string status;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(Host, porta);
            var st = tcp.GetStream();
            await st.WriteAsync(Encoding.ASCII.GetBytes($"GET /sessao HTTP/1.1\r\nHost: {Host}:{porta}\r\nConnection: close\r\nCookie: mx={chave}\r\n\r\n"));
            var buf = new byte[256];
            using var cts2 = new CancellationTokenSource(3000);
            var n = await st.ReadAsync(buf, cts2.Token);
            var partes = Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0].Split(' ');
            status = partes.Length >= 2 ? partes[1] : "0";
        }
        catch (Exception) { status = "erro"; }
        Console.WriteLine($"SESSAO\t{status}");
        Console.Out.Flush();
        if (status == "401") { Console.WriteLine("PAREAR"); Console.Out.Flush(); return 4; }
        var espera = Math.Min(5000, 500 << Math.Min(tentativa, 4));
        tentativa++;
        var fim = relogio.ElapsedMilliseconds + espera;
        while (relogio.ElapsedMilliseconds < fim && !Parar()) await Task.Delay(50);
    }
    return 0;
}

static int Uso()
{
    Console.Error.WriteLine("uso: parear <porta> <codigo> | estado <porta> <chave> | enviar <porta> <chave> <json> | ciclos <porta> <chave> <n> | get <porta> <caminho> [chave|-] [origin|-] | cabecalhos <porta> <caminho> [etag|-] [chave|-] | hostget <porta> <caminho> <host> [chave|-] | rajada <porta> <n> [codigo] | segurar <porta> <chave> <segundos> [arquivo-parar] | reconectar <porta> <chave> <segundos> [arquivo-parar] | ferramenta <porta> <chave> <json> | brilhorajada <porta> <chave> <tela> <de> <ate> | segundo <porta> <chave> | tela <porta> <chave|-> <q:mon:w:seg:ok|semok;...> | arquivo <porta> <chave> <caminho> [nome] | info <porta> <chave> <seg-ligado> <seg-depois>");
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
            if (r.MessageType == WebSocketMessageType.Close) { Fecho.Codigo = (int?)ws.CloseStatus; return null; }
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
    if (caminho is "/ws" or "/tela/ws") sb.Append("Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
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

// Fase 16 (D1): GET cru com o Host que o roteiro escolher. So conecta no 127.0.0.1; o Host e o que muda.
static async Task<int> HostGet(int porta, string caminho, string host, string? chave)
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(Host, porta);
    var stream = tcp.GetStream();
    var sb = new StringBuilder();
    sb.Append($"GET {caminho} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n");
    if (chave is not null) sb.Append($"Cookie: mx={chave}\r\n");
    if (caminho is "/ws" or "/tela/ws") sb.Append($"Origin: http://{host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
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

// Fase 16 (D2): n pedidos de pareamento ao mesmo tempo, com o corpo seguro ate todos terem mandado o cabecalho.
// O codigo tem que ser ERRADO (a rajada nunca pareia nada).
static async Task<int> Rajada(int porta, int n, string codigo)
{
    var corpo = JsonSerializer.Serialize(new { codigo });
    var conexoes = new List<(TcpClient Tcp, NetworkStream Stream)>();
    for (var i = 0; i < n; i++)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(Host, porta);
        conexoes.Add((tcp, tcp.GetStream()));
    }
    var cab = $"POST /parear HTTP/1.1\r\nHost: {Host}:{porta}\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(corpo)}\r\nConnection: close\r\n\r\n";
    foreach (var c in conexoes) await c.Stream.WriteAsync(Encoding.ASCII.GetBytes(cab));

    var respostas = new string?[n];
    async Task<string?> Ler(int i, int ms)
    {
        try
        {
            using var cts = new CancellationTokenSource(ms);
            using var acumulado = new MemoryStream();
            var buf = new byte[1024];
            while (true)
            {
                var lidos = await conexoes[i].Stream.ReadAsync(buf, cts.Token);
                if (lidos == 0) break;
                acumulado.Write(buf, 0, lidos);
                if (Encoding.ASCII.GetString(acumulado.GetBuffer(), 0, (int)acumulado.Length).Contains("\r\n\r\n")) break;
            }
            var linha = Encoding.ASCII.GetString(acumulado.GetBuffer(), 0, (int)acumulado.Length).Split("\r\n")[0].Split(' ');
            return linha.Length >= 2 ? linha[1] : null;
        }
        catch { return null; }
    }

    // 1) as bloqueadas respondem sem esperar o corpo
    await Task.Delay(1500);
    var antes = await Task.WhenAll(Enumerable.Range(0, n).Select(i => Ler(i, 800)));
    for (var i = 0; i < n; i++) respostas[i] = antes[i];
    // 2) solta o corpo de todas ao mesmo tempo e le o resto
    await Task.WhenAll(Enumerable.Range(0, n).Where(i => respostas[i] is null).Select(async i =>
    {
        try { await conexoes[i].Stream.WriteAsync(Encoding.UTF8.GetBytes(corpo)); } catch { }
    }));
    var depois = await Task.WhenAll(Enumerable.Range(0, n).Select(i => respostas[i] is null ? Ler(i, 5000) : Task.FromResult(respostas[i])));
    foreach (var c in conexoes) c.Tcp.Dispose();
    var c401 = depois.Count(r => r == "401");
    var c429 = depois.Count(r => r == "429");
    Console.WriteLine($"401={c401} 429={c429} outros={n - c401 - c429}");
    return 0;
}

static async Task<int> Cabecalhos(int porta, string caminho, string? etag, string? chave = null)
{
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(Host, porta);
    var stream = tcp.GetStream();
    var sb = new StringBuilder();
    sb.Append($"GET {caminho} HTTP/1.1\r\nHost: {Host}:{porta}\r\nConnection: close\r\n");
    if (!string.IsNullOrEmpty(etag) && etag != "-") sb.Append($"If-None-Match: {etag}\r\n");
    if (chave is not null) sb.Append($"Cookie: mx={chave}\r\n");
    sb.Append("\r\n");
    await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
    using var ms = new MemoryStream();
    using var cts = new CancellationTokenSource(10000);
    var buf = new byte[16384];
    int n;
    while ((n = await stream.ReadAsync(buf, cts.Token)) > 0) ms.Write(buf, 0, n);
    var tudo = ms.ToArray();
    var fim = -1;
    for (var i = 0; i + 3 < tudo.Length; i++)
        if (tudo[i] == 13 && tudo[i + 1] == 10 && tudo[i + 2] == 13 && tudo[i + 3] == 10) { fim = i; break; }
    if (fim < 0) { Console.WriteLine("0||||||"); return 0; }
    var linhas = Encoding.ASCII.GetString(tudo, 0, fim).Split("\r\n");
    var partes = linhas[0].Split(' ');
    var status = partes.Length >= 2 ? partes[1] : "0";
    string H(string nome) => linhas.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2 && p[0].Equals(nome, StringComparison.OrdinalIgnoreCase)).Select(p => p[1].Trim()).FirstOrDefault() ?? "";
    var corpo = tudo.AsSpan(fim + 4).ToArray();
    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(corpo)).ToLowerInvariant();
    var semCr = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(corpo.Where(b => b != 13).ToArray())).ToLowerInvariant();
    // Fase 17: campos 9 (Location), 10 (quantos Cache-Control vieram) e 11 (1 se o corpo cita /app/js/app.js)
    var nCache = linhas.Skip(1).Count(l => l.StartsWith("Cache-Control:", StringComparison.OrdinalIgnoreCase));
    var citaApp = Encoding.UTF8.GetString(corpo).Contains("/app/js/app.js", StringComparison.Ordinal) ? 1 : 0;
    Console.WriteLine($"{status}|{H("Content-Type")}|{H("Cache-Control")}|{H("ETag")}|{sha}|{corpo.Length}|{semCr}|{H("X-Frame-Options")}|{H("Content-Security-Policy")}|{H("Location")}|{nCache}|{citaApp}");
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
            if (texto is null) break;
            if (EhEstado(texto))
            {
                if (relogio.ElapsedMilliseconds >= 0) Console.WriteLine($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\t{texto}");
                Console.Out.Flush();
            }
            else
            {
                // Fase 9: ocupado, desconectado, aviso... uma linha "MSG<TAB>json"
                Console.WriteLine($"MSG\t{texto}");
                Console.Out.Flush();
            }
        }
        if (Fecho.Codigo is { } cod) { Console.WriteLine($"FECHOU\t{cod}"); Console.Out.Flush(); }
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

// ---- Fase 9 ----

static async Task<int> Ferramenta(int porta, string chave, string json)
{
    string tipo;
    try
    {
        using var cmd = JsonDocument.Parse(json);
        tipo = cmd.RootElement.GetProperty("t").GetString() ?? "";
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
    {
        Console.Error.WriteLine("RECUSADO: comando invalido");
        return 3;
    }
    if (tipo is not ("energia" or "energiaCancelar" or "projecao" or "telas" or "atualizar" or "desconectar"
        or "mouse" or "clique" or "botao" or "rolar" or "texto" or "tecla" or "mouseAbs" or "acordado"
        or "janelas" or "janela" or "atalho" or "sono" or "assinar" or "clipLer" or "clipGravar" or "brilho"))
    {
        Console.Error.WriteLine($"RECUSADO: '{tipo}' nao e um comando de ferramenta permitido.");
        return 3;
    }
    using var c = await Conectar(porta, chave);
    var estado = await ProximoEstado(c, 5000);
    if (estado is null) { Console.Error.WriteLine("erro: sem estado"); return 2; }
    bool teste;
    using (var doc = JsonDocument.Parse(estado))
        teste = doc.RootElement.TryGetProperty("teste", out var t) && t.ValueKind == JsonValueKind.True;
    if (!teste)
    {
        Console.Error.WriteLine("RECUSADO: o app nao esta em modo de teste (--teste-pc); nada foi enviado.");
        return 3;
    }
    await c.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
    // Tempo para o app processar antes de a conexao fechar. Fase 9c: as mensagens avulsas que chegarem nesse tempo (a lista de
    // janelas, por exemplo) saem como "MSG<TAB><json>"; os estados sao ignorados.
    using (var espera = new CancellationTokenSource(tipo is "janelas" or "clipLer" ? 1500 : 400))
    {
        try
        {
            while (c.State == WebSocketState.Open)
            {
                var texto = await Ler(c, espera.Token);
                if (texto is null) break;
                if (!EhEstado(texto)) Console.WriteLine($"MSG\t{texto}");
            }
        }
        catch (WebSocketException) { }
    }
    Console.WriteLine("enviado");
    return 0;
}

// Fase 12: rajada de comandos de brilho no MESMO canal (sem pausa entre eles), para provar o agrupamento do worker.
// Confere "teste":true antes; sem ele RECUSA (codigo 3) sem enviar nada. Manda {"t":"brilho","tela":T,"valor":V} para V de "de" a "ate".
static async Task<int> BrilhoRajada(int porta, string chave, int tela, int de, int ate)
{
    var (c, estado) = await ConectarDeTeste(porta, chave);
    if (c is null) return 3;
    using (c)
    {
        for (var v = de; v <= ate; v++)
        {
            var json = "{\"t\":\"brilho\",\"tela\":" + tela + ",\"valor\":" + v + "}";
            await c.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        await Task.Delay(1500); // tempo para o app processar antes de fechar
    }
    Console.WriteLine("enviado");
    return 0;
}

// Fase 9d: "teste":true no estado, ou recusa. Devolve o WebSocket ja conectado e o estado.
static async Task<(ClientWebSocket? Ws, string? Estado)> ConectarDeTeste(int porta, string chave)
{
    var c = await Conectar(porta, chave);
    var estado = await ProximoEstado(c, 5000);
    if (estado is null) { c.Dispose(); Console.Error.WriteLine("erro: sem estado"); return (null, null); }
    bool teste;
    using (var doc = JsonDocument.Parse(estado))
        teste = doc.RootElement.TryGetProperty("teste", out var t) && t.ValueKind == JsonValueKind.True;
    if (!teste)
    {
        c.Dispose();
        Console.Error.WriteLine("RECUSADO: o app nao esta em modo de teste (--teste-pc); nada foi enviado.");
        return (null, null);
    }
    return (c, estado);
}

// Fase 9d: POST /arquivo com o corpo cru. So aceita arquivos de %TEMP%\mixer-check\ e so com o app em modo de teste. A sessao WebSocket
// fica aberta durante o envio (o servidor exige que esta chave seja a do celular conectado).
static async Task<int> Arquivo(int porta, string chave, string caminho, string? nome)
{
    var pasta = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mixer-check")) + Path.DirectorySeparatorChar;
    var completo = Path.GetFullPath(caminho);
    if (!completo.StartsWith(pasta, StringComparison.OrdinalIgnoreCase) || !File.Exists(completo))
    {
        Console.Error.WriteLine("RECUSADO: so arquivos existentes dentro de %TEMP%\\mixer-check\\.");
        return 3;
    }
    var (ws, _) = await ConectarDeTeste(porta, chave);
    if (ws is null) return 3;
    using var _c = ws;
    using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromMinutes(2) };
    using var fs = File.OpenRead(completo);
    using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{Host}:{porta}/arquivo?nome={Uri.EscapeDataString(string.IsNullOrEmpty(nome) ? Path.GetFileName(completo) : nome)}")
    {
        Content = new StreamContent(fs),
    };
    req.Headers.TryAddWithoutValidation("Cookie", $"mx={chave}");
    req.Headers.TryAddWithoutValidation("Origin", $"http://{Host}:{porta}");
    try
    {
        using var resp = await http.SendAsync(req);
        var corpo = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"{(int)resp.StatusCode}\t{corpo}");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"ERRO\t{ex.GetBaseException().Message}");
    }
    return 0;
}

// Fase 9d: assina o estado do PC por um tempo, cancela e continua ouvindo para provar que nada mais chega.
static async Task<int> Info(int porta, string chave, int segLigado, int segDepois)
{
    var (ws, _) = await ConectarDeTeste(porta, chave);
    if (ws is null) return 3;
    using var _c = ws;
    async Task Enviar(string json) => await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
    // Um unico leitor para a conexao inteira (cancelar um ReceiveAsync aborta o WebSocket); a fase diz como marcar cada mensagem.
    var fase = "INFO";
    var leitor = Task.Run(async () =>
    {
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var texto = await Ler(ws, CancellationToken.None);
                if (texto is null) break;
                if (texto.Contains("\"t\":\"info\"")) Console.WriteLine($"{Volatile.Read(ref fase)}\t{texto}");
            }
        }
        catch (WebSocketException) { }
    });
    await Enviar("{\"t\":\"assinar\",\"g\":\"info\",\"on\":true}");
    await Task.Delay(segLigado * 1000);
    await Enviar("{\"t\":\"assinar\",\"g\":\"info\",\"on\":false}");
    await Task.Delay(300); // uma mensagem que ja estava a caminho ainda pode chegar: so conta o que vier depois dessa folga
    Volatile.Write(ref fase, "DEPOIS");
    Console.WriteLine("OFF");
    await Task.Delay(segDepois * 1000);
    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "fim", new CancellationTokenSource(2000).Token); } catch { }
    try { await leitor; } catch { }
    Console.WriteLine("fim");
    return 0;
}

static async Task<int> Segundo(int porta, string chave)
{
    using var c = await Conectar(porta, chave);
    using var cts = new CancellationTokenSource(5000);
    while (c.State == WebSocketState.Open)
    {
        var texto = await Ler(c, cts.Token);
        if (texto is null) break;
        Console.WriteLine($"MSG\t{texto}");
    }
    Console.WriteLine($"FECHOU\t{Fecho.Codigo?.ToString() ?? "-"}");
    return 0;
}

// Fase 10: canal /tela/ws (um so, com varios passos). A imagem so e lida para conferir cabecalho, tamanho e dimensoes: nunca vai para o disco.
static async Task<int> Tela(int porta, string chave, string passosTxt)
{
    var passos = new List<(string Q, int Mon, int W, int Seg, bool Ok, string Cur, bool Ponteiro)>();
    foreach (var t in passosTxt.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var p = t.Split(':');
        // Fase 15c: campos opcionais depois do ok|semok: <cur> (tamanho do ponteiro, "-" = nao manda) e "p" (manda "p":1)
        passos.Add((p[0], int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]), p.Length < 5 || p[4] != "semok",
            p.Length > 5 && p[5] != "-" && p[5] != "" ? p[5] : "", p.Length > 6 && p[6] == "p"));
    }
    using var ws = new ClientWebSocket();
    if (chave != "-")
    {
        ws.Options.Cookies = new CookieContainer();
        ws.Options.Cookies.Add(new Uri($"http://{Host}:{porta}/"), new Cookie("mx", chave));
    }
    ws.Options.SetRequestHeader("Origin", $"http://{Host}:{porta}");
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://{Host}:{porta}/tela/ws"), cts.Token);
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
    {
        Console.WriteLine($"RECUSADO\t{ex.Message}");
        return 4;
    }

    // Um unico leitor para o canal inteiro (cancelar um ReceiveAsync aborta o WebSocket): cada mensagem vira um item da fila.
    var fila = Channel.CreateUnbounded<(bool Fechou, int Bytes, int W, int H, bool Jpeg, string? Texto)>();
    var leitor = Task.Run(async () =>
    {
        var buf = new byte[4 * 1024 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var len = 0;
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        fila.Writer.TryWrite((true, (int)(ws.CloseStatus ?? 0), 0, 0, false, null));
                        return;
                    }
                    len += r.Count;
                } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Text)
                {
                    fila.Writer.TryWrite((false, 0, 0, 0, false, Encoding.UTF8.GetString(buf, 0, len)));
                    continue;
                }
                if (r.MessageType != WebSocketMessageType.Binary) continue;
                var jpeg = len > 3 && buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF;
                var (jw, jh) = jpeg ? DimensoesJpeg(buf.AsSpan(0, len).ToArray()) : (0, 0);
                fila.Writer.TryWrite((false, len, jw, jh, jpeg, null));
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            fila.Writer.TryWrite((true, 1006, 0, 0, false, null));
        }
    });

    var ok = Encoding.UTF8.GetBytes("{\"ok\":1}");
    for (var i = 0; i < passos.Count; i++)
    {
        var ps = passos[i];
        var extra = (ps.Cur != "" ? $",\"cur\":{ps.Cur}" : "") + (ps.Ponteiro ? ",\"p\":1" : "");
        var cfg = Encoding.UTF8.GetBytes($"{{\"mon\":{ps.Mon},\"w\":{ps.W},\"q\":\"{ps.Q}\"{extra}}}");
        await ws.SendAsync(cfg, WebSocketMessageType.Text, true, CancellationToken.None);
        var relogio = Stopwatch.StartNew();
        long quadros = 0, bytes = 0;
        int ultW = 0, ultH = 0;
        var todosJpeg = true;
        // Fase 15c: textos {"p":[x,y]} recebidos, quadros que vieram logo depois de um texto, textos com x ou y fora de 0..1 e quadros sem texto antes
        int textos = 0, pares = 0, foraDeFaixa = 0, semTexto = 0;
        var textoPendente = false;
        while (true)
        {
            var resta = TimeSpan.FromSeconds(ps.Seg) - relogio.Elapsed;
            if (resta <= TimeSpan.Zero) break;
            using var cts = new CancellationTokenSource(resta);
            bool tem;
            try { tem = await fila.Reader.WaitToReadAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
            if (!tem || !fila.Reader.TryRead(out var item)) break;
            if (item.Fechou)
            {
                Console.WriteLine($"FECHOU\t{item.Bytes}");
                return 5;
            }
            if (item.Texto is not null)
            {
                textos++;
                try
                {
                    using var d = JsonDocument.Parse(item.Texto);
                    var a = d.RootElement.GetProperty("p");
                    if (a.GetArrayLength() != 2 || a[0].GetDouble() is < 0 or > 1 || a[1].GetDouble() is < 0 or > 1) foraDeFaixa++;
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { foraDeFaixa++; }
                textoPendente = true;
                continue;
            }
            if (textoPendente) pares++; else semTexto++;
            textoPendente = false;
            quadros++; bytes += item.Bytes; ultW = item.W; ultH = item.H;
            if (!item.Jpeg) todosJpeg = false;
            if (ps.Ok && ws.State == WebSocketState.Open) await ws.SendAsync(ok, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        var medio = quadros > 0 ? bytes / quadros : 0;
        Console.WriteLine($"PASSO\t{i}\t{ps.Q}\t{quadros}\t{medio}\t{ultW}x{ultH}\t{relogio.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}\t{(todosJpeg ? "jpeg" : "nao-jpeg")}\t{textos}\t{pares}\t{foraDeFaixa}\t{semTexto}");
    }
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
        await Task.WhenAny(leitor, Task.Delay(3000));
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
    Console.WriteLine("FIM");
    return 0;
}

// Percorre os segmentos do JPEG ate o SOF (largura e altura).
static (int, int) DimensoesJpeg(byte[] b)
{
    var i = 2;
    while (i + 9 < b.Length)
    {
        if (b[i] != 0xFF) { i++; continue; }
        var m = b[i + 1];
        if (m == 0xFF) { i++; continue; }
        if (m is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
        var len = (b[i + 2] << 8) | b[i + 3];
        if (m is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
        i += 2 + len;
    }
    return (0, 0);
}

static class Fecho
{
    public static int? Codigo;
}
