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
//   celular.exe ferramenta <porta> <chave> <json>    -> (Fase 9) conecta, confere que o estado traz "teste":true (o app esta com --teste-pc) e so
//                                                       entao envia o comando de ferramenta (energia, energiaCancelar, projecao, telas, atualizar,
//                                                       desconectar); sem "teste":true RECUSA (codigo 3) sem enviar nada
//                                                       (Fase 9c: tambem janelas, janela, atalho, sono e assinar; as mensagens avulsas recebidas logo depois do envio,
//                                                       como a lista de janelas, saem como "MSG<TAB><json>")
//   celular.exe tela <porta> <chave|-> <mon> <w> [n [periodo-ms]] -> (Fase 9b) GET /tela?mon=&w= (n vezes seguidas, sem pausa ou a cada periodo-ms); imprime por quadro
//                                                       "<i><TAB><status><TAB><tipo><TAB><bytes><TAB><LxA><TAB><ms><TAB>jpeg|nao-jpeg" e, com n > 1, "TOTAL<TAB><ms>".
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
        case "ferramenta" when args.Length >= 4:
            return await Ferramenta(int.Parse(args[1]), args[2], args[3]);
        case "tela" when args.Length >= 5:
            return await Tela(int.Parse(args[1]), args[2], int.Parse(args[3]), int.Parse(args[4]), args.Length > 5 ? int.Parse(args[5]) : 1, args.Length > 6 ? int.Parse(args[6]) : 0);
        case "arquivo" when args.Length >= 4:
            return await Arquivo(int.Parse(args[1]), args[2], args[3], args.Length > 4 ? args[4] : null);
        case "info" when args.Length >= 5:
            return await Info(int.Parse(args[1]), args[2], int.Parse(args[3]), int.Parse(args[4]));
        case "segundo" when args.Length >= 3:
            return await Segundo(int.Parse(args[1]), args[2]);
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
    Console.Error.WriteLine("uso: parear <porta> <codigo> | estado <porta> <chave> | enviar <porta> <chave> <json> | ciclos <porta> <chave> <n> | get <porta> <caminho> [chave|-] [origin|-] | segurar <porta> <chave> <segundos> [arquivo-parar] | ferramenta <porta> <chave> <json> | segundo <porta> <chave> | tela <porta> <chave|-> <mon> <w> [n [periodo-ms]] | arquivo <porta> <chave> <caminho> [nome] | info <porta> <chave> <seg-ligado> <seg-depois>");
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
        or "janelas" or "janela" or "atalho" or "sono" or "assinar" or "clipLer" or "clipGravar"))
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

// Fase 9b: pede quadros de /tela com o cookie dado ("-" = sem cookie). A imagem so e lida para conferir cabecalho, tamanho e dimensoes: nunca vai para o disco.
static async Task<int> Tela(int porta, string chave, int mon, int w, int n, int periodoMs)
{
    using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
    var total = Stopwatch.StartNew();
    for (var i = 0; i < n; i++)
    {
        var sw = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"http://{Host}:{porta}/tela?mon={mon}&w={w}");
        if (chave != "-") req.Headers.TryAddWithoutValidation("Cookie", $"mx={chave}");
        using var resp = await http.SendAsync(req);
        var corpo = await resp.Content.ReadAsByteArrayAsync();
        var tipo = resp.Content.Headers.ContentType?.MediaType ?? "-";
        var jpeg = corpo.Length > 3 && corpo[0] == 0xFF && corpo[1] == 0xD8 && corpo[2] == 0xFF;
        var (jw, jh) = jpeg ? DimensoesJpeg(corpo) : (0, 0);
        Console.WriteLine($"{i}	{(int)resp.StatusCode}	{tipo}	{corpo.Length}	{jw}x{jh}	{sw.ElapsedMilliseconds}	{(jpeg ? "jpeg" : "nao-jpeg")}");
        if (periodoMs > 0)
        {
            var falta = (i + 1) * periodoMs - (int)total.ElapsedMilliseconds; // ritmo fixo, nao acumula a demora de cada quadro
            if (falta > 0) await Task.Delay(falta);
        }
    }
    if (n > 1) Console.WriteLine($"TOTAL	{total.ElapsedMilliseconds}");
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
