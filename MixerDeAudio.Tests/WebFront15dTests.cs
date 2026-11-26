using System.Text.RegularExpressions;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

// Fase 15d: Janelas, Atalhos e Enviar do novo front, conferidos direto nos arquivos (sem navegador).
public class WebFront15dTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MixerDeAudio.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string App(params string[] parts) => Path.Combine(new[] { Root(), "MixerDeAudio", "Remote", "Web", "app" }.Concat(parts).ToArray());

    private static string Js(string name) => File.ReadAllText(App("js", "telas", name + ".js"));
    private static string Css(string name) => File.ReadAllText(App("css", "telas", name + ".css"));

    private static readonly string[] Modules = { "janelas", "atalhos", "enviar" };

    // Tipos aceitos pelo PC: os `case "..."` do analisador de comandos (RemoteProtocol.ParseCommand).
    private static HashSet<string> AcceptedTypes()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "MixerDeAudio", "Remote", "RemoteProtocol.cs"));
        var start = src.IndexOf("ParseCommand(string? json", StringComparison.Ordinal);
        Assert.True(start > 0);
        return Regex.Matches(src[start..], "case \"(?<t>[A-Za-z]+)\":").Select(m => m.Groups["t"].Value).ToHashSet();
    }

    private static HashSet<string> SentTypes(string js) =>
        Regex.Matches(js, @"\bt:\s*'(?<t>[A-Za-z]+)'").Select(m => m.Groups["t"].Value).ToHashSet();

    [Theory]
    [InlineData("janelas")]
    [InlineData("atalhos")]
    [InlineData("enviar")]
    public void Modulo_SoMandaTiposQueOPcAceita(string modulo)
    {
        var enviados = SentTypes(Js(modulo));
        Assert.NotEmpty(enviados);
        var aceitos = AcceptedTypes();
        Assert.All(enviados, t => Assert.Contains(t, aceitos));
    }

    [Fact]
    public void TiposEsperados_DeCadaModulo()
    {
        Assert.Equal(new[] { "janela", "janelas" }, SentTypes(Js("janelas")).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "atalho", "atualizar" }, SentTypes(Js("atalhos")).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "clipGravar", "clipLer" }, SentTypes(Js("enviar")).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("{\"t\":\"janelas\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":12345,\"acao\":\"frente\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":12345,\"acao\":\"minimizar\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":12345,\"acao\":\"fechar\"}")]
    [InlineData("{\"t\":\"atalho\",\"nome\":\"Steam\"}")]
    [InlineData("{\"t\":\"atualizar\"}")]
    [InlineData("{\"t\":\"clipLer\"}")]
    [InlineData("{\"t\":\"clipGravar\",\"s\":\"texto\"}")]
    [InlineData("{\"t\":\"assinar\",\"g\":\"janelas\",\"on\":true}")]
    public void MensagensDosModulos_SaoValidasParaOPc(string json)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var command));
        Assert.NotNull(command);
    }

    [Fact]
    public void AcoesDeJanela_UsadasNoJs_ExistemNoPc()
    {
        var js = Js("janelas");
        foreach (var acao in new[] { "frente", "minimizar", "fechar" })
        {
            Assert.Contains($"'{acao}'", js);
            Assert.True(WindowFilter.TryParseAction(acao, out _));
        }
        var usadas = Regex.Matches(js, @"agir\([^,]+,\s*'(?<a>[a-z]+)'\)").Select(m => m.Groups["a"].Value).ToHashSet();
        Assert.All(usadas, a => Assert.True(WindowFilter.TryParseAction(a, out _)));
    }

    [Fact]
    public void Janelas_AssinaSoNoTopo_ESoltaAoSairEAoDestruir()
    {
        var js = Js("janelas");
        Assert.Contains("ctx.assinar('janelas')", js);
        Assert.Single(Regex.Matches(js, @"ctx\.assinar\("));
        Assert.Contains("aoTopo(sim)", js);
        Assert.Contains("destruir()", js);
        // o soltar aparece no ramo "saiu do topo" e no destruir
        Assert.True(Regex.Matches(js, @"soltar\(\)").Count >= 2);
        Assert.Contains("ctx.aoMensagem('janelas'", js);
        Assert.Contains("cancelar();", js);
    }

    [Fact]
    public void Janelas_FecharPedeConfirmacao_EAConfirmacaoEnviaAAcao()
    {
        var js = Js("janelas");
        Assert.Contains("criarAviso", js);
        Assert.Contains("perigo: true", js);
        Assert.Contains("container: ctx.camadas", js);
        // o envio de "fechar" só acontece dentro de aoConfirmar
        Assert.Matches(@"aoConfirmar:\s*\(\)\s*=>\s*\{\s*agir\(w\.id,\s*'fechar'\)", js);
        Assert.DoesNotMatch(@"aoClicar:\s*\(\)\s*=>\s*\{?\s*agir\(w\.id,\s*'fechar'\)", js);
    }

    [Fact]
    public void Janelas_ListaSemDataSemGesto_PoisSoRolaNaVertical()
    {
        Assert.DoesNotContain("setAttribute('data-sem-gesto'", Js("janelas"));
        Assert.DoesNotContain("setAttribute('data-sem-gesto'", Js("atalhos"));
    }

    [Fact]
    public void Enviar_TextoSelecionavelTemDataSemGesto()
    {
        Assert.Contains("setAttribute('data-sem-gesto', '')", Js("enviar"));
    }

    [Fact]
    public void Enviar_LimitesEMensagensDaPaginaAntiga()
    {
        var js = Js("enviar");
        Assert.Contains($"MAX_ARQUIVO = {FileNameRules.MaxBytes}", js);
        Assert.Contains("MAX_TEXTO = 4000", js);
        Assert.True(4000 <= ClipboardBridge.Limit(new string('a', 20000)).Text.Length);
        foreach (var status in new[] { 401, 403, 409, 411, 413, 507 })
            Assert.Contains($"{status}:", js);
        // a ordem de leitura do corpo do upload e a rota são as da página antiga
        Assert.Contains("'/arquivo?nome=' + encodeURIComponent(", js);
        Assert.Contains("x.upload.onprogress", js);
        Assert.Contains("xhr.abort()", js);
        Assert.Contains("x.onabort", js);
        Assert.Contains("clipGravar", js);
        Assert.Contains("ctx.aoMensagem('clip'", js);
    }

    [Fact]
    public void Atalhos_LeAListaDoEstadoEUsaIconesQueExistem()
    {
        var js = Js("atalhos");
        Assert.Contains("estado.pc.atalhos", js);
        Assert.Contains("ctx.aoMudar(", js);
        var icones = File.ReadAllText(App("js", "icones.js"));
        var usados = Regex.Matches(js, @"\],\s*'(?<i>[a-z]+)'\]|return\s+'(?<i2>[a-z]+)'").Select(m => m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value).ToHashSet();
        Assert.NotEmpty(usados);
        foreach (var nome in usados) Assert.Matches($@"\b{nome}\s*:", icones);
    }

    [Theory]
    [InlineData("janelas")]
    [InlineData("atalhos")]
    [InlineData("enviar")]
    public void Css_SoTokens_ESeletoresComOPrefixoDaArea(string modulo)
    {
        var css = Css(modulo);
        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);
        Assert.DoesNotContain("rgb(", css);
        var seletores = Regex.Matches(css, @"(?:^|\})\s*(?<s>[^{}/@]+)\{").Select(m => m.Groups["s"].Value.Trim());
        foreach (var s in seletores)
            foreach (var parte in s.Split(','))
                Assert.StartsWith("." + modulo + "-", parte.Trim());
    }

    [Theory]
    [InlineData("janelas")]
    [InlineData("atalhos")]
    [InlineData("enviar")]
    public void Modulo_ExportaCriarTela_ENaoMexeNosArquivosFechados(string modulo)
    {
        var js = Js(modulo);
        Assert.Contains("export function criarTela(ctx)", js);
        Assert.DoesNotContain("provisoria", js);
        // só importa componentes e ícones (nada de conexao.js, estado.js ou pilha.js direto: tudo vem do ctx)
        foreach (Match m in Regex.Matches(js, @"from\s+'(?<p>[^']+)'"))
            Assert.Matches(@"^\.\./(componentes/[a-z]+|icones)\.js$", m.Groups["p"].Value);
    }

    [Fact]
    public void Janelas_IconeVemDaRotaIcone_ComFallbackParaInicial()
    {
        var js = Js("janelas");
        Assert.Contains("'/icone/' + encodeURIComponent(w.icone)", js);
        Assert.Contains("img.onerror", js);
        Assert.Contains("charAt(0).toUpperCase()", js);
    }
}
