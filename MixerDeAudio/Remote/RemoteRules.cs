using System;
using System.Collections.Generic;
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

/// <summary>
/// Limitador de tentativas de pareamento (D7): <see cref="MaxFailures"/> erros seguidos bloqueiam todo o pareamento por
/// <see cref="BlockFor"/>. Um acerto zera a contagem. O relógio é injetável para os testes. Seguro entre threads.
/// </summary>
public sealed class PairingLimiter
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan BlockFor = TimeSpan.FromSeconds(60);

    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private int _failures;
    private DateTime _blockedUntil = DateTime.MinValue;

    public PairingLimiter(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>Se o pareamento está bloqueado agora. Passado o bloqueio, a contagem recomeça.</summary>
    public bool IsBlocked
    {
        get
        {
            lock (_gate)
            {
                if (_blockedUntil == DateTime.MinValue) return false;
                if (_now() < _blockedUntil) return true;
                _blockedUntil = DateTime.MinValue;
                _failures = 0;
                return false;
            }
        }
    }

    /// <summary>Registra um erro. Devolve verdadeiro se este erro iniciou o bloqueio.</summary>
    public bool RegisterFailure()
    {
        lock (_gate)
        {
            if (_blockedUntil != DateTime.MinValue && _now() < _blockedUntil) return false;
            _failures++;
            if (_failures < MaxFailures) return false;
            _blockedUntil = _now() + BlockFor;
            return true;
        }
    }

    /// <summary>Registra um acerto: zera a contagem.</summary>
    public void RegisterSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _blockedUntil = DateTime.MinValue;
        }
    }
}
