using System.Text.RegularExpressions;

namespace MixerDeAudio.Tests;

// Fase 15a: Início, Opções e Som do novo front (Remote/Web/app), conferidos direto do repositório, sem navegador.
public class WebFront15aTests
{
    private static string RepoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MixerDeAudio.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoDir(), "MixerDeAudio", "Remote", "Web", "app");
    private static string Js(string name) => File.ReadAllText(Path.Combine(AppDir(), "js", "telas", name + ".js"));

    private static readonly string[] Modulos = ["inicio", "opcoes", "som"];

    // nomes de ícone de icones.js (as chaves do objeto IC)
    private static HashSet<string> Icones() =>
        Regex.Matches(File.ReadAllText(Path.Combine(AppDir(), "js", "icones.js")), @"^\s{2}(?<n>[a-z0-9]+):'", RegexOptions.Multiline)
            .Select(m => m.Groups["n"].Value).ToHashSet();

    // os tipos "t" que o PC aceita: os `case "..."` do ParseCommand (RemoteProtocol.cs, o mesmo que o RemoteSession usa)
    private static HashSet<string> TiposAceitos()
    {
        var src = File.ReadAllText(Path.Combine(RepoDir(), "MixerDeAudio", "Remote", "RemoteProtocol.cs"));
        var inicio = src.IndexOf("switch (t.GetString())", StringComparison.Ordinal);
        Assert.True(inicio > 0);
        return Regex.Matches(src[inicio..], @"^\s{16}case ""(?<t>[a-zA-Z]+)"":", RegexOptions.Multiline).Select(m => m.Groups["t"].Value).ToHashSet();
    }

    [Fact]
    public void Modulos_TiposDeMensagemSaoOsQueOPcAceita()
    {
        var aceitos = TiposAceitos();
        Assert.Contains("padrao", aceitos);
        var usados = new HashSet<string>();
        foreach (var m in Modulos)
            foreach (Match x in Regex.Matches(Js(m), @"\bt: '(?<t>[a-zA-Z]+)'"))
                usados.Add(x.Groups["t"].Value);
        // os que o Início, o Som e as Opções mandam, sem perder nenhum da página antiga
        foreach (var t in new[] { "padrao", "geral", "geralMudo", "volume", "mudo", "rota", "midia", "energia" })
            Assert.Contains(t, usados);
        Assert.All(usados, t => Assert.Contains(t, aceitos));
        // o que o Início assina é um grupo que o PC conhece
        Assert.Contains("assinar('info')", Js("inicio"));
        Assert.Contains("\"info\"", File.ReadAllText(Path.Combine(RepoDir(), "MixerDeAudio", "Remote", "RemoteProtocol.cs")));
    }

    [Fact]
    public void Som_AcoesDeMidiaESaidasSaoAsDaPaginaAntiga()
    {
        var som = Js("som");
        foreach (var acao in new[] { "'ant'", "'pp'", "'prox'" }) Assert.Contains("acao: " + acao, som);
        Assert.Contains("/icone/", som);                       // ícones dos apps, como na página antiga
        Assert.Contains("{ t: 'padrao', id: s.id }", som);    // tocar numa saída = virar a padrão
        Assert.Contains("{ t: 'rota', app: chave, id }", som); // saída do app pela folha
        Assert.Contains("criarFolha(", som);
        Assert.Contains("soMidia", som);
        Assert.Matches(@"export function criarTela\(ctx\)", som);
        Assert.Contains("Nenhum app com som no momento.", som);
    }

    [Fact]
    public void Favoritos_CadaItemTemIconeEAcaoQueExistem()
    {
        var inicio = Js("inicio");
        var icones = Icones();
        Assert.NotEmpty(icones);

        // catálogo fixo: ícone existente e `tipo` tratado em executar()
        var fixos = Regex.Matches(inicio, @"\{ id: '(?<id>[a-z]+)', rotulo: '[^']+', curto: '[^']+', icone: '(?<ic>[a-z]+)', tipo: '(?<tipo>[a-z]+)'");
        Assert.Equal(4, fixos.Count);
        var tipos = Regex.Match(inicio, @"TIPOS_FAVORITO = \[(?<l>[^\]]+)\]").Groups["l"].Value;
        var tratados = Regex.Matches(inicio.Substring(inicio.IndexOf("function executar", StringComparison.Ordinal)), @"case '(?<c>[a-z]+)':").Select(m => m.Groups["c"].Value).ToHashSet();
        foreach (Match f in fixos)
        {
            Assert.Contains(f.Groups["ic"].Value, icones);
            Assert.Contains("'" + f.Groups["tipo"].Value + "'", tipos);
            Assert.Contains(f.Groups["tipo"].Value, tratados);
        }
        foreach (var t in Regex.Matches(tipos, @"'(?<t>[a-z]+)'").Select(m => m.Groups["t"].Value))
            Assert.Contains(t, tratados);

        // ícones trocados em tempo de execução (mudo, play/pausa) e os das saídas
        foreach (var ic in new[] { "mute", "volume", "pause", "play", "headphones", "speaker", "monitor" }) Assert.Contains(ic, icones);
        foreach (Match a in Regex.Matches(inicio, @"\{ nome: '[a-z]+', rotulo: '[^']+', icone: '(?<ic>[a-z]+)'"))
            Assert.Contains(a.Groups["ic"].Value, icones);

        // os padrões do protótipo e a chave guardada (D11)
        foreach (var id in new[] { "'mudo'", "'brilho'", "'bloquear'", "'area:tela'", "'play'" }) Assert.Contains(id, inicio);
        Assert.Contains("'inicio.favoritos'", inicio);
        Assert.Contains("'Bloquear o PC?'", inicio);   // ação perigosa com confirmação (D10)
    }

    [Fact]
    public void Inicio_DevolveAoTopoMantemAreasEUsaFaixaLarga()
    {
        var inicio = Js("inicio");
        Assert.Matches(@"export const AREAS = \[", inicio);
        Assert.Matches(@"export function areasVisiveis\(", inicio);
        Assert.Matches(@"export function criarTelaInicio\(", inicio);
        Assert.Matches(@"aoTopo\(sim\)", inicio);
        Assert.Matches(@"atualizar\(estado\)", inicio);
        Assert.Contains("criarFaixa({ larga: true })", inicio);
        Assert.Contains("aoAbrirArea('estado')", inicio);   // tocar no estado rápido abre o Estado do PC
        Assert.Contains("'GPU'", inicio);
        Assert.Contains("criarMedidor(", inicio);
        // assina o info só com o Início no topo
        Assert.Matches(@"noTopo && ligado", inicio);
        var app = File.ReadAllText(Path.Combine(AppDir(), "js", "app.js"));
        Assert.Contains("if (inicio.aoTopo) inicio.aoTopo(sim)", app);
    }

    [Fact]
    public void Opcoes_TemMaoUsadaEFavoritos()
    {
        var op = Js("opcoes");
        Assert.Contains("export function criarGrupoMao", op);   // a Galeria usa
        Assert.Contains("'Favoritos do Início'", op);
        Assert.Contains("criarInterruptor(", op);
        Assert.Contains("CHAVE_FAVORITOS", op);
        Assert.Matches(@"export function criarTelaOpcoes\(\{ ctx", op);
    }

    [Fact]
    public void ModulosDa15a_UsamSoComponentesFechadosECoresPorToken()
    {
        var componentes = Directory.EnumerateFiles(Path.Combine(AppDir(), "js", "componentes"), "*.js").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var m in Modulos)
        {
            foreach (Match i in Regex.Matches(Js(m), @"from '\.\./componentes/(?<c>[a-z]+)\.js'"))
                Assert.Contains(i.Groups["c"].Value, componentes);
            Assert.DoesNotContain("innerHTML = '<", Js(m));
        }
        foreach (var css in new[] { "inicio.css", "som.css" })
        {
            var txt = File.ReadAllText(Path.Combine(AppDir(), "css", "telas", css));
            Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", txt);   // sem cor fixa
            Assert.DoesNotMatch(@"rgb\(|hsl\(", txt);
        }
    }

    [Fact]
    public void Gpu_LeitorPorPdhSemPacoteNovo()
    {
        var leitor = File.ReadAllText(Path.Combine(RepoDir(), "MixerDeAudio", "Pc", "GpuReader.Native.cs"));
        Assert.Contains("pdh.dll", leitor);
        Assert.Contains(@"\GPU Engine(*)\Utilization Percentage", leitor);
        Assert.Contains("PcInfoRules.GpuPercent", leitor);
        // só nasce junto com o leitor do info (que só existe com o info assinado)
        var info = File.ReadAllText(Path.Combine(RepoDir(), "MixerDeAudio", "Pc", "PcInfo.Native.cs"));
        Assert.Contains("GpuReader _gpu = new()", info);
        var csproj = File.ReadAllText(Path.Combine(RepoDir(), "MixerDeAudio", "MixerDeAudio.csproj"));
        Assert.DoesNotContain("Pdh", csproj, StringComparison.OrdinalIgnoreCase);
        var sessao = File.ReadAllText(Path.Combine(RepoDir(), "MixerDeAudio", "Remote", "RemoteSession.cs"));
        Assert.Contains("_info?.Dispose()", sessao);
    }
}
