using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, parte 9c: lista de janelas (D21), alvo das ações (D22) e o protocolo de janelas.</summary>
public class WindowFilterTests
{
    private const int Own = 4242;

    private static WindowSnapshot Win(string title = "Bloco de Notas", int pid = 100, string process = "notepad", bool visible = true,
        bool owner = false, bool tool = false, bool cloaked = false, bool min = false, long id = 1) =>
        new(id, title, pid, process, @"C:\x\" + process + ".exe", visible, owner, tool, cloaked, min);

    // ---- Filtro (D21) ----

    [Fact]
    public void JanelaNormal_Entra() => Assert.True(WindowFilter.Include(Win(), Own, false));

    [Fact]
    public void Invisivel_Fica() => Assert.False(WindowFilter.Include(Win(visible: false), Own, false));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SemTitulo_Fica(string title) => Assert.False(WindowFilter.Include(Win(title: title), Own, false));

    [Fact]
    public void ComDono_Fica() => Assert.False(WindowFilter.Include(Win(owner: true), Own, false));

    [Fact]
    public void JanelaDeFerramenta_Fica() => Assert.False(WindowFilter.Include(Win(tool: true), Own, false));

    [Fact]
    public void Cloaked_Fica() => Assert.False(WindowFilter.Include(Win(cloaked: true), Own, false));

    [Fact]
    public void ProprioApp_Fica() => Assert.False(WindowFilter.Include(Win(pid: Own), Own, false));

    [Fact]
    public void Minimizada_Entra() => Assert.True(WindowFilter.Include(Win(min: true), Own, false));

    // ---- Exceção de teste ----

    [Fact]
    public void Teste_FerramentaMxTesteEntraSoNoModoDeTeste()
    {
        var w = Win(title: "MX-teste janela", tool: true, process: "janela-teste");
        Assert.True(WindowFilter.Include(w, Own, true));
        Assert.False(WindowFilter.Include(w, Own, false));
    }

    [Fact]
    public void Teste_FerramentaQueNaoEMxTesteContinuaDeFora() =>
        Assert.False(WindowFilter.Include(Win(title: "Outra", tool: true), Own, true));

    [Fact]
    public void Teste_ExcecaoNaoLiberaOutrasRegras()
    {
        Assert.False(WindowFilter.Include(Win(title: "MX-teste x", tool: true, visible: false), Own, true));
        Assert.False(WindowFilter.Include(Win(title: "MX-teste x", tool: true, owner: true), Own, true));
        Assert.False(WindowFilter.Include(Win(title: "MX-teste x", tool: true, cloaked: true), Own, true));
        Assert.False(WindowFilter.Include(Win(title: "MX-teste x", tool: true, pid: Own), Own, true));
    }

    [Fact]
    public void Prefixo_DiferencaMaiusculas() => Assert.False(WindowFilter.IsTestTitle("mx-teste janela"));

    // ---- Título e nome do processo ----

    [Fact]
    public void Titulo_CortaEm120()
    {
        var clipped = WindowFilter.ClipTitle(new string('a', 300));
        Assert.Equal(120, clipped.Length);
        Assert.Equal("curto", WindowFilter.ClipTitle("curto"));
        Assert.Equal("", WindowFilter.ClipTitle(null));
    }

    [Fact]
    public void Titulo_NaoPartePar()
    {
        var s = new string('a', 119) + "\U0001F600" + "zzz"; // o emoji ocupa os caracteres 119 e 120
        var clipped = WindowFilter.ClipTitle(s);
        Assert.Equal(119, clipped.Length);
        Assert.False(char.IsHighSurrogate(clipped[^1]));
    }

    [Theory]
    [InlineData(@"C:\Windows\notepad.exe", "notepad")]
    [InlineData(@"C:\a b\Meu App.v2.exe", "Meu App.v2")]
    [InlineData("calc.exe", "calc")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NomeDoProcesso(string? path, string expected) => Assert.Equal(expected, WindowFilter.ProcessName(path));

    // ---- Alvo da ação (D22) ----

    private static WindowItem Item(long id, string process = "notepad", string title = "t") => new(id, title, process, null, false);

    [Fact]
    public void Alvo_NaListaEComMesmoProcesso_Resolve()
    {
        var shown = new[] { Item(10), Item(20, "chrome") };
        var fresh = new[] { Item(20, "chrome", "novo titulo"), Item(10) };
        var target = WindowFilter.Resolve(shown, fresh, 20);
        Assert.NotNull(target);
        Assert.Equal("novo titulo", target!.Title);
    }

    [Fact]
    public void Alvo_IdInventado_Ignorado()
    {
        var list = new[] { Item(10) };
        Assert.Null(WindowFilter.Resolve(list, list, 999));
        Assert.Null(WindowFilter.Resolve(list, list, 0));
    }

    [Fact]
    public void Alvo_FechouDesdeAUltimaLista_Ignorado() =>
        Assert.Null(WindowFilter.Resolve(new[] { Item(10) }, Array.Empty<WindowItem>(), 10));

    [Fact]
    public void Alvo_NuncaMostradoAoCelular_Ignorado() =>
        Assert.Null(WindowFilter.Resolve(Array.Empty<WindowItem>(), new[] { Item(10) }, 10));

    [Fact]
    public void Alvo_MesmoIdEOutroProcesso_Ignorado() =>
        Assert.Null(WindowFilter.Resolve(new[] { Item(10, "notepad") }, new[] { Item(10, "chrome") }, 10));

    [Fact]
    public void Alvo_ProcessoComparaSemDiferenciarMaiusculas() =>
        Assert.NotNull(WindowFilter.Resolve(new[] { Item(10, "Notepad") }, new[] { Item(10, "notepad") }, 10));

    // ---- Ações ----

    [Theory]
    [InlineData("frente", WindowAction.Front)]
    [InlineData("minimizar", WindowAction.Minimize)]
    [InlineData("fechar", WindowAction.Close)]
    public void Acao_Parse(string id, WindowAction expected)
    {
        Assert.True(WindowFilter.TryParseAction(id, out var a));
        Assert.Equal(expected, a);
        Assert.Equal(id, WindowFilter.ActionId(a));
    }

    [Theory]
    [InlineData("matar")]
    [InlineData("kill")]
    [InlineData("")]
    [InlineData(null)]
    public void Acao_Desconhecida_Recusada(string? id) => Assert.False(WindowFilter.TryParseAction(id, out _));

    // ---- Protocolo ----

    private static RemoteCommand? Parse(string json)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var cmd));
        return cmd;
    }

    [Fact]
    public void Comando_Janelas() => Assert.IsType<WindowsCommand>(Parse("{\"t\":\"janelas\"}"));

    [Fact]
    public void Comando_Janela_Valido()
    {
        var c = Assert.IsType<WindowActionCommand>(Parse("{\"t\":\"janela\",\"id\":197382,\"acao\":\"fechar\"}"));
        Assert.Equal(197382, c.Id);
        Assert.Equal(WindowAction.Close, c.Action);
    }

    [Theory]
    [InlineData("{\"t\":\"janela\",\"id\":1,\"acao\":\"matar\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":1}")]
    [InlineData("{\"t\":\"janela\",\"acao\":\"frente\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":1.5,\"acao\":\"frente\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":\"1\",\"acao\":\"frente\"}")]
    [InlineData("{\"t\":\"janela\",\"id\":1e30,\"acao\":\"frente\"}")]
    public void Comando_Janela_Invalido(string json) =>
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out _));

    [Fact]
    public void Comando_Assinar()
    {
        var on = Assert.IsType<SubscribeCommand>(Parse("{\"t\":\"assinar\",\"g\":\"janelas\",\"on\":true}"));
        Assert.Equal(("janelas", true), (on.Group, on.On));
        var off = Assert.IsType<SubscribeCommand>(Parse("{\"t\":\"assinar\",\"g\":\"janelas\",\"on\":false}"));
        Assert.False(off.On);
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand("{\"t\":\"assinar\",\"g\":\"tudo\",\"on\":true}", out _));
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand("{\"t\":\"assinar\",\"g\":\"janelas\"}", out _));
    }

    [Fact]
    public void Grupos_DosComandosNovos()
    {
        Assert.Equal(RemoteTools.Windows, RemoteCommandGroups.GroupOf(new WindowsCommand()));
        Assert.Equal(RemoteTools.Windows, RemoteCommandGroups.GroupOf(new WindowActionCommand(1, WindowAction.Front)));
        Assert.Equal(RemoteTools.Shortcuts, RemoteCommandGroups.GroupOf(new ShortcutCommand("x")));
        Assert.Equal(RemoteTools.Info, RemoteCommandGroups.GroupOf(new SleepCommand(5)));
        Assert.Equal(RemoteTools.Windows, RemoteCommandGroups.GroupOf(new SubscribeCommand("janelas", true)));
        Assert.Equal(RemoteTools.Info, RemoteCommandGroups.GroupOf(new SubscribeCommand("info", true)));
    }

    [Fact]
    public void Mensagem_Janelas_SemCaminhoDoExe()
    {
        var items = new[]
        {
            new WindowItem(197382, "MX-teste janela", "janela-teste", @"C:\x\janela-teste.exe", false),
            new WindowItem(5, "Sem processo", "", null, true),
        };
        var json = RemoteProtocol.BuildWindows(items);
        Assert.DoesNotContain("janela-teste.exe", json);
        Assert.DoesNotContain(@"C:\", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("janelas", doc.RootElement.GetProperty("t").GetString());
        var lista = doc.RootElement.GetProperty("lista");
        Assert.Equal(2, lista.GetArrayLength());
        var a = lista[0];
        Assert.Equal(197382, a.GetProperty("id").GetInt64());
        Assert.Equal("MX-teste janela", a.GetProperty("titulo").GetString());
        Assert.Equal("janela-teste", a.GetProperty("proc").GetString());
        Assert.Equal(RemoteProtocol.WindowIconId(@"C:\x\janela-teste.exe"), a.GetProperty("icone").GetString());
        Assert.True(RemoteProtocol.IsIconId(a.GetProperty("icone").GetString()));
        Assert.False(a.GetProperty("min").GetBoolean());
        Assert.Equal("", lista[1].GetProperty("icone").GetString());
        Assert.True(lista[1].GetProperty("min").GetBoolean());
    }

    [Fact]
    public void Mensagem_Janelas_TituloLongoCortado()
    {
        var json = RemoteProtocol.BuildWindows(new[] { new WindowItem(1, new string('x', 500), "p", null, false) });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(120, doc.RootElement.GetProperty("lista")[0].GetProperty("titulo").GetString()!.Length);
    }

    [Fact]
    public void IconeDeJanela_NaoColideComIconeDeApp() =>
        Assert.NotEqual(RemoteProtocol.IconId(@"c:\x\a.exe"), RemoteProtocol.WindowIconId(@"C:\x\a.exe"));

    [Fact]
    public void IconeDeJanela_IgnoraMaiusculasDoCaminho() =>
        Assert.Equal(RemoteProtocol.WindowIconId(@"C:\X\A.exe"), RemoteProtocol.WindowIconId(@"c:\x\a.EXE"));
}
