using System.Net;
using System.Text;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

public class RemoteRulesTests
{
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.10")]
    [InlineData("169.254.1.2")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("febf::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:192.168.1.5")]
    public void EnderecoPrivado_EhAceito(string ip) => Assert.True(RemoteRules.IsPrivateNetwork(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("11.0.0.1")]
    [InlineData("169.253.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("fec0::1")]
    [InlineData("fb00::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void EnderecoPublico_EhRecusado(string ip) => Assert.False(RemoteRules.IsPrivateNetwork(IPAddress.Parse(ip)));

    [Fact]
    public void EnderecoNulo_EhRecusado() => Assert.False(RemoteRules.IsPrivateNetwork(null));

    [Theory]
    [InlineData("http://192.168.0.10:8750", "192.168.0.10:8750", true)]
    [InlineData("http://192.168.0.10:8750/", "192.168.0.10:8750", true)]
    [InlineData("HTTP://Localhost:8750", "localhost:8750", true)]
    [InlineData("http://evil.com", "192.168.0.10:8750", false)]
    [InlineData("http://192.168.0.10:8751", "192.168.0.10:8750", false)]
    [InlineData("https://192.168.0.10:8750", "192.168.0.10:8750", false)]
    [InlineData("192.168.0.10:8750", "192.168.0.10:8750", false)]
    [InlineData("http://192.168.0.10:8750/x", "192.168.0.10:8750", false)]
    [InlineData(null, "192.168.0.10:8750", false)]
    [InlineData("", "192.168.0.10:8750", false)]
    [InlineData("http://192.168.0.10:8750", null, false)]
    public void Origin_ContraHost(string? origin, string? host, bool esperado) =>
        Assert.Equal(esperado, RemoteRules.OriginMatchesHost(origin, host));

    [Fact]
    public void SecWebSocketAccept_ExemploDaRfc6455() =>
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", RemoteRules.ComputeWebSocketAccept("dGhlIHNhbXBsZSBub25jZQ=="));

    private static string Cabecalho(params string[] linhas) => string.Join("\r\n", linhas) + "\r\n\r\n";

    [Fact]
    public void Cabecalho_Valido_EhLido()
    {
        var head = RemoteRules.ParseHead(Cabecalho("GET /icone/abc HTTP/1.1", "Host: 192.168.0.10:8750", "cookie: mx=abc; x=1"));
        Assert.NotNull(head);
        Assert.Equal("GET", head!.Method);
        Assert.Equal("/icone/abc", head.Path);
        Assert.Equal("192.168.0.10:8750", head.Header("HOST"));
        Assert.Equal("abc", RemoteRules.GetCookie(head.Header("Cookie")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("GET / HTTP/2")]
    [InlineData("GET /")]
    [InlineData("GET http://x/ HTTP/1.1")]
    [InlineData("GE T / HTTP/1.1")]
    [InlineData("GET  / HTTP/1.1")]
    public void LinhaDeRequisicaoMalformada_EhRecusada(string linha) => Assert.Null(RemoteRules.ParseHead(Cabecalho(linha, "Host: x")));

    [Theory]
    [InlineData("SemDoisPontos")]
    [InlineData(": vazio")]
    [InlineData("Nome Com Espaco: x")]
    public void CabecalhoMalformado_EhRecusado(string linha) => Assert.Null(RemoteRules.ParseHead(Cabecalho("GET / HTTP/1.1", linha)));

    [Fact]
    public void CabecalhoGrandeDemais_EhRecusado()
    {
        var grande = Cabecalho("GET / HTTP/1.1", "X-Grande: " + new string('a', RemoteRules.MaxHeadBytes));
        Assert.Null(RemoteRules.ParseHead(grande));
    }

    [Fact]
    public void FindHeadEnd_AcheOFimOuDevolveMenosUm()
    {
        var bytes = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\ncorpo");
        Assert.Equal("GET / HTTP/1.1\r\nHost: x\r\n\r\n".Length, RemoteRules.FindHeadEnd(bytes));
        Assert.Equal(-1, RemoteRules.FindHeadEnd(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n")));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("0", 0)]
    [InlineData("27", 27)]
    [InlineData("1024", 1024)]
    [InlineData("1025", -1)]
    [InlineData("-1", -1)]
    [InlineData("abc", -1)]
    public void ContentLength_ComLimite(string? valor, int esperado)
    {
        var linhas = valor is null ? new[] { "POST /parear HTTP/1.1" } : new[] { "POST /parear HTTP/1.1", "Content-Length: " + valor };
        Assert.Equal(esperado, RemoteRules.BodyLength(RemoteRules.ParseHead(Cabecalho(linhas))!));
    }

    [Fact]
    public void Upgrade_ValidoEInvalido()
    {
        var ok = RemoteRules.ParseHead(Cabecalho("GET /ws HTTP/1.1", "Upgrade: websocket", "Connection: keep-alive, Upgrade",
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==", "Sec-WebSocket-Version: 13"))!;
        Assert.True(RemoteRules.IsWebSocketUpgrade(ok));
        var semChave = RemoteRules.ParseHead(Cabecalho("GET /ws HTTP/1.1", "Upgrade: websocket", "Connection: Upgrade"))!;
        Assert.False(RemoteRules.IsWebSocketUpgrade(semChave));
        var chaveCurta = RemoteRules.ParseHead(Cabecalho("GET /ws HTTP/1.1", "Upgrade: websocket", "Connection: Upgrade", "Sec-WebSocket-Key: abc"))!;
        Assert.False(RemoteRules.IsWebSocketUpgrade(chaveCurta));
        var post = RemoteRules.ParseHead(Cabecalho("POST /ws HTTP/1.1", "Upgrade: websocket", "Connection: Upgrade", "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ=="))!;
        Assert.False(RemoteRules.IsWebSocketUpgrade(post));
    }

    [Theory]
    [InlineData("mx=abc", "abc")]
    [InlineData("a=1; mx=abc; b=2", "abc")]
    [InlineData("a=1;mx=abc", "abc")]
    [InlineData("a=1", null)]
    [InlineData("xmx=abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Cookie(string? cabecalho, string? esperado) => Assert.Equal(esperado, RemoteRules.GetCookie(cabecalho));

    [Fact]
    public void Limitador_CincoErrosBloqueiam_LiberamDepoisDe60s_UmAcertoZera()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var limitador = new PairingLimiter(() => agora);
        for (var i = 0; i < 4; i++)
        {
            Assert.False(limitador.RegisterFailure());
            Assert.False(limitador.IsBlocked);
        }
        Assert.True(limitador.RegisterFailure()); // o 5o erro inicia o bloqueio
        Assert.True(limitador.IsBlocked);
        agora += TimeSpan.FromSeconds(59);
        Assert.True(limitador.IsBlocked);
        agora += TimeSpan.FromSeconds(2);
        Assert.False(limitador.IsBlocked);
        // depois do bloqueio a contagem recomeça do zero
        for (var i = 0; i < 4; i++) limitador.RegisterFailure();
        Assert.False(limitador.IsBlocked);
        limitador.RegisterSuccess();
        for (var i = 0; i < 4; i++) limitador.RegisterFailure();
        Assert.False(limitador.IsBlocked); // o acerto zerou: ainda faltaria 1 erro
        Assert.True(limitador.RegisterFailure());
        Assert.True(limitador.IsBlocked);
    }

    [Fact]
    public void Limitador_ErrosDuranteOBloqueioNaoPrologamNada()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var limitador = new PairingLimiter(() => agora);
        for (var i = 0; i < 5; i++) limitador.RegisterFailure();
        agora += TimeSpan.FromSeconds(30);
        Assert.False(limitador.RegisterFailure());
        agora += TimeSpan.FromSeconds(31);
        Assert.False(limitador.IsBlocked);
    }

    [Fact]
    public void HashDaChave_EhSha256EmHexadecimal()
    {
        // SHA-256 de "abc"
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", RemoteRules.HashKey("abc"));
        Assert.NotEqual(RemoteRules.HashKey("abc"), RemoteRules.HashKey("abd"));
    }

    [Fact]
    public void NovaChave_Tem43CaracteresBase64UrlEEhDiferenteACadaVez()
    {
        var a = RemoteRules.NewKey();
        var b = RemoteRules.NewKey();
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NovoCodigo_Tem6Digitos()
    {
        for (var i = 0; i < 50; i++) Assert.Matches("^[0-9]{6}$", RemoteRules.NewCode());
    }

    [Theory]
    [InlineData("123456", "123456", true)]
    [InlineData("123456", " 123456 ", true)]
    [InlineData("123456", "123457", false)]
    [InlineData("123456", "12345", false)]
    [InlineData("123456", null, false)]
    [InlineData(null, "123456", false)]
    [InlineData("", "", false)]
    public void Codigo_Comparacao(string? esperado, string? dado, bool resultado) =>
        Assert.Equal(resultado, RemoteRules.CodeMatches(esperado, dado));

    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/120", "Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Safari", "iPhone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)", "iPad")]
    [InlineData("curl/8.0", "Celular")]
    [InlineData(null, "Celular")]
    public void NomeDoAparelho(string? userAgent, string esperado) => Assert.Equal(esperado, RemoteRules.PhoneName(userAgent));

    [Fact]
    public void EnderecoEUrlDoQr()
    {
        Assert.Equal("http://192.168.0.10:8750", RemoteRules.BuildAddress("192.168.0.10", 8750));
        Assert.Equal("http://192.168.0.10:8751/#c=012345", RemoteRules.BuildPairingUrl("192.168.0.10", 8751, "012345"));
    }

    [Fact]
    public void PaginaDoCelular_TemMenosDe40KbESemUrlExterna()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "MixerDeAudio", "Remote", "Web", "index.html");
            if (File.Exists(candidate)) { path = candidate; break; }
        }
        Assert.NotNull(path);
        var bytes = File.ReadAllBytes(path!);
        Assert.True(bytes.Length < 40 * 1024, $"index.html tem {bytes.Length} bytes");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotMatch(@"https?://", text);
        Assert.DoesNotMatch(@"(src|href)\s*=\s*[""']?//", text);
        Assert.DoesNotContain("@import", text);
    }
}
