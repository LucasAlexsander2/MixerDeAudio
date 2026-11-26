using System.Text.RegularExpressions;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

// Fase 15b: Energia, Monitores, Timer de sono e Estado do PC no novo front. Confere os arquivos direto do repositório, sem navegador.
public class WebFront15bTests
{
    private static readonly string[] Modules = { "energia", "monitores", "sono", "estado-pc" };

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MixerDeAudio.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "MixerDeAudio", "Remote", "Web", "app");
    }

    private static string Js(string module) => File.ReadAllText(Path.Combine(AppDir(), "js", "telas", module + ".js"));
    private static string Css(string module) => File.ReadAllText(Path.Combine(AppDir(), "css", "telas", module + ".css"));

    private static string Strip(string js) => Regex.Replace(js, @"//[^\n]*", ""); // sem comentários de linha

    [Fact]
    public void Modulos_TemCriarTelaEDevolvemUmaTelaDeVerdade()
    {
        foreach (var m in Modules)
        {
            var js = Js(m);
            Assert.Contains("export function criarTela(ctx)", js);
            Assert.DoesNotContain("criarTelaProvisoria", js);
            Assert.Contains("destruir()", js);
            Assert.Contains("ctx.voltar()", js);
            Assert.True(Css(m).Length > 100, $"{m}.css ainda está vazio");
        }
    }

    [Fact]
    public void TiposMandadosPeloJs_SaoAceitosPeloPc()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var m in Modules)
            foreach (Match x in Regex.Matches(Strip(Js(m)), @"\bt:\s*'(?<t>[A-Za-z]+)'"))
                found.Add(x.Groups["t"].Value);

        // Só estes tipos são mandados pelas quatro telas (nada fora do contrato).
        Assert.Equal(new[] { "acordado", "atualizar", "brilho", "energia", "energiaCancelar", "projecao", "sono", "telas" }, found.ToArray());
        foreach (var t in found)
            Assert.NotEqual(ParseStatus.UnknownType, RemoteProtocol.ParseCommand($"{{\"t\":\"{t}\"}}", out _));
    }

    [Theory]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":0}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"reiniciar\",\"seg\":1800}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"suspender\",\"seg\":86400}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"bloquear\",\"seg\":0}")]
    [InlineData("{\"t\":\"energiaCancelar\"}")]
    [InlineData("{\"t\":\"projecao\",\"modo\":\"principal\"}")]
    [InlineData("{\"t\":\"projecao\",\"modo\":\"duplicar\"}")]
    [InlineData("{\"t\":\"projecao\",\"modo\":\"estender\"}")]
    [InlineData("{\"t\":\"projecao\",\"modo\":\"segunda\"}")]
    [InlineData("{\"t\":\"atualizar\"}")]
    [InlineData("{\"t\":\"telas\",\"acao\":\"apagar\"}")]
    [InlineData("{\"t\":\"telas\",\"acao\":\"acordar\"}")]
    [InlineData("{\"t\":\"brilho\",\"tela\":1,\"valor\":55}")]
    [InlineData("{\"t\":\"acordado\",\"on\":true}")]
    [InlineData("{\"t\":\"sono\",\"min\":30}")]
    [InlineData("{\"t\":\"sono\",\"min\":0}")]
    public void MensagensNoFormatoQueAsTelasMandam_SaoValidas(string json)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        Assert.NotNull(c);
    }

    [Fact]
    public void Energia_TemposEAcoesDoJs_ValemParaOPc()
    {
        var js = Js("energia");
        var tempos = Regex.Matches(js, @"\[(?<s>\d+), '(?<n>[^']+)'\]").Select(x => int.Parse(x.Groups["s"].Value)).ToList();
        Assert.Contains(0, tempos);
        Assert.Contains(86400, tempos);
        Assert.True(tempos.Count >= 15, "perdeu algum tempo da página antiga");
        Assert.All(tempos, s => Assert.True(PowerRules.IsValidSeconds(s), $"seg {s} recusado pelo PC"));

        var acoes = Regex.Matches(js, @"valor: '(?<a>[a-z]+)', texto").Select(x => x.Groups["a"].Value).ToList();
        Assert.Equal(new[] { "desligar", "reiniciar", "suspender", "bloquear" }, acoes);
        Assert.All(acoes, a => Assert.True(PowerRules.TryParse(a, out _), a));
    }

    [Fact]
    public void Energia_AcoesPedemConfirmacaoECancelarExiste()
    {
        var js = Js("energia");
        Assert.Contains("criarAviso", js);
        Assert.Contains("container: ctx.camadas", js);
        // o botão de cada ação só abre o aviso; o envio fica em aoConfirmar
        Assert.Contains("aoConfirmar", js);
        Assert.Contains("energiaCancelar", js);
        Assert.Contains("estado.pc.energia", js);
        Assert.Contains("clearInterval(tick)", js);
    }

    [Fact]
    public void Monitores_ModosDoJs_ValemEFuncoesDaPaginaAntigaEstaoLa()
    {
        var js = Js("monitores");
        var modos = Regex.Matches(js, @"valor: '(?<m>[a-z]+)', texto").Select(x => x.Groups["m"].Value).ToList();
        Assert.Equal(new[] { "principal", "duplicar", "estender", "segunda" }, modos);
        Assert.All(modos, m => Assert.True(DisplayTopology.TryParseMode(m, out _), m));
        Assert.Contains("'apagar'", js);
        Assert.Contains("'acordar'", js);
        Assert.Contains("intervaloMs: 150", js); // brilho: 1 envio a cada 150 ms, como na página antiga
        Assert.Contains("DDC/CI", js);            // tela sem brilho
        Assert.Contains("Manter a tela ligada", js);
        Assert.Contains("1500", js);              // pede `atualizar` depois de trocar a projeção
    }

    [Fact]
    public void Sono_MinutosDoJs_ValemEHaCancelar()
    {
        var js = Js("sono");
        var mins = Regex.Matches(js, @"\[(?<m>\d+), '(?<n>[^']+)'\]").Select(x => int.Parse(x.Groups["m"].Value)).ToList();
        Assert.Equal(10, mins.Count);
        Assert.All(mins, m => Assert.True(SleepRules.IsValidMinutes(m), $"{m} min recusado pelo PC"));
        Assert.Contains("min: 0", js);
        Assert.Contains("clearInterval(tick)", js);
    }

    [Fact]
    public void EstadoPc_AssinaInfoSoNoTopoESoltaAoSair()
    {
        var js = Js("estado-pc");
        Assert.Contains("ctx.assinar('info')", js);
        Assert.Single(Regex.Matches(js, @"ctx\.assinar\("));
        Assert.Contains("aoTopo(sim)", js);
        Assert.Contains("if (sim && !soltar)", js);
        Assert.Contains("else if (!sim && soltar)", js);
        Assert.Contains("destruir()", js);
        Assert.Contains("ctx.aoMensagem('info'", js);
        // sem `gpu` no info a tela mostra "GPU —"; o campo, quando vem, é um número
        Assert.Contains("typeof m.gpu === 'number'", js);
        Assert.Contains("'—'", js);
        // os dados da página antiga continuam: CPU, memória, ligado há, bateria e disco
        foreach (var palavra in new[] { "Memória", "Tempo ligado", "Bateria", "Sem bateria", "carregando", "Disco do Windows" })
            Assert.Contains(palavra, js);
    }

    [Fact]
    public void OutrasTelas_NaoAssinamNada()
    {
        foreach (var m in new[] { "energia", "monitores", "sono" })
            Assert.DoesNotContain("ctx.assinar", Js(m));
    }

    [Fact]
    public void Modulos_SoImportamComponentesEIcones_SemMexerNosFechados()
    {
        foreach (var m in Modules)
        {
            var specs = Regex.Matches(Js(m), @"from\s+'(?<p>[^']+)'").Select(x => x.Groups["p"].Value).ToList();
            Assert.All(specs, s => Assert.StartsWith("../componentes/", s));
        }
    }

    [Fact]
    public void Icones_UsadosExistemEmIconesJs()
    {
        var icones = File.ReadAllText(Path.Combine(AppDir(), "js", "icones.js"));
        var existentes = Regex.Matches(icones, @"^\s*([a-z0-9]+):", RegexOptions.Multiline).Select(x => x.Groups[1].Value).ToHashSet();
        foreach (var m in Modules)
            foreach (Match x in Regex.Matches(Js(m), @"icone: '(?<i>[a-z0-9]+)'"))
                Assert.Contains(x.Groups["i"].Value, existentes);
    }

    [Fact]
    public void Css_SoUsaTokensDeCor()
    {
        foreach (var m in Modules)
        {
            var css = Css(m);
            Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);
            Assert.DoesNotMatch(@"\b(rgb|rgba|hsl)\(", css);
        }
    }
}
