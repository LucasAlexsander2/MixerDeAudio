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

    // ---- Fase 16: Host (D1), limitador (D2), janela de pareamento (D3), CSP (D6), vagas por IP (D7) ----

    private static readonly IPAddress[] Permitidos = [IPAddress.Parse("192.168.0.10"), IPAddress.Parse("10.0.0.5")];

    [Theory]
    [InlineData("192.168.0.10:8750", true)]
    [InlineData("10.0.0.5:8750", true)]
    [InlineData("127.0.0.1:8750", true)]
    [InlineData("localhost:8750", true)]
    [InlineData("LocalHost:8750", true)]
    [InlineData("[::1]:8750", true)]
    [InlineData("192.168.0.10:8751", false)]      // porta errada
    [InlineData("192.168.0.10", false)]           // sem porta
    [InlineData("localhost", false)]
    [InlineData("mal.exemplo:8750", false)]       // nome de domínio
    [InlineData("localhost.mal.exemplo:8750", false)]
    [InlineData("192.168.0.11:8750", false)]      // IP que não é do PC
    [InlineData("8.8.8.8:8750", false)]
    [InlineData("[fe80::1]:8750", false)]         // IPv6 que não é do PC
    [InlineData("[::1]", false)]
    [InlineData("::1:8750", false)]               // IPv6 sem colchetes
    [InlineData("127.1:8750", false)]             // forma abreviada
    [InlineData("192.168.000.10:8750", false)]    // zeros à esquerda
    [InlineData("192.168.0.10:8750:1", false)]
    [InlineData("192.168.0.10:abc", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Host_SoIpDoPcOuLocal_ComAPortaAtual(string? host, bool esperado) =>
        Assert.Equal(esperado, RemoteRules.HostAllowed(host, Permitidos, 8750));

    [Fact]
    public void Host_SemInterfacePermitida_SoLocalPassa()
    {
        Assert.False(RemoteRules.HostAllowed("192.168.0.10:8750", Array.Empty<IPAddress>(), 8750));
        Assert.True(RemoteRules.HostAllowed("127.0.0.1:8750", Array.Empty<IPAddress>(), 8750));
    }

    [Fact]
    public void Csp_TemFrameAncestorsNoneEOWebSocketDoHost()
    {
        var csp = RemoteRules.BuildCsp("192.168.0.10:8750");
        Assert.StartsWith("frame-ancestors 'none'; default-src 'self'; ", csp);
        Assert.Contains("script-src 'self'; style-src", csp);   // Fase 17, D5: sem 'unsafe-inline' nos scripts
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("img-src 'self' blob: data:", csp);
        Assert.Contains("font-src 'self'", csp);
        Assert.EndsWith("connect-src 'self' ws://192.168.0.10:8750", csp);
        Assert.EndsWith("connect-src 'self' ws://[::1]:8750", RemoteRules.BuildCsp("[::1]:8750"));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("x;y:8750")]
    [InlineData("h\r\nX: 1")]
    [InlineData(null)]
    public void Csp_HostComCaractereEstranho_FicaSoComSelf(string? host) =>
        Assert.EndsWith("connect-src 'self'", RemoteRules.BuildCsp(host));

    [Fact]
    public void Limitador_CincoTentativasBloqueiamOIp_ComBloqueioCrescente_EVoltaAoComecoDepoisDe1h()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var l = new PairingLimiter(() => agora);
        PairingTicket t = default;
        for (var i = 0; i < PairingLimiter.MaxAttemptsPerIp; i++) t = l.TryBegin("1.1.1.1");
        Assert.Equal(PairingGate.Allowed, t.Gate);
        Assert.True(t.StartedBlock); // a 5a passa e inicia o bloqueio
        var ticketDoBloqueio = t;
        var bloqueado = l.TryBegin("1.1.1.1");
        Assert.Equal(PairingGate.IpBlocked, bloqueado.Gate);
        Assert.InRange(bloqueado.RetryAfterSeconds, 59, 60);
        Assert.Equal(60, ticketDoBloqueio.BlockSeconds);

        // 1 min depois a janela é nova, mas o próximo bloqueio é de 5 min
        agora += TimeSpan.FromSeconds(61);
        for (var i = 0; i < PairingLimiter.MaxAttemptsPerIp; i++) t = l.TryBegin("1.1.1.1");
        Assert.True(t.StartedBlock);
        agora += TimeSpan.FromMinutes(4);
        Assert.Equal(PairingGate.IpBlocked, l.TryBegin("1.1.1.1").Gate);
        agora += TimeSpan.FromMinutes(11);
        // e o terceiro, de 30 min (o degrau mais alto da escada)
        for (var i = 0; i < PairingLimiter.MaxAttemptsPerIp; i++) t = l.TryBegin("1.1.1.1");
        Assert.True(t.StartedBlock);
        agora += TimeSpan.FromMinutes(29);
        Assert.Equal(PairingGate.IpBlocked, l.TryBegin("1.1.1.1").Gate);
        agora += TimeSpan.FromMinutes(1.5);
        Assert.Equal(PairingGate.Allowed, l.TryBegin("1.1.1.1").Gate);

        // 1 h sem tentativas: volta ao bloqueio de 1 min
        agora += TimeSpan.FromHours(1);
        for (var i = 0; i < PairingLimiter.MaxAttemptsPerIp; i++) t = l.TryBegin("1.1.1.1");
        Assert.True(t.StartedBlock);
        agora += TimeSpan.FromSeconds(61);
        Assert.Equal(PairingGate.Allowed, l.TryBegin("1.1.1.1").Gate);
    }

    [Fact]
    public void Limitador_UmIpBloqueadoNaoBloqueiaOutro_EUmAcertoZeraAConta()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var l = new PairingLimiter(() => agora);
        for (var i = 0; i < 5; i++) l.TryBegin("1.1.1.1");
        Assert.Equal(PairingGate.IpBlocked, l.TryBegin("1.1.1.1").Gate);
        Assert.Equal(PairingGate.Allowed, l.TryBegin("2.2.2.2").Gate);

        for (var i = 0; i < 3; i++) l.TryBegin("3.3.3.3");
        l.RegisterSuccess("3.3.3.3");
        for (var i = 0; i < 4; i++) Assert.False(l.TryBegin("3.3.3.3").StartedBlock); // a conta recomeçou do zero
        Assert.True(l.TryBegin("3.3.3.3").StartedBlock);
    }

    [Fact]
    public void Limitador_Global_FechaOPareamentoAteReabrir()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var l = new PairingLimiter(() => agora);
        PairingTicket t = default;
        for (var i = 0; i < PairingLimiter.MaxAttemptsGlobal; i++) t = l.TryBegin("10.0.0." + i); // um IP diferente a cada vez
        Assert.Equal(PairingGate.Allowed, t.Gate);
        Assert.True(t.StartedBlock);
        Assert.True(l.GlobalLocked);
        Assert.Equal(PairingGate.GlobalBlocked, l.TryBegin("10.0.1.1").Gate);
        agora += TimeSpan.FromHours(2); // o tempo sozinho não abre
        Assert.Equal(PairingGate.GlobalBlocked, l.TryBegin("10.0.1.2").Gate);
        l.ReopenGlobal(); // o usuário abriu as Configurações
        Assert.False(l.GlobalLocked);
        Assert.Equal(PairingGate.Allowed, l.TryBegin("10.0.1.2").Gate);
    }

    [Fact]
    public void Limitador_Global_AContaRecomecaDepoisDe10Min()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var l = new PairingLimiter(() => agora);
        for (var i = 0; i < PairingLimiter.MaxAttemptsGlobal - 1; i++) l.TryBegin("10.0.0." + i);
        agora += PairingLimiter.GlobalWindow + TimeSpan.FromSeconds(1);
        Assert.False(l.TryBegin("10.0.9.9").StartedBlock);
        Assert.False(l.GlobalLocked);
    }

    [Fact]
    public void Limitador_Reserva_EAtomica_Com32PedidosAoMesmoTempo_NoMaximo5Passam()
    {
        var l = new PairingLimiter();
        var passaram = 0;
        var bloqueados = 0;
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 32 }, _ =>
        {
            var t = l.TryBegin("9.9.9.9");
            if (t.Gate == PairingGate.Allowed) Interlocked.Increment(ref passaram);
            else Interlocked.Increment(ref bloqueados);
        });
        Assert.Equal(PairingLimiter.MaxAttemptsPerIp, passaram);
        Assert.Equal(32 - PairingLimiter.MaxAttemptsPerIp, bloqueados);
    }

    [Fact]
    public void Limitador_Reserva_EAtomica_Com32IpsDiferentes_NoMaximoOLimiteGlobalPassa()
    {
        var l = new PairingLimiter();
        var passaram = 0;
        Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 32 }, i =>
        {
            if (l.TryBegin("10.1.0." + i).Gate == PairingGate.Allowed) Interlocked.Increment(ref passaram);
        });
        Assert.Equal(PairingLimiter.MaxAttemptsGlobal, passaram);
    }

    [Fact]
    public void JanelaDePareamento_AbertaEnquantoOUsuarioOlha_E2MinDepoisDeFechar()
    {
        var agora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var j = new PairingWindow(() => agora);
        Assert.False(j.IsOpen); // nunca aberta: fechada
        j.Open();
        Assert.True(j.IsOpen);
        agora += TimeSpan.FromHours(3);
        Assert.True(j.IsOpen); // aberta o tempo que as Configurações ficarem na tela
        j.Close();
        Assert.True(j.IsOpen);
        agora += TimeSpan.FromSeconds(119);
        Assert.True(j.IsOpen);
        agora += TimeSpan.FromSeconds(1);
        Assert.False(j.IsOpen); // fechada aos 2 min
        j.Open();
        Assert.True(j.IsOpen); // abrir de novo reabre
        j.Close();
        j.Close(); // fechar de novo não renova o prazo
        agora += TimeSpan.FromSeconds(120);
        Assert.False(j.IsOpen);
    }

    [Fact]
    public void Vagas_PorIp_NoMaximo8_ELiberaAoSair()
    {
        var c = new IpRequestCounter();
        for (var i = 0; i < IpRequestCounter.MaxPerIp; i++) Assert.True(c.TryEnter("1.1.1.1"));
        Assert.False(c.TryEnter("1.1.1.1"));
        Assert.True(c.TryEnter("2.2.2.2")); // outro IP tem as suas
        c.Leave("1.1.1.1");
        Assert.True(c.TryEnter("1.1.1.1"));
        Assert.Equal(IpRequestCounter.MaxPerIp, c.Open("1.1.1.1"));
        for (var i = 0; i < 20; i++) c.Leave("1.1.1.1");
        Assert.Equal(0, c.Open("1.1.1.1"));
    }

    [Theory]
    [InlineData("POST", "/parear", true)]
    [InlineData("POST", "/parear?x=1", true)]
    [InlineData("GET", "/parear", false)]
    [InlineData("POST", "/arquivo", false)]
    public void EhPareamento_SoPostEmParear(string metodo, string caminho, bool esperado) =>
        Assert.Equal(esperado, RemoteRules.IsPair(new HttpHead(metodo, caminho, new Dictionary<string, string>())));

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
    public void PaginaDoCelular_TemMenosDe64KbESemUrlExterna()
    {
        // Fase 17: a pagina servida em / e o app/index.html (a antiga saiu); a garantia de nao ter URL externa vale para ele e para o CSS
        string? dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "MixerDeAudio", "Remote", "Web", "app");
            if (Directory.Exists(candidate)) { dir = candidate; break; }
        }
        Assert.NotNull(dir);
        var bytes = File.ReadAllBytes(Path.Combine(dir!, "index.html"));
        Assert.True(bytes.Length < 64 * 1024, $"a pagina tem {bytes.Length} bytes");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotMatch(@"https?://", text);
        Assert.DoesNotMatch(@"(src|href)\s*=\s*[""']?//", text);
        Assert.DoesNotContain("@import", text);
        foreach (var css in Directory.GetFiles(Path.Combine(dir!, "css"), "*.css", SearchOption.AllDirectories))
        {
            var c = File.ReadAllText(css);
            Assert.DoesNotMatch(@"https?://", c);
            Assert.DoesNotContain("@import", c);
        }
    }

    private static string ServerSource() // Fase 17: confere as regras de rota no codigo-fonte do servidor
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "MixerDeAudio", "Remote", "RemoteServer.cs");
            if (File.Exists(f)) return File.ReadAllText(f);
        }
        throw new FileNotFoundException("RemoteServer.cs");
    }

    [Fact]
    public void Rotas_RaizServeONovoENovoDa301ParaRaiz()
    {
        var src = ServerSource();
        Assert.Contains("NewPageResource = \"MixerDeAudio.Web/app/index.html\"", src);
        Assert.Matches(@"path == ""/""\)\s*await ServePageAsync", src);
        Assert.Matches(@"path == ""/novo"" \|\| path == ""/novo/""[\s\S]{0,200}WriteAsync\(stream, 301, ""text/plain; charset=utf-8"", Array\.Empty<byte>\(\), new\[\] \{ ""Location: /"" \}", src);
        Assert.Contains("301 => \"Moved Permanently\"", src);
        Assert.DoesNotContain("OldPage", src);
        Assert.DoesNotContain("ServeNewPageAsync", src);
    }

    [Fact]
    public void Icone_UsaUmUnicoCacheControlDe1Hora()
    {
        var src = ServerSource();
        Assert.Contains("\"image/png\", png, null, ct, \"private, max-age=3600\")", src);
        Assert.DoesNotContain("\"Cache-Control: private", src);
    }
}
