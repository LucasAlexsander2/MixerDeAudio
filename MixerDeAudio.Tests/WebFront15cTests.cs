using System.Text.Json;
using System.Text.RegularExpressions;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

// Fase 15c: o que os módulos Controle e Tela do PC mandam é aceito pelo PC (tipos de mensagem, teclas da lista fixa, modificadores).
public class WebFront15cTests
{
    private static readonly string[] Modulos = { "controle", "tela-pc", "teclado-pc" };

    private static string TelasDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MixerDeAudio.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "MixerDeAudio", "Remote", "Web", "app", "js", "telas");
    }

    private static string Js(string modulo) => File.ReadAllText(Path.Combine(TelasDir(), modulo + ".js"));

    // Uma mensagem de exemplo de cada tipo que os módulos mandam, com os campos que eles usam.
    private static readonly Dictionary<string, string> Exemplos = new()
    {
        ["mouse"] = "{\"t\":\"mouse\",\"dx\":3,\"dy\":-2}",
        ["clique"] = "{\"t\":\"clique\",\"b\":\"esq\",\"duplo\":true}",
        ["botao"] = "{\"t\":\"botao\",\"b\":\"esq\",\"baixo\":true}",
        ["rolar"] = "{\"t\":\"rolar\",\"dx\":0,\"dy\":2}",
        ["texto"] = "{\"t\":\"texto\",\"s\":\"oi\"}",
        ["tecla"] = "{\"t\":\"tecla\",\"k\":\"left\",\"mods\":[\"ctrl\"]}",
        ["mouseAbs"] = "{\"t\":\"mouseAbs\",\"mon\":0,\"x\":0.5,\"y\":0.5}",
        ["acordado"] = "{\"t\":\"acordado\",\"on\":true}",
    };

    private static IEnumerable<string> TiposUsados(string modulo) =>
        Regex.Matches(Js(modulo), @"\bt:\s*'(?<t>[A-Za-z]+)'").Select(m => m.Groups["t"].Value).Distinct();

    [Fact]
    public void Modulos_SoMandamTiposQueOPcAceita()
    {
        var todos = Modulos.SelectMany(TiposUsados).Distinct().ToList();
        Assert.NotEmpty(todos);
        foreach (var t in todos)
        {
            Assert.True(Exemplos.ContainsKey(t), "tipo sem exemplo no teste: " + t);
            Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(Exemplos[t], out _));
        }
    }

    [Fact]
    public void Controle_UsaOsTiposDoTouchpadEDoTeclado()
    {
        var tipos = TiposUsados("controle").ToHashSet();
        foreach (var t in new[] { "mouse", "clique", "botao", "rolar", "tecla", "acordado" }) Assert.Contains(t, tipos);
        Assert.Contains("texto", TiposUsados("teclado-pc").ToHashSet());
    }

    [Fact]
    public void TelaPc_UsaOsTiposDoTouchpadEDoToqueDireto()
    {
        var tipos = TiposUsados("tela-pc").ToHashSet();
        foreach (var t in new[] { "mouse", "clique", "botao", "rolar", "mouseAbs" }) Assert.Contains(t, tipos);
    }

    [Fact]
    public void TeclasDoControle_SaoDaListaFixaDoPc()
    {
        var nomes = new HashSet<string>();
        // tecla('left') nos botões de Apresentação, Vídeo e Mídia
        foreach (Match m in Regex.Matches(Js("controle"), @"tecla\('(?<k>[a-z0-9+]+)'\)")) nomes.Add(m.Groups["k"].Value);
        // listas de teclado: ['Esc', 'esc', ...]
        foreach (Match m in Regex.Matches(Js("teclado-pc"), @"^\s*\[?\['[^']*', '(?<k>[a-z0-9+]+)'", RegexOptions.Multiline)) nomes.Add(m.Groups["k"].Value);
        foreach (Match m in Regex.Matches(Js("teclado-pc"), @"\['[^'\]]+', '(?<k>[a-z0-9+]+)'(?:, '[^']*')?(?:, \d)?\]")) nomes.Add(m.Groups["k"].Value);
        Assert.True(nomes.Count > 30, "poucas teclas achadas: " + nomes.Count);
        foreach (var n in nomes)
            Assert.True(InputRules.IsKnownKey(n), "tecla fora da lista fixa do PC: " + n);
    }

    [Fact]
    public void Teclado_ListaDeFuncaoTemF1AteF12()
    {
        Assert.Contains("Array.from({ length: 12 }, (_, i) => ['F' + (i + 1), 'f' + (i + 1)])", Js("teclado-pc"));
        for (var i = 1; i <= 12; i++) Assert.True(InputRules.IsKnownKey("f" + i));
    }

    [Fact]
    public void LetrasComModificador_SaoAceitasPeloPc()
    {
        var m = Regex.Match(Js("teclado-pc"), @"const LETRAS = \[(?<l>[^\]]*)\]");
        Assert.True(m.Success);
        var letras = Regex.Matches(m.Groups["l"].Value, @"'([a-z])'").Select(x => x.Groups[1].Value).ToList();
        Assert.Equal(10, letras.Count);
        foreach (var l in letras)
        {
            foreach (var mod in new[] { "ctrl", "alt", "shift", "win" })
            {
                var json = "{\"t\":\"tecla\",\"k\":\"" + l + "\",\"mods\":[\"" + mod + "\"]}";
                Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out _));
            }
            // sozinha a letra não é tecla da lista: o front manda como texto
            if (!InputRules.IsKnownKey(l)) Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand("{\"t\":\"tecla\",\"k\":\"" + l + "\"}", out _));
        }
    }

    [Fact]
    public void ModificadoresDoFront_SaoOsQuatroDoPc()
    {
        var m = Regex.Match(Js("teclado-pc"), @"const MODS = \[(?<l>.*?)\];", RegexOptions.Singleline);
        Assert.True(m.Success);
        var ids = Regex.Matches(m.Groups["l"].Value, @"\['(?<id>[a-z]+)',").Select(x => x.Groups["id"].Value).ToList();
        Assert.Equal(new[] { "ctrl", "alt", "shift", "win" }, ids);
        foreach (var id in ids) Assert.True(InputRules.IsModName(id));
    }

    [Fact]
    public void TelaPc_ConfiguracaoDoCanalTemOsCamposQueOPcLe()
    {
        var js = Js("tela-pc");
        Assert.Contains("ws({ mon, w: largura(), q: qual, cur: ponteiro, p: 1 })", js);
        // a mesma configuração, lida pelo PC
        var msg = ScreenStreamRules.Parse(System.Text.Encoding.UTF8.GetBytes("{\"mon\":0,\"w\":900,\"q\":\"normal\",\"cur\":1.5,\"p\":1}"));
        Assert.Equal((1.5, true), (msg.Cursor, msg.Pointer));
        // as opções do tamanho do ponteiro do front são as que o PC reconhece
        var opcoes = Regex.Match(js, @"const PONTEIROS = \[(?<l>[^\]]*)\]").Groups["l"].Value
            .Split(',').Select(x => double.Parse(x.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(new[] { 1.0, 1.5, 2.0, 3.0 }, opcoes);
        foreach (var o in opcoes) Assert.Equal(o, ScreenStreamRules.CursorScale(o));
        Assert.Contains("pref.ler('tela.ponteiro', 1.5)", js); // padrão 1,5x (P3)
    }

    [Fact]
    public void TelaPc_TextoDoPonteiroQueOFrontEspera()
    {
        // {"p":[x,y]}: o front lê m.p com dois números de 0 a 1
        using var doc = JsonDocument.Parse(ScreenStreamRules.PointerMessage(0.25, 0.75));
        var p = doc.RootElement.GetProperty("p");
        Assert.Equal(2, p.GetArrayLength());
        Assert.Equal(0.25, p[0].GetDouble());
        Assert.Equal(0.75, p[1].GetDouble());
        Assert.Contains("Array.isArray(m.p)", Js("tela-pc"));
    }

    [Fact]
    public void TelaPc_SoTransmiteNoTopoEComAPaginaVisivel()
    {
        var js = Js("tela-pc");
        Assert.Contains("topo && !destruida && document.visibilityState === 'visible'", js);
        Assert.Contains("aoTopo(sim)", js);
        Assert.Contains("destruir()", js);
    }

    [Fact]
    public void CssDasTelas_ExistemELigamNoIndex()
    {
        var app = Path.GetFullPath(Path.Combine(TelasDir(), "..", ".."));
        foreach (var css in new[] { "controle", "tela-pc" })
            Assert.True(new FileInfo(Path.Combine(app, "css", "telas", css + ".css")).Length > 200, css);
    }
}
