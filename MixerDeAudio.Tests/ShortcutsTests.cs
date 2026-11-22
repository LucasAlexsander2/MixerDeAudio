using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, parte 9c: lista de atalhos (D23) e o comando <c>atalho</c>.</summary>
public class ShortcutsTests
{
    [Theory]
    [InlineData("Teste.lnk", true)]
    [InlineData("Site.url", true)]
    [InlineData("MAIUSCULA.LNK", true)]
    [InlineData("Site.URL", true)]
    [InlineData("programa.exe", false)]
    [InlineData("script.bat", false)]
    [InlineData("nota.txt", false)]
    [InlineData("semextensao", false)]
    [InlineData("duplo.lnk.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SoLnkEUrl(string? name, bool expected) => Assert.Equal(expected, ShortcutRules.IsShortcutFile(name));

    [Fact]
    public void Lista_FiltraOrdenaECortaOCaminho()
    {
        var files = new[] { @"C:\pasta\Zeta.lnk", @"C:\pasta\alfa.url", @"C:\pasta\Beta.lnk", @"C:\pasta\x.exe", @"C:\pasta\leia.txt" };
        Assert.Equal(new[] { "alfa.url", "Beta.lnk", "Zeta.lnk" }, ShortcutRules.Select(files));
    }

    [Fact]
    public void Lista_NomesIguaisComExtensoesDiferentes_FicaOLnk()
    {
        var files = new[] { "Teste.url", "Teste.lnk" };
        Assert.Equal(new[] { "Teste.lnk" }, ShortcutRules.Select(files));
    }

    [Fact]
    public void Lista_LimiteDe30()
    {
        var files = Enumerable.Range(0, 45).Select(i => $"atalho{i:00}.lnk").ToList();
        var list = ShortcutRules.Select(files);
        Assert.Equal(ShortcutRules.MaxShortcuts, list.Count);
        Assert.Equal(30, list.Count);
        Assert.Equal("atalho00.lnk", list[0]);
        Assert.Equal("atalho29.lnk", list[^1]);
    }

    [Fact]
    public void Lista_Vazia() => Assert.Empty(ShortcutRules.Select(Array.Empty<string>()));

    [Fact]
    public void Lista_ArquivoSoComExtensao_Ignorado() => Assert.Empty(ShortcutRules.Select(new[] { ".lnk" }));

    [Fact]
    public void NomesMostrados_SemExtensao() =>
        Assert.Equal(new[] { "Teste", "Meu Site" }, ShortcutRules.DisplayNames(new[] { "Teste.url", "Meu Site.lnk" }));

    // ---- Nome fora da lista (D23) ----

    [Fact]
    public void Resolve_NomeDaLista_DevolveOArquivo() =>
        Assert.Equal("Teste.url", ShortcutRules.Resolve(new[] { "Outro.lnk", "Teste.url" }, "Teste"));

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("../x")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData("Teste.url")]   // com extensão: não é o nome mostrado
    [InlineData("teste")]       // maiúsculas diferentes
    [InlineData("Teste ")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_NomeForaDaLista_Ignorado(string? name) =>
        Assert.Null(ShortcutRules.Resolve(new[] { "Teste.url" }, name));

    // ---- Pasta ----

    [Fact]
    public void Pasta_NormalEmAtalhosDentroDosDados() =>
        Assert.Equal(Path.Combine(@"C:\dados", "Atalhos"), ShortcutFolder.PathFor(@"C:\dados", false));

    [Fact]
    public void Pasta_DeTesteEmTempMixerCheckAtalhos()
    {
        var p = ShortcutFolder.PathFor(@"C:\dados", true);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "mixer-check", "atalhos"), p);
        Assert.DoesNotContain("dados", p);
    }

    [Fact]
    public void Pasta_InexistenteDaListaVazia() =>
        Assert.Empty(ShortcutFolder.Files(Path.Combine(Path.GetTempPath(), "mx-nao-existe-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void Pasta_LeArquivosDeVerdade()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mx-atalhos-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            ShortcutFolder.Ensure(dir);
            File.WriteAllText(Path.Combine(dir, "Teste.url"), "[InternetShortcut]\r\nURL=https://example.com/\r\n");
            File.WriteAllText(Path.Combine(dir, "leia.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "sub.lnk"));
            var files = ShortcutFolder.Files(dir);
            Assert.Equal(new[] { "Teste.url" }, files.Where(f => f != "sub.lnk"));
            Assert.Equal("Teste.url", ShortcutRules.Resolve(files, "Teste"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    // ---- Protocolo ----

    [Fact]
    public void Comando_Atalho()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"atalho\",\"nome\":\"Teste\"}", out var cmd));
        Assert.Equal("Teste", Assert.IsType<ShortcutCommand>(cmd).Name);
    }

    [Theory]
    [InlineData("{\"t\":\"atalho\"}")]
    [InlineData("{\"t\":\"atalho\",\"nome\":\"\"}")]
    [InlineData("{\"t\":\"atalho\",\"nome\":5}")]
    public void Comando_Atalho_Invalido(string json) =>
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out _));

    [Fact]
    public void Estado_TrazAtalhosDoPc()
    {
        var state = new RemoteState(Array.Empty<RemoteOutput>(), true, 50, false, Array.Empty<RemoteApp>(), new[] { "atalhos" }, true,
            new RemotePcState(Shortcuts: new[] { "Teste", "Meu Site" }));
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(state));
        var atalhos = doc.RootElement.GetProperty("pc").GetProperty("atalhos");
        Assert.Equal(new[] { "Teste", "Meu Site" }, atalhos.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Estado_SemAtalhos_SemCampo()
    {
        var state = new RemoteState(Array.Empty<RemoteOutput>(), true, 50, false, Array.Empty<RemoteApp>(), new[] { "energia" }, true,
            new RemotePcState(PowerOn: true));
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(state));
        Assert.False(doc.RootElement.GetProperty("pc").TryGetProperty("atalhos", out _));
    }

    // ---- RecordingPc ----

    [Fact]
    public void RecordingPc_GravaOAtalhoENaoAbreNada()
    {
        var log = new List<string>();
        var pc = new RecordingPc(log.Add);
        pc.OpenShortcut(@"C:\nao-abre\Teste.url", "Teste");
        Assert.Equal(new[] { "teste-pc: atalho Teste" }, log);
    }
}
