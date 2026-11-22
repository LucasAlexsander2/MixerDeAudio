using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, parte 9d: nome do arquivo recebido, limites, pastas, query e corte do texto da área de transferência (D25, D26, D28).</summary>
public class FileNameRulesTests
{
    private static readonly string B = ((char)92).ToString(); // a barra invertida

    [Theory]
    [InlineData("foto.jpg", "foto.jpg")]
    [InlineData("a b (1).txt", "a b (1).txt")]
    [InlineData("relatório ção.pdf", "relatório ção.pdf")]
    public void Nome_Normal_FicaIgual(string raw, string esperado) => Assert.Equal(esperado, FileNameRules.Clean(raw));

    [Fact]
    public void Nome_ComCaminhoDoWindows_FicaSoComOFinal() => Assert.Equal("x.txt", FileNameRules.Clean(".." + B + "x.txt"));

    [Fact]
    public void Nome_ComCaminhoCompleto_FicaSoComOFinal() => Assert.Equal("senha.txt", FileNameRules.Clean("C:" + B + "Users" + B + "a" + B + "senha.txt"));

    [Fact]
    public void Nome_ComBarraNormal_FicaSoComOFinal() => Assert.Equal("passwd", FileNameRules.Clean("../../etc/passwd"));

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("a/")]
    public void Nome_SemNadaUtil_ViraArquivo(string raw) => Assert.Equal("arquivo", FileNameRules.Clean(raw));

    [Fact]
    public void Nome_Nulo_ViraArquivo() => Assert.Equal("arquivo", FileNameRules.Clean(null));

    [Theory]
    [InlineData("a<b>c:d\"e|f?g*h.txt", "abcdefgh.txt")]
    [InlineData("a\tb\nc.txt", "abc.txt")]
    public void Nome_CaracteresProibidos_Saem(string raw, string esperado) => Assert.Equal(esperado, FileNameRules.Clean(raw));

    [Theory]
    [InlineData("nome.txt.", "nome.txt")]
    [InlineData("nome.txt   ", "nome.txt")]
    [InlineData("  nome.txt", "nome.txt")]
    public void Nome_PontoEEspacoNoFim_Saem(string raw, string esperado) => Assert.Equal(esperado, FileNameRules.Clean(raw));

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("NUL.tar.gz", "_NUL.tar.gz")]
    [InlineData("Com1", "_Com1")]
    [InlineData("LPT9.log", "_LPT9.log")]
    [InlineData("AUX", "_AUX")]
    [InlineData("PRN.x", "_PRN.x")]
    public void Nome_Reservado_GanhaPrefixo(string raw, string esperado) => Assert.Equal(esperado, FileNameRules.Clean(raw));

    [Theory]
    [InlineData("console.txt")]
    [InlineData("com10.txt")]
    [InlineData("nulo")]
    public void Nome_ParecidoComReservado_FicaIgual(string raw) => Assert.Equal(raw, FileNameRules.Clean(raw));

    [Fact]
    public void Nome_Longo_CortaEm120_MantendoAExtensao()
    {
        var name = FileNameRules.Clean(new string('a', 300) + ".jpeg");
        Assert.Equal(FileNameRules.MaxNameLength, name.Length);
        Assert.EndsWith(".jpeg", name);
    }

    [Fact]
    public void Nome_LongoSemExtensao_CortaEm120() => Assert.Equal(120, FileNameRules.Clean(new string('b', 500)).Length);

    [Fact]
    public void Nome_Com120Exatos_FicaIgual()
    {
        var raw = new string('c', 116) + ".txt";
        Assert.Equal(raw, FileNameRules.Clean(raw));
    }

    [Fact]
    public void Nome_ExtensaoGigante_AindaCabeEm120()
    {
        var name = FileNameRules.Clean("a." + new string('x', 300));
        Assert.True(name.Length <= 120);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    // ---- Colisão ----

    [Fact]
    public void Unique_Livre_FicaIgual() => Assert.Equal("a.txt", FileNameRules.Unique("a.txt", _ => false));

    [Fact]
    public void Unique_Existente_VaiParaDois()
    {
        var taken = new HashSet<string> { "a.txt" };
        Assert.Equal("a (2).txt", FileNameRules.Unique("a.txt", taken.Contains));
    }

    [Fact]
    public void Unique_DoisExistentes_VaiParaTres()
    {
        var taken = new HashSet<string> { "a.txt", "a (2).txt" };
        Assert.Equal("a (3).txt", FileNameRules.Unique("a.txt", taken.Contains));
    }

    [Fact]
    public void Unique_SemExtensao_PoeONumeroNoFim()
    {
        var taken = new HashSet<string> { "LEIAME" };
        Assert.Equal("LEIAME (2)", FileNameRules.Unique("LEIAME", taken.Contains));
    }

    [Fact]
    public void Unique_ComVariosPontos_MantemSoUltimaExtensao()
    {
        var taken = new HashSet<string> { "a.tar.gz" };
        Assert.Equal("a.tar (2).gz", FileNameRules.Unique("a.tar.gz", taken.Contains));
    }

    // ---- Limites ----

    [Fact]
    public void Limite_Normal500Mb_Teste5Mb()
    {
        Assert.Equal(500L * 1024 * 1024, FileNameRules.Limit(false));
        Assert.Equal(5L * 1024 * 1024, FileNameRules.Limit(true));
    }

    [Fact]
    public void Espaco_PrecisaDoTamanhoMaisUmGb()
    {
        const long gb = 1024L * 1024 * 1024;
        Assert.True(FileNameRules.HasRoom(gb + 100, 100));
        Assert.False(FileNameRules.HasRoom(gb + 99, 100));
        Assert.False(FileNameRules.HasRoom(0, 1));
    }

    // ---- Pastas (D26, D28) ----

    [Fact]
    public void Pasta_Normal_EDownloadsDoCelular() =>
        Assert.Equal(Path.Combine("D", "Downloads", "Do celular"), FileNameRules.ReceivedFolder(Path.Combine("D", "Downloads"), "T", false));

    [Fact]
    public void Pasta_DeTeste_EMixerCheckRecebidos() =>
        Assert.Equal(Path.Combine("T", "mixer-check", "recebidos"), FileNameRules.ReceivedFolder(Path.Combine("D", "Downloads"), "T", true));

    // ---- Query ----

    [Theory]
    [InlineData("nome=foto.jpg", "foto.jpg")]
    [InlineData("x=1&nome=a%20b.txt", "a b.txt")]
    [InlineData("nome=%C3%A7%C3%A3o.txt", "ção.txt")]
    [InlineData("nome=a+b.txt", "a b.txt")]
    [InlineData("nome=%ZZ", "%ZZ")]
    [InlineData("nome=..%5Cx.txt", "..\\x.txt")]
    public void Query_NomeDecodificado(string query, string esperado) =>
        Assert.Equal(esperado.Replace("\\", B), FileNameRules.NameFromQuery(query));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("outro=1")]
    [InlineData("nome")]
    public void Query_SemNome_Nulo(string? query) => Assert.Null(FileNameRules.NameFromQuery(query));

    // ---- Corte do texto da área de transferência (D25) ----

    [Fact]
    public void Clip_Curto_NaoCorta()
    {
        var (text, cut) = ClipboardBridge.Limit("MX-teste 123");
        Assert.Equal("MX-teste 123", text);
        Assert.False(cut);
    }

    [Fact]
    public void Clip_NuloOuVazio_Vazio()
    {
        Assert.Equal(("", false), ClipboardBridge.Limit(null));
        Assert.Equal(("", false), ClipboardBridge.Limit(""));
    }

    [Fact]
    public void Clip_Exato15000_NaoCorta()
    {
        var (text, cut) = ClipboardBridge.Limit(new string('x', 15000));
        Assert.Equal(15000, text.Length);
        Assert.False(cut);
    }

    [Fact]
    public void Clip_Acima_CortaEmQuinzeMil()
    {
        var (text, cut) = ClipboardBridge.Limit(new string('x', 15001));
        Assert.Equal(15000, text.Length);
        Assert.True(cut);
    }

    [Fact]
    public void Clip_ParSubstitutoNaBorda_NaoParteOEmoji()
    {
        var s = new string('x', 14999) + "😀" + "fim"; // o emoji ocupa as posições 14999 e 15000
        var (text, cut) = ClipboardBridge.Limit(s);
        Assert.True(cut);
        Assert.Equal(14999, text.Length);
        Assert.False(char.IsHighSurrogate(text[^1]));
    }

    // ---- Protocolo ----

    [Fact]
    public void Protocolo_ClipLer()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"clipLer\"}", out var c));
        Assert.IsType<ClipReadCommand>(c);
        Assert.Equal(RemoteTools.Transfer, RemoteCommandGroups.GroupOf(c!));
    }

    [Fact]
    public void Protocolo_ClipGravar_LevaOTexto()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"clipGravar\",\"s\":\"MX-teste 123\"}", out var c));
        Assert.Equal("MX-teste 123", Assert.IsType<ClipWriteCommand>(c).Text);
        Assert.Equal(RemoteTools.Transfer, RemoteCommandGroups.GroupOf(c!));
    }

    [Fact]
    public void Protocolo_ClipGravar_TextoVazioLimpa()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"clipGravar\",\"s\":\"\"}", out var c));
        Assert.Equal("", Assert.IsType<ClipWriteCommand>(c).Text);
    }

    [Fact]
    public void Protocolo_ClipGravar_CortaEmQuinzeMil()
    {
        var json = "{\"t\":\"clipGravar\",\"s\":\"" + new string('a', 15500) + "\"}";
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Equal(15000, Assert.IsType<ClipWriteCommand>(c).Text.Length);
    }

    [Theory]
    [InlineData("{\"t\":\"clipGravar\"}")]
    [InlineData("{\"t\":\"clipGravar\",\"s\":5}")]
    public void Protocolo_ClipGravar_SemTexto_Invalido(string json) =>
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out _));

    [Fact]
    public void Protocolo_BuildClip_TemTextoECortado()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(RemoteProtocol.BuildClip("olá ✓", true));
        Assert.Equal("clip", doc.RootElement.GetProperty("t").GetString());
        Assert.Equal("olá ✓", doc.RootElement.GetProperty("s").GetString());
        Assert.True(doc.RootElement.GetProperty("cortado").GetBoolean());
    }

    // ---- Rotas (D26): quem trata o corpo ----

    [Fact]
    public void Rota_PostArquivo_EEnvio()
    {
        var head = RemoteRules.ParseHead("POST /arquivo?nome=a.txt HTTP/1.1\r\nContent-Length: 10\r\nHost: x");
        Assert.NotNull(head);
        Assert.True(RemoteRules.IsUpload(head!));
        Assert.Equal(10, RemoteRules.UploadLength(head!));
    }

    [Theory]
    [InlineData("GET /arquivo HTTP/1.1\r\nHost: x")]
    [InlineData("POST /parear HTTP/1.1\r\nHost: x")]
    [InlineData("POST /arquivos HTTP/1.1\r\nHost: x")]
    public void Rota_OutrasRotas_NaoSaoEnvio(string text) => Assert.False(RemoteRules.IsUpload(RemoteRules.ParseHead(text)!));

    [Theory]
    [InlineData("POST /arquivo HTTP/1.1\r\nHost: x", -1)]
    [InlineData("POST /arquivo HTTP/1.1\r\nContent-Length: abc\r\nHost: x", -1)]
    [InlineData("POST /arquivo HTTP/1.1\r\nContent-Length: -5\r\nHost: x", -1)]
    [InlineData("POST /arquivo HTTP/1.1\r\nContent-Length: 600000000\r\nHost: x", 600000000)]
    public void Rota_ContentLength_ObrigatorioEValido(string text, long esperado) =>
        Assert.Equal(esperado, RemoteRules.UploadLength(RemoteRules.ParseHead(text)!));
}
