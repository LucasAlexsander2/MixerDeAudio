using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MixerDeAudio.Remote;

/// <summary>Cabeçalho de uma requisição HTTP já lido (Fase 6, D5).</summary>
public sealed record HttpHead(string Method, string Path, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}

/// <summary>
/// Regras puras do servidor do celular (Fase 6, D5 a D7): rede privada, Origin e Host, handshake do WebSocket, leitura
/// do cabeçalho HTTP com limites, cookie e geração e hash de código e chave. Sem rede, sem WinUI.
/// </summary>
public static class RemoteRules
{
    public const int MaxHeadBytes = 8 * 1024;
    public const int MaxBodyBytes = 1024;
    public const string CookieName = "mx";
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    // ---- Porta e reconexão (Fase 10b, D11 e D12) ----

    public const int FirstPort = 8750;
    public const int LastPort = 8759;

    /// <summary>Porta salva válida: fora de 8750 a 8759 vira 8750.</summary>
    public static int NormalizePort(int port) => port is >= FirstPort and <= LastPort ? port : FirstPort;

    /// <summary>Ordem de tentativa na partida: a porta salva primeiro, depois as outras da faixa em ordem crescente.</summary>
    public static IReadOnlyList<int> PortOrder(int saved)
    {
        saved = NormalizePort(saved);
        var list = new List<int> { saved };
        for (var p = FirstPort; p <= LastPort; p++)
            if (p != saved) list.Add(p);
        return list;
    }

    /// <summary>O que a página faz depois de uma resposta de <c>/sessao</c> ou <c>/parear</c> (espelhado no JS).</summary>
    public enum PageAction { Connect, Pair, Reconnect }

    /// <summary>
    /// Só 401 leva ao pareamento. 2xx segue conectando. Sem resposta (<c>null</c>), 5xx, 409 ("ocupado"), WebSocket caído
    /// e qualquer outro status ficam em "Reconectando…".
    /// </summary>
    public static PageAction DecidePage(int? status) => status switch
    {
        null => PageAction.Reconnect,
        >= 200 and < 300 => PageAction.Connect,
        401 => PageAction.Pair,
        _ => PageAction.Reconnect,
    };

    /// <summary>Espera antes da tentativa <paramref name="attempt"/> (0, 1, 2…): 0,5 s, dobrando, até 5 s.</summary>
    public static int ReconnectDelayMs(int attempt) => Math.Min(5000, 500 << Math.Clamp(attempt, 0, 4));

    // ---- Rede local (D7) ----

    /// <summary>
    /// Endereço de rede privada: 10/8, 172.16/12, 192.168/16, 169.254/16, loopback, IPv6 de enlace local (fe80::/10) ou
    /// ULA (fc00::/7). IPv6 mapeado para IPv4 vale pelo IPv4.
    /// </summary>
    public static bool IsPrivateNetwork(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            return false;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) return true; // fe80::/10
            if ((b[0] & 0xfe) == 0xfc) return true;                 // fc00::/7
            return false;
        }
        return false;
    }

    // ---- Origin e Host (D6) ----

    /// <summary>O <c>Origin</c> (esquema + host:porta) tem que apontar para o mesmo <c>Host</c> da requisição. Ausente ou vazio é recusado.</summary>
    public static bool OriginMatchesHost(string? origin, string? host)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(host)) return false;
        var o = origin.Trim();
        var sep = o.IndexOf("://", StringComparison.Ordinal);
        if (sep < 0) return false;
        var scheme = o.Substring(0, sep);
        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase)) return false;
        var authority = o.Substring(sep + 3).TrimEnd('/');
        if (authority.Length == 0 || authority.Contains('/')) return false;
        return string.Equals(authority, host.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fase 16, D1: o <c>Host</c> da requisição só vale se for o IP de uma interface permitida do PC, <c>127.0.0.1</c>,
    /// <c>localhost</c> ou <c>[::1]</c>, sempre com a porta atual. Nome de domínio, porta diferente ou Host ausente são
    /// recusados (defesa contra DNS rebinding). IPv4 só em quatro números (nada de "127.1" nem de zeros à esquerda).
    /// </summary>
    public static bool HostAllowed(string? host, IReadOnlyCollection<IPAddress> allowed, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim();
        string name;
        string portText;
        var bracketed = host[0] == '[';
        if (bracketed)
        {
            var end = host.IndexOf(']');
            if (end < 0) return false;
            name = host.Substring(1, end - 1);
            var rest = host.Substring(end + 1);
            if (rest.Length == 0 || rest[0] != ':') return false;
            portText = rest.Substring(1);
        }
        else
        {
            var colon = host.LastIndexOf(':');
            if (colon < 0 || host.IndexOf(':') != colon) return false;
            name = host.Substring(0, colon);
            portText = host.Substring(colon + 1);
        }
        if (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) || p != port) return false;
        if (!bracketed && name.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Length == 0 || name.Contains('%')) return false;
        if (!IPAddress.TryParse(name, out var ip)) return false;
        if (bracketed != (ip.AddressFamily == AddressFamily.InterNetworkV6)) return false;
        if (!bracketed && !string.Equals(ip.ToString(), name, StringComparison.Ordinal)) return false; // só "a.b.c.d" em decimal, sem zeros à esquerda
        if (ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback)) return true;
        foreach (var a in allowed)
            if (a.Equals(ip)) return true;
        return false;
    }

    /// <summary>
    /// Fase 16, D6: política de conteúdo das páginas (<c>/</c>). <paramref name="host"/> é o <c>Host</c> já
    /// validado por <see cref="HostAllowed"/>; se vier com algum caractere fora de letras, números, ponto, hífen, dois-pontos
    /// e colchetes, o <c>connect-src</c> fica só com <c>'self'</c>.
    /// </summary>
    public static string BuildCsp(string? host)
    {
        var connect = "'self'";
        if (!string.IsNullOrEmpty(host) && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or ':' or '[' or ']' or '-'))
            connect += " ws://" + host;
        return "frame-ancestors 'none'; default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
             + "img-src 'self' blob: data:; font-src 'self'; connect-src " + connect;
    }

    // ---- WebSocket (RFC 6455) ----

    /// <summary>Valor de <c>Sec-WebSocket-Accept</c> para o <c>Sec-WebSocket-Key</c> recebido.</summary>
    public static string ComputeWebSocketAccept(string key)
    {
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid));
        return Convert.ToBase64String(hash);
    }

    /// <summary>Pedido de upgrade válido: <c>Upgrade: websocket</c>, <c>Connection</c> com upgrade e chave de 16 bytes em base64.</summary>
    public static bool IsWebSocketUpgrade(HttpHead head)
    {
        if (!string.Equals(head.Method, "GET", StringComparison.Ordinal)) return false;
        if (!string.Equals(head.Header("Upgrade")?.Trim(), "websocket", StringComparison.OrdinalIgnoreCase)) return false;
        var conn = head.Header("Connection");
        if (conn is null || conn.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) < 0) return false;
        var key = head.Header("Sec-WebSocket-Key");
        if (key is null) return false;
        try { return Convert.FromBase64String(key.Trim()).Length == 16; }
        catch (FormatException) { return false; }
    }

    // ---- Leitura do cabeçalho HTTP com limites (D5) ----

    /// <summary>Posição logo depois de <c>\r\n\r\n</c> no buffer, ou -1 se o fim do cabeçalho ainda não chegou.</summary>
    public static int FindHeadEnd(ReadOnlySpan<byte> buffer)
    {
        for (var i = 0; i + 3 < buffer.Length; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n') return i + 4;
        }
        return -1;
    }

    /// <summary>
    /// Lê a linha de requisição e os cabeçalhos. Nulo se estiver malformado ou grande demais: linha de requisição
    /// diferente de "MÉTODO CAMINHO HTTP/1.x", caminho que não começa com "/", cabeçalho sem ":" ou nome inválido.
    /// </summary>
    public static HttpHead? ParseHead(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxHeadBytes) return null;
        var lines = text.Split("\r\n");
        if (lines.Length < 1) return null;
        var parts = lines[0].Split(' ');
        if (parts.Length != 3) return null;
        var method = parts[0];
        var path = parts[1];
        if (method.Length == 0 || !IsToken(method)) return null;
        if (path.Length == 0 || path[0] != '/') return null;
        if (parts[2] != "HTTP/1.1" && parts[2] != "HTTP/1.0") return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue; // a linha vazia do fim
            var colon = line.IndexOf(':');
            if (colon <= 0) return null;
            var name = line.Substring(0, colon);
            if (!IsToken(name)) return null;
            headers[name] = line.Substring(colon + 1).Trim();
        }
        return new HttpHead(method, path, headers);
    }

    private static bool IsToken(string s)
    {
        foreach (var c in s)
        {
            var ok = char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c);
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Tamanho do corpo (<c>Content-Length</c>): 0 se não houver; -1 se for inválido ou passar de <see cref="MaxBodyBytes"/>.</summary>
    public static int BodyLength(HttpHead head)
    {
        var v = head.Header("Content-Length");
        if (v is null) return 0;
        if (!int.TryParse(v.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)) return -1;
        return n > MaxBodyBytes ? -1 : n;
    }

    // ---- Envio de arquivo (Fase 9d, D26) ----

    /// <summary>O <c>POST /arquivo</c>: o corpo não é lido pela leitura comum (limite de 1 KB), e sim em blocos direto para o disco.</summary>
    public static bool IsUpload(HttpHead head)
    {
        if (head.Method != "POST") return false;
        var q = head.Path.IndexOf('?');
        return (q >= 0 ? head.Path.Substring(0, q) : head.Path) == "/arquivo";
    }

    /// <summary>O <c>POST /parear</c> (Fase 16, D2): o corpo só é lido DEPOIS de reservar a tentativa no limitador, por isso não vai na leitura comum.</summary>
    public static bool IsPair(HttpHead head)
    {
        if (head.Method != "POST") return false;
        var q = head.Path.IndexOf('?');
        return (q >= 0 ? head.Path.Substring(0, q) : head.Path) == "/parear";
    }

    /// <summary><c>Content-Length</c> do envio: -1 se faltar ou for inválido.</summary>
    public static long UploadLength(HttpHead head)
    {
        var v = head.Header("Content-Length");
        if (v is null) return -1;
        return long.TryParse(v.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1;
    }

    // ---- Cookie e chaves ----

    /// <summary>Valor do cookie <paramref name="name"/> no cabeçalho <c>Cookie</c>, ou nulo.</summary>
    public static string? GetCookie(string? header, string name = CookieName)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        foreach (var part in header.Split(';'))
        {
            var p = part.Trim();
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            if (string.Equals(p.Substring(0, eq), name, StringComparison.Ordinal)) return p.Substring(eq + 1);
        }
        return null;
    }

    /// <summary>Nome curto do aparelho a partir do User-Agent ("Android", "iPhone"...); "Celular" se não reconhecer (D11).</summary>
    public static string PhoneName(string? userAgent)
    {
        var ua = userAgent ?? "";
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "iPhone";
        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iPad";
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "Mac";
        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
        return "Celular";
    }

    /// <summary>Endereço mostrado nas Configurações: <c>http://&lt;ip&gt;:&lt;porta&gt;</c> (Fase 6, D17).</summary>
    public static string BuildAddress(string ip, int port) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"http://{ip}:{port}");

    /// <summary>Endereço do QR code: <c>http://&lt;ip&gt;:&lt;porta&gt;/#c=&lt;código&gt;</c>; a página lê o código do fragmento e pareia sozinha (D15, D17).</summary>
    public static string BuildPairingUrl(string ip, int port, string code) => BuildAddress(ip, port) + "/#c=" + code;

    /// <summary>Código de pareamento: 6 dígitos de um gerador criptográfico.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Chave da sessão: 32 bytes aleatórios em base64url, sem preenchimento.</summary>
    public static string NewKey() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 da chave, em hexadecimal minúsculo (64 caracteres): é o que fica no <c>config.json</c>.</summary>
    public static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Comparação do código em tempo constante.</summary>
    public static bool CodeMatches(string? expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || given is null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given.Trim()));
    }
}

/// <summary>Resultado de <see cref="PairingLimiter.TryBegin"/>.</summary>
public enum PairingGate { Allowed, IpBlocked, GlobalBlocked }

/// <param name="Gate">Se a tentativa pode seguir.</param>
/// <param name="RetryAfterSeconds">Quando bloqueada por IP: segundos até liberar (0 no bloqueio global, que só abre pelas Configurações).</param>
/// <param name="StartedBlock">Esta tentativa (que passou) fechou a conta e iniciou um bloqueio: quem chama troca o código (D2).</param>
/// <param name="BlockSeconds">Duração do bloqueio por IP que esta tentativa iniciou (0 se não iniciou; o bloqueio global não tem prazo).</param>
public readonly record struct PairingTicket(PairingGate Gate, int RetryAfterSeconds, bool StartedBlock, int BlockSeconds = 0);

/// <summary>
/// Limitador de tentativas de pareamento (Fase 16, D2). <see cref="TryBegin"/> reserva a tentativa dentro do <c>lock</c>,
/// ANTES de ler o código, então tentativas em paralelo não furam o limite. Por IP: <see cref="MaxAttemptsPerIp"/>
/// tentativas bloqueiam esse IP por 1, 5 e 30 min (crescendo; volta ao começo depois de 1 h sem tentativas). Global:
/// <see cref="MaxAttemptsGlobal"/> tentativas em 10 min (de todos os IPs) fecham o pareamento até
/// <see cref="ReopenGlobal"/> (o usuário abrir as Configurações). Um acerto zera a conta do IP e a global. Relógio
/// injetável. Seguro entre threads.
/// </summary>
public sealed class PairingLimiter
{
    public const int MaxAttemptsPerIp = 5;
    public const int MaxAttemptsGlobal = 12;
    public static readonly TimeSpan[] BlockLevels = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];
    public static readonly TimeSpan CalmReset = TimeSpan.FromHours(1);
    public static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(10);
    private const int PruneAbove = 256;

    private sealed class IpState
    {
        public int Attempts;
        public int Level;
        public DateTime BlockedUntil = DateTime.MinValue;
        public DateTime Last = DateTime.MinValue;
    }

    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, IpState> _ips = new(StringComparer.Ordinal);
    private int _globalCount;
    private DateTime _globalStart = DateTime.MinValue;
    private bool _globalLocked;

    public PairingLimiter(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>Pareamento fechado pelo bloqueio global (abre de novo só com <see cref="ReopenGlobal"/>).</summary>
    public bool GlobalLocked
    {
        get { lock (_gate) return _globalLocked; }
    }

    /// <summary>Reserva uma tentativa do <paramref name="ip"/>. Chamar antes de ler o corpo.</summary>
    public PairingTicket TryBegin(string ip)
    {
        lock (_gate)
        {
            var now = _now();
            if (_globalLocked) return new PairingTicket(PairingGate.GlobalBlocked, 0, false);
            if (_ips.Count > PruneAbove) Prune(now);
            if (!_ips.TryGetValue(ip, out var st)) _ips[ip] = st = new IpState();

            if (st.BlockedUntil > now)
                return new PairingTicket(PairingGate.IpBlocked, (int)Math.Ceiling((st.BlockedUntil - now).TotalSeconds), false);
            if (st.BlockedUntil != DateTime.MinValue) { st.BlockedUntil = DateTime.MinValue; st.Attempts = 0; } // o bloqueio acabou: janela nova
            if (st.Last != DateTime.MinValue && now - st.Last >= CalmReset) { st.Attempts = 0; st.Level = 0; }

            if (_globalStart == DateTime.MinValue || now - _globalStart >= GlobalWindow) { _globalStart = now; _globalCount = 0; }
            st.Attempts++;
            st.Last = now;
            _globalCount++;

            var started = false;
            var blockSeconds = 0;
            if (st.Attempts >= MaxAttemptsPerIp)
            {
                var length = BlockLevels[Math.Min(st.Level, BlockLevels.Length - 1)];
                st.BlockedUntil = now + length;
                st.Level++;
                started = true;
                blockSeconds = (int)length.TotalSeconds;
            }
            if (_globalCount >= MaxAttemptsGlobal)
            {
                _globalLocked = true;
                started = true;
            }
            return new PairingTicket(PairingGate.Allowed, 0, started, blockSeconds);
        }
    }

    /// <summary>Acerto: zera a conta do IP e a global.</summary>
    public void RegisterSuccess(string ip)
    {
        lock (_gate)
        {
            _ips.Remove(ip);
            _globalCount = 0;
            _globalStart = DateTime.MinValue;
        }
    }

    /// <summary>O usuário abriu as Configurações: o pareamento fechado pelo bloqueio global abre de novo.</summary>
    public void ReopenGlobal()
    {
        lock (_gate)
        {
            _globalLocked = false;
            _globalCount = 0;
            _globalStart = DateTime.MinValue;
        }
    }

    private void Prune(DateTime now)
    {
        var old = new List<string>();
        foreach (var (ip, st) in _ips)
            if (st.BlockedUntil <= now && now - st.Last >= CalmReset) old.Add(ip);
        foreach (var ip in old) _ips.Remove(ip);
    }
}

/// <summary>
/// Janela de pareamento (Fase 16, D3): aberta enquanto as Configurações mostram a seção do celular e por
/// <see cref="Grace"/> depois de fechadas. Relógio injetável. Seguro entre threads.
/// </summary>
public sealed class PairingWindow
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private bool _open;
    private DateTime _closedAt = DateTime.MinValue;

    public PairingWindow(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public void Open()
    {
        lock (_gate) { _open = true; _closedAt = DateTime.MinValue; }
    }

    /// <summary>Começa a contar os 2 min. Sem efeito se já estava fechada.</summary>
    public void Close()
    {
        lock (_gate)
        {
            if (!_open) return;
            _open = false;
            _closedAt = _now();
        }
    }

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                if (_open) return true;
                return _closedAt != DateTime.MinValue && _now() - _closedAt < Grace;
            }
        }
    }
}

/// <summary>
/// Requisições abertas por IP (Fase 16, D7): no máximo <see cref="MaxPerIp"/> ao mesmo tempo para cada IP, além do limite
/// de todas juntas que o servidor já tinha. Seguro entre threads.
/// </summary>
public sealed class IpRequestCounter
{
    public const int MaxPerIp = 8;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _open = new(StringComparer.Ordinal);

    /// <summary>Reserva uma vaga para o <paramref name="ip"/>; falso se ele já tem <see cref="MaxPerIp"/> abertas.</summary>
    public bool TryEnter(string ip)
    {
        lock (_gate)
        {
            _open.TryGetValue(ip, out var n);
            if (n >= MaxPerIp) return false;
            _open[ip] = n + 1;
            return true;
        }
    }

    public void Leave(string ip)
    {
        lock (_gate)
        {
            if (!_open.TryGetValue(ip, out var n)) return;
            if (n <= 1) _open.Remove(ip);
            else _open[ip] = n - 1;
        }
    }

    public int Open(string ip)
    {
        lock (_gate) return _open.TryGetValue(ip, out var n) ? n : 0;
    }
}
