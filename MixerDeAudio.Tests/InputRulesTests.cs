using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9b, D13 a D16: teclas, texto, faixas do mouse e leitura dos comandos de entrada.</summary>
public class InputRulesTests
{
    private static IReadOnlyList<KeyEvent> Keys(string name)
    {
        Assert.True(InputRules.TryKey(name, out var events), name);
        return events;
    }

    [Fact]
    public void Enter_ApertaESolta()
    {
        var e = Keys("enter");
        Assert.Equal(new[] { new KeyEvent(false, 0x0D, false), new KeyEvent(false, 0x0D, true) }, e);
    }

    [Fact]
    public void AltTab_ModificadorApertadoAntesESoltoDepois()
    {
        var e = Keys("alt+tab");
        Assert.Equal(new[]
        {
            new KeyEvent(false, 0x12, false), new KeyEvent(false, 0x09, false),
            new KeyEvent(false, 0x09, true), new KeyEvent(false, 0x12, true),
        }, e);
    }

    [Fact]
    public void CtrlShiftEsc_ModificadoresNaOrdemESoltosAoContrario()
    {
        var e = Keys("ctrl+shift+esc");
        Assert.Equal(new[]
        {
            new KeyEvent(false, 0x11, false), new KeyEvent(false, 0x10, false),
            new KeyEvent(false, 0x1B, false), new KeyEvent(false, 0x1B, true),
            new KeyEvent(false, 0x10, true), new KeyEvent(false, 0x11, true),
        }, e);
    }

    [Fact]
    public void CtrlV_QuatroEventos()
    {
        var e = Keys("ctrl+v");
        Assert.Equal(4, e.Count);
        Assert.Equal((ushort)0x56, e[1].Code);
    }

    [Fact]
    public void WinD_WinEhTeclaEstendida()
    {
        var e = Keys("win+d");
        Assert.Equal(4, e.Count);
        Assert.Equal(new KeyEvent(false, 0x5B, false, true), e[0]);
        Assert.Equal(new KeyEvent(false, 0x5B, true, true), e[3]);
        Assert.Equal((ushort)0x44, e[1].Code);
    }

    [Theory]
    [InlineData("left", 0x25)]
    [InlineData("up", 0x26)]
    [InlineData("right", 0x27)]
    [InlineData("down", 0x28)]
    [InlineData("delete", 0x2E)]
    [InlineData("pgup", 0x21)]
    public void TeclasDeNavegacao_SaoEstendidas(string nome, int vk)
    {
        var e = Keys(nome);
        Assert.All(e, k => Assert.True(k.Extended));
        Assert.Equal((ushort)vk, e[0].Code);
    }

    [Theory]
    [InlineData("f1", 0x70)]
    [InlineData("f5", 0x74)]
    [InlineData("f12", 0x7B)]
    public void TeclasDeFuncao(string nome, int vk) => Assert.Equal((ushort)vk, Keys(nome)[0].Code);

    [Theory]
    [InlineData("play", 0xB3)]
    [InlineData("next", 0xB0)]
    [InlineData("prev", 0xB1)]
    [InlineData("volup", 0xAF)]
    [InlineData("voldown", 0xAE)]
    [InlineData("mute", 0xAD)]
    public void TeclasDeMidia(string nome, int vk)
    {
        var e = Keys(nome);
        Assert.Equal(2, e.Count);
        Assert.Equal((ushort)vk, e[0].Code);
    }

    [Fact]
    public void ShiftF5_ComecaDoSlideAtual() => Assert.Equal(new ushort[] { 0x10, 0x74, 0x74, 0x10 }, Keys("shift+f5").Select(k => k.Code));

    [Fact]
    public void NomeEmMaiusculas_Vale() => Assert.True(InputRules.TryKey("ALT+TAB", out _));

    [Theory]
    [InlineData("win+r")]
    [InlineData("ctrl+alt+delete")]
    [InlineData("alt+f5")]
    [InlineData("format")]
    [InlineData("")]
    [InlineData(null)]
    public void NomeForaDaLista_Ignorado(string? nome)
    {
        Assert.False(InputRules.TryKey(nome, out var e));
        Assert.Empty(e);
        Assert.False(InputRules.IsKnownKey(nome));
    }

    [Fact]
    public void TodaTecla_SoltaTudoOQueApertou()
    {
        foreach (var nome in InputRules.KeyNames)
        {
            Assert.True(InputRules.TryKey(nome, out var e));
            Assert.Equal(e.Count(k => !k.Up), e.Count(k => k.Up));
            Assert.True(e[^1].Up, nome);
            var apertadas = new Stack<ushort>();
            foreach (var k in e)
            {
                if (!k.Up) apertadas.Push(k.Code);
                else Assert.True(apertadas.Contains(k.Code), nome);
            }
        }
    }

    [Fact]
    public void Texto_AcentoSimboloEQuebraDeLinha()
    {
        var e = InputRules.TextEvents("olá ✓\nMX");
        Assert.Equal(16, e.Count); // 7 caracteres x 2 + Enter x 2
        Assert.True(e[0].IsUnicode);
        Assert.Equal((ushort)'o', e[0].Code);
        Assert.Equal((ushort)'á', e[4].Code);
        Assert.Equal((ushort)'✓', e[8].Code);
        Assert.Equal(new KeyEvent(false, 0x0D, false), e[10]);
        Assert.Equal(new KeyEvent(false, 0x0D, true), e[11]);
        Assert.Equal((ushort)'X', e[14].Code);
        Assert.True(e[15].Up);
    }

    [Fact]
    public void Texto_EmojiSaoDuasUnidades()
    {
        var e = InputRules.TextEvents("😀");
        Assert.Equal(4, e.Count);
        Assert.Equal((ushort)"😀"[0], e[0].Code);
        Assert.Equal((ushort)"😀"[1], e[2].Code);
        Assert.All(e, k => Assert.True(k.IsUnicode));
    }

    [Fact]
    public void Texto_CrLfEhUmEnterSo()
    {
        var e = InputRules.TextEvents("a\r\nb\rc");
        Assert.Equal(10, e.Count); // a, Enter, b, Enter, c
        Assert.Equal(2, e.Count(k => !k.IsUnicode && !k.Up));
    }

    [Fact]
    public void Texto_TabPassaEControleSai()
    {
        Assert.Equal(6, InputRules.TextEvents("a\tb").Count);
        Assert.Equal(4, InputRules.TextEvents("a\u0007\u001bb").Count);
        Assert.Empty(InputRules.TextEvents(""));
        Assert.Empty(InputRules.TextEvents(null));
    }

    [Fact]
    public void LimitarTexto_NaoPartePar()
    {
        Assert.Equal(1000, InputRules.LimitText(new string('a', 1500)).Length);
        var limite = new string('a', 999) + "😀" + "zzz";
        var cortado = InputRules.LimitText(limite);
        Assert.Equal(999, cortado.Length);
        Assert.Equal("abc", InputRules.LimitText("abc"));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(-5, -5)]
    [InlineData(5000, 1000)]
    [InlineData(-5000, -1000)]
    [InlineData(2.5, 3)]
    [InlineData(-2.5, -3)]
    [InlineData(double.NaN, 0)]
    public void Movimento_CortadoEm1000(double entrada, int esperado) => Assert.Equal(esperado, InputRules.ClampDelta(entrada));

    [Theory]
    [InlineData(-3, -3)]
    [InlineData(99, 20)]
    [InlineData(-99, -20)]
    [InlineData(0.4, 0)]
    public void Rolagem_CortadaEm20(double entrada, int esperado) => Assert.Equal(esperado, InputRules.ClampScroll(entrada));

    [Fact]
    public void Botoes_IdaEVolta()
    {
        foreach (var (id, b) in new[] { ("esq", MouseButton.Left), ("dir", MouseButton.Right), ("meio", MouseButton.Middle) })
        {
            Assert.True(InputRules.TryParseButton(id, out var lido));
            Assert.Equal(b, lido);
            Assert.Equal(id, InputRules.ButtonId(b));
        }
        Assert.False(InputRules.TryParseButton("x", out _));
        Assert.False(InputRules.TryParseButton(null, out _));
    }

    // ---- Protocolo ----

    private static (ParseStatus Status, RemoteCommand? Command) Parse(string json)
    {
        var s = RemoteProtocol.ParseCommand(json, out var c);
        return (s, c);
    }

    [Fact]
    public void Comando_MouseCortaNaFaixa()
    {
        var (s, c) = Parse("{\"t\":\"mouse\",\"dx\":10,\"dy\":-5}");
        Assert.Equal(ParseStatus.Ok, s);
        Assert.Equal(new MouseMoveCommand(10, -5), c);
        Assert.Equal(new MouseMoveCommand(1000, -1000), Parse("{\"t\":\"mouse\",\"dx\":99999,\"dy\":-99999}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"mouse\",\"dx\":1}").Status);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"mouse\",\"dx\":\"1\",\"dy\":2}").Status);
    }

    [Fact]
    public void Comando_Clique()
    {
        Assert.Equal(new ClickCommand(MouseButton.Left, false), Parse("{\"t\":\"clique\",\"b\":\"esq\"}").Command);
        Assert.Equal(new ClickCommand(MouseButton.Right, true), Parse("{\"t\":\"clique\",\"b\":\"dir\",\"duplo\":true}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"clique\",\"b\":\"outro\"}").Status);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"clique\"}").Status);
    }

    [Fact]
    public void Comando_BotaoParaArrastar()
    {
        Assert.Equal(new ButtonCommand(MouseButton.Left, true), Parse("{\"t\":\"botao\",\"b\":\"esq\",\"baixo\":true}").Command);
        Assert.Equal(new ButtonCommand(MouseButton.Left, false), Parse("{\"t\":\"botao\",\"b\":\"esq\",\"baixo\":false}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"botao\",\"b\":\"esq\"}").Status);
    }

    [Fact]
    public void Comando_RolarCortaEm20()
    {
        Assert.Equal(new ScrollCommand(0, -3), Parse("{\"t\":\"rolar\",\"dy\":-3}").Command);
        Assert.Equal(new ScrollCommand(20, -20), Parse("{\"t\":\"rolar\",\"dx\":500,\"dy\":-500}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"rolar\"}").Status);
    }

    [Fact]
    public void Comando_TextoAteMilCaracteres()
    {
        Assert.Equal(new TextCommand("olá ✓\nMX"), Parse("{\"t\":\"texto\",\"s\":\"olá ✓\\nMX\"}").Command);
        var longo = Parse("{\"t\":\"texto\",\"s\":\"" + new string('x', 3000) + "\"}");
        Assert.Equal(ParseStatus.Ok, longo.Status);
        Assert.Equal(1000, ((TextCommand)longo.Command!).Text.Length);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"texto\",\"s\":\"\"}").Status);
    }

    [Fact]
    public void Comando_TeclaSoDaListaEEmMinusculas()
    {
        Assert.Equal(new KeyCommand("alt+tab"), Parse("{\"t\":\"tecla\",\"k\":\"Alt+Tab\"}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"tecla\",\"k\":\"win+r\"}").Status);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"tecla\"}").Status);
    }

    [Fact]
    public void Comando_MouseAbsolutoCortaEm0a1()
    {
        Assert.Equal(new MouseAbsCommand(1, 0.25, 0.75), Parse("{\"t\":\"mouseAbs\",\"mon\":1,\"x\":0.25,\"y\":0.75}").Command);
        Assert.Equal(new MouseAbsCommand(0, 1, 0), Parse("{\"t\":\"mouseAbs\",\"mon\":-4,\"x\":7,\"y\":-2}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"mouseAbs\",\"mon\":0,\"x\":0.5}").Status);
    }

    [Fact]
    public void Comando_Acordado()
    {
        Assert.Equal(new AwakeCommand(true), Parse("{\"t\":\"acordado\",\"on\":true}").Command);
        Assert.Equal(new AwakeCommand(false), Parse("{\"t\":\"acordado\",\"on\":false}").Command);
        Assert.Equal(ParseStatus.InvalidFields, Parse("{\"t\":\"acordado\"}").Status);
    }

    [Fact]
    public void Grupo_ComandosDeEntradaSaoDoGrupoEntrada()
    {
        RemoteCommand[] todos =
        {
            new MouseMoveCommand(1, 1), new ClickCommand(MouseButton.Left, false), new ButtonCommand(MouseButton.Left, true),
            new ScrollCommand(0, 1), new TextCommand("a"), new KeyCommand("enter"), new MouseAbsCommand(0, 0, 0), new AwakeCommand(true),
        };
        Assert.All(todos, c => Assert.Equal(RemoteTools.Input, RemoteCommandGroups.GroupOf(c)));
        Assert.Equal(7, todos.Count(c => c is InputCommand)); // o acordado roda na thread da tela
    }

    [Fact]
    public void Estado_AcordadoEMonitores()
    {
        var pc = new RemotePcState(false, null, null, true, true, new[] { new RemoteScreen(0, 1920, 1080, true), new RemoteScreen(1, 1280, 1024, false) });
        var state = new RemoteState(Array.Empty<RemoteOutput>(), false, 0, false, Array.Empty<RemoteApp>(), new[] { "entrada", "tela" }, true, pc);
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(state));
        var p = doc.RootElement.GetProperty("pc");
        Assert.True(p.GetProperty("acordado").GetBoolean());
        var m = p.GetProperty("monitores");
        Assert.Equal(2, m.GetArrayLength());
        Assert.Equal(1920, m[0].GetProperty("w").GetInt32());
        Assert.Equal(1080, m[0].GetProperty("h").GetInt32());
        Assert.True(m[0].GetProperty("principal").GetBoolean());
        Assert.Equal(1, m[1].GetProperty("i").GetInt32());
        Assert.False(p.TryGetProperty("energia", out _));
    }

    [Fact]
    public void Estado_SemOsGruposNaoTemAcordadoNemMonitores()
    {
        var state = new RemoteState(Array.Empty<RemoteOutput>(), false, 0, false, Array.Empty<RemoteApp>(), Array.Empty<string>(), false,
            new RemotePcState(true));
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(state));
        var p = doc.RootElement.GetProperty("pc");
        Assert.False(p.TryGetProperty("acordado", out _));
        Assert.False(p.TryGetProperty("monitores", out _));
    }

    [Fact]
    public void RecordingPc_GravaEntradaComConteudoSoNoModoDeTeste()
    {
        var linhas = new List<string>();
        IPcActions pc = new RecordingPc(linhas.Add);
        pc.MouseMove(10, -5);
        pc.MouseClick(MouseButton.Left, true);
        pc.MouseButtonSet(MouseButton.Left, false);
        pc.MouseScroll(0, -3);
        pc.TypeText("olá ✓\nMX");
        pc.PressKey("alt+tab");
        pc.KeepAwake(true);
        Assert.Equal(new[]
        {
            "teste-pc: entrada mouse 10 -5",
            "teste-pc: entrada clique esq duplo",
            "teste-pc: entrada botao esq cima",
            "teste-pc: entrada rolar 0 -3",
            "teste-pc: entrada texto 16 eventos: olá ✓\\nMX",
            "teste-pc: entrada tecla alt+tab 4 eventos",
            "teste-pc: acordado on",
        }, linhas);
    }
}
