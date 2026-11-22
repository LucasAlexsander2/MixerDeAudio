using System.Text;
using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;
using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, parte 9a: grupos de ferramentas, um celular por vez, protocolo novo, --teste-pc e o RecordingPc.</summary>
public class RemoteToolsTests
{
    // ---- SingleSessionPolicy (D3) ----

    [Fact]
    public void SemNinguem_Aceita() => Assert.Equal(SessionDecision.Accept, SingleSessionPolicy.Decide(null, "aaa"));

    [Fact]
    public void MesmaChave_Substitui() => Assert.Equal(SessionDecision.Replace, SingleSessionPolicy.Decide("aaa", "aaa"));

    [Fact]
    public void OutraChave_Recusa() => Assert.Equal(SessionDecision.Refuse, SingleSessionPolicy.Decide("aaa", "bbb"));

    [Fact]
    public void ChaveAtualVazia_NaoEhNinguem() => Assert.Equal(SessionDecision.Refuse, SingleSessionPolicy.Decide("", "bbb"));

    [Fact]
    public void Chaves_ComparamMaiusculasEMinusculas() => Assert.Equal(SessionDecision.Refuse, SingleSessionPolicy.Decide("AAA", "aaa"));

    // ---- Grupos (D1) ----

    [Fact]
    public void Grupos_SaoOitoComIdsFixos() =>
        Assert.Equal(new[] { "energia", "monitores", "entrada", "tela", "janelas", "atalhos", "transferencia", "estado" }, RemoteTools.All);

    [Fact]
    public void Normalize_NuloEVazio_TudoDesligado()
    {
        Assert.Empty(RemoteTools.Normalize(null));
        Assert.Empty(RemoteTools.Normalize(new List<string>()));
    }

    [Fact]
    public void Normalize_TiraDesconhecidosERepetidos_EOrdena()
    {
        var r = RemoteTools.Normalize(new[] { "tela", "xyz", "energia", "tela", "", "ENERGIA" });
        Assert.Equal(new[] { "energia", "tela" }, r);
    }

    [Fact]
    public void With_LigaEDesliga()
    {
        var l = RemoteTools.With(new List<string>(), "monitores", true);
        Assert.Equal(new[] { "monitores" }, l);
        l = RemoteTools.With(l, "energia", true);
        Assert.Equal(new[] { "energia", "monitores" }, l);
        l = RemoteTools.With(l, "energia", true); // sem repetir
        Assert.Equal(2, l.Count);
        l = RemoteTools.With(l, "monitores", false);
        Assert.Equal(new[] { "energia" }, l);
        Assert.True(RemoteTools.IsEnabled(l, "energia"));
        Assert.False(RemoteTools.IsEnabled(l, "monitores"));
    }

    [Fact]
    public void Configuracao_Padrao_TemTudoDesligado()
    {
        Assert.Empty(AppSettings.Default.FerramentasCelular);
        Assert.Empty(AppSettings.Parse("{\"Versao\":1}").FerramentasCelular);
    }

    [Fact]
    public void Configuracao_ArquivoAntigoSemOCampo_Continua()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"Tema\":\"escuro\",\"CelularLigado\":true}");
        Assert.Equal("escuro", s.Tema);
        Assert.True(s.CelularLigado);
        Assert.Empty(s.FerramentasCelular);
    }

    [Fact]
    public void Configuracao_IdDesconhecidoERepetido_SaemNaLeitura()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"FerramentasCelular\":[\"tela\",\"foo\",\"tela\",\"energia\"]}");
        Assert.Equal(new[] { "energia", "tela" }, s.FerramentasCelular);
        Assert.Equal(1, s.Versao);
    }

    [Fact]
    public void Configuracao_IdaEVolta()
    {
        var s = AppSettings.Default with { FerramentasCelular = new List<string> { "monitores", "energia" } };
        var again = AppSettings.Parse(s.Serialize());
        Assert.Equal(new[] { "energia", "monitores" }, again.FerramentasCelular);
    }

    // ---- Protocolo (D6) ----

    private static RemoteState Vazio(IReadOnlyCollection<string>? tools = null, bool teste = false, RemotePcState? pc = null) =>
        new(Array.Empty<RemoteOutput>(), false, 0, false, Array.Empty<RemoteApp>(), tools, teste, pc);

    [Fact]
    public void Estado_FerrTodoFalsoPorPadrao_ESemPc()
    {
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Vazio()));
        var ferr = doc.RootElement.GetProperty("ferr");
        foreach (var id in RemoteTools.All) Assert.False(ferr.GetProperty(id).GetBoolean());
        Assert.False(doc.RootElement.GetProperty("teste").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("pc", out _));
    }

    [Fact]
    public void Estado_FerrETesteRefletemAConfiguracao()
    {
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Vazio(new[] { "energia", "tela" }, teste: true)));
        var ferr = doc.RootElement.GetProperty("ferr");
        Assert.True(ferr.GetProperty("energia").GetBoolean());
        Assert.True(ferr.GetProperty("tela").GetBoolean());
        Assert.False(ferr.GetProperty("monitores").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("teste").GetBoolean());
    }

    [Fact]
    public void Estado_Pc_EnergiaSemTimerEhNulo_ComTimerTemAcaoEHorario()
    {
        using (var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Vazio(new[] { "energia" }, pc: new RemotePcState(true)))))
        {
            var pc = doc.RootElement.GetProperty("pc");
            Assert.Equal(JsonValueKind.Null, pc.GetProperty("energia").ValueKind);
            Assert.False(pc.TryGetProperty("projecao", out _));
        }
        var com = new RemotePcState(true, new RemotePower("suspender", 1_800_000_000_000, 60));
        using (var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Vazio(new[] { "energia" }, pc: com))))
        {
            var e = doc.RootElement.GetProperty("pc").GetProperty("energia");
            Assert.Equal("suspender", e.GetProperty("acao").GetString());
            Assert.Equal(1_800_000_000_000, e.GetProperty("em").GetInt64());
            Assert.Equal(60, e.GetProperty("rest").GetInt32());
        }
    }

    [Fact]
    public void Estado_Pc_Projecao()
    {
        var pc = new RemotePcState(false, null, new RemoteProjection("estender", 2));
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Vazio(new[] { "monitores" }, pc: pc)));
        var p = doc.RootElement.GetProperty("pc");
        Assert.False(p.TryGetProperty("energia", out _));
        Assert.Equal("estender", p.GetProperty("projecao").GetProperty("modo").GetString());
        Assert.Equal(2, p.GetProperty("projecao").GetProperty("telas").GetInt32());
        var sem = new RemotePcState(false, null, new RemoteProjection(null, 0));
        using var doc2 = JsonDocument.Parse(RemoteProtocol.BuildState(Vazio(new[] { "monitores" }, pc: sem)));
        Assert.Equal(JsonValueKind.Null, doc2.RootElement.GetProperty("pc").GetProperty("projecao").GetProperty("modo").ValueKind);
    }

    [Fact]
    public void MensagensAvulsas()
    {
        using (var d = JsonDocument.Parse(RemoteProtocol.BuildBusy("Android")))
        {
            Assert.Equal("ocupado", d.RootElement.GetProperty("t").GetString());
            Assert.Equal("Android", d.RootElement.GetProperty("por").GetString());
        }
        using (var d = JsonDocument.Parse(RemoteProtocol.BuildDisconnected()))
            Assert.Equal("desconectado", d.RootElement.GetProperty("t").GetString());
        using (var d = JsonDocument.Parse(RemoteProtocol.BuildNotice("O Windows recusou a troca de projeção")))
        {
            Assert.Equal("aviso", d.RootElement.GetProperty("t").GetString());
            Assert.Equal("O Windows recusou a troca de projeção", d.RootElement.GetProperty("msg").GetString());
        }
    }

    [Theory]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":0}", PowerAction.Shutdown, 0)]
    [InlineData("{\"t\":\"energia\",\"acao\":\"reiniciar\",\"seg\":60}", PowerAction.Restart, 60)]
    [InlineData("{\"t\":\"energia\",\"acao\":\"suspender\",\"seg\":5}", PowerAction.Suspend, 5)]
    [InlineData("{\"t\":\"energia\",\"acao\":\"bloquear\",\"seg\":86400}", PowerAction.Lock, 86400)]
    public void Comando_Energia(string json, PowerAction acao, int seg)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        var p = Assert.IsType<PowerCommand>(c);
        Assert.Equal(acao, p.Action);
        Assert.Equal(seg, p.Seconds);
    }

    [Theory]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":1}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":4}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":86401}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":-1}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":1.5}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\"}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"hibernar\",\"seg\":0}")]
    [InlineData("{\"t\":\"energia\",\"seg\":0}")]
    [InlineData("{\"t\":\"energia\",\"acao\":\"desligar\",\"seg\":\"0\"}")]
    public void Comando_EnergiaInvalido(string json)
    {
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Null(c);
    }

    [Fact]
    public void Comandos_SemCampos()
    {
        Assert.IsType<PowerCancelCommand>(Parse("{\"t\":\"energiaCancelar\"}"));
        Assert.IsType<RefreshCommand>(Parse("{\"t\":\"atualizar\"}"));
        Assert.IsType<DisconnectCommand>(Parse("{\"t\":\"desconectar\"}"));
    }

    [Theory]
    [InlineData("principal", ProjectionMode.Primary)]
    [InlineData("segunda", ProjectionMode.Second)]
    [InlineData("estender", ProjectionMode.Extend)]
    [InlineData("duplicar", ProjectionMode.Duplicate)]
    public void Comando_Projecao(string modo, ProjectionMode esperado)
    {
        var p = Assert.IsType<ProjectionCommand>(Parse("{\"t\":\"projecao\",\"modo\":\"" + modo + "\"}"));
        Assert.Equal(esperado, p.Mode);
    }

    [Fact]
    public void Comando_ProjecaoEModoInvalidos()
    {
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand("{\"t\":\"projecao\",\"modo\":\"girar\"}", out _));
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand("{\"t\":\"projecao\"}", out _));
    }

    [Fact]
    public void Comando_Telas()
    {
        Assert.True(Assert.IsType<ScreensCommand>(Parse("{\"t\":\"telas\",\"acao\":\"apagar\"}")).Off);
        Assert.False(Assert.IsType<ScreensCommand>(Parse("{\"t\":\"telas\",\"acao\":\"acordar\"}")).Off);
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand("{\"t\":\"telas\",\"acao\":\"explodir\"}", out _));
    }

    [Fact]
    public void Grupos_DosComandos()
    {
        Assert.Equal("energia", RemoteCommandGroups.GroupOf(new PowerCommand(PowerAction.Lock, 0)));
        Assert.Equal("energia", RemoteCommandGroups.GroupOf(new PowerCancelCommand()));
        Assert.Equal("monitores", RemoteCommandGroups.GroupOf(new ProjectionCommand(ProjectionMode.Extend)));
        Assert.Equal("monitores", RemoteCommandGroups.GroupOf(new ScreensCommand(true)));
        Assert.Null(RemoteCommandGroups.GroupOf(new RefreshCommand()));
        Assert.Null(RemoteCommandGroups.GroupOf(new DisconnectCommand()));
        Assert.Null(RemoteCommandGroups.GroupOf(new MasterVolumeCommand(10)));
    }

    [Fact]
    public void MensagemDe16Kb_EhAceita_ESoAcimaEhGrande()
    {
        Assert.Equal(16 * 1024, RemoteProtocol.MaxMessageBytes);
        var prefixo = "{\"t\":\"volume\",\"app\":\"";
        var sufixo = "\",\"v\":1}";
        var json = prefixo + new string('a', RemoteProtocol.MaxMessageBytes - prefixo.Length - sufixo.Length) + sufixo;
        Assert.Equal(16 * 1024, Encoding.UTF8.GetByteCount(json));
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out _));
        Assert.Equal(ParseStatus.TooLarge, RemoteProtocol.ParseCommand(json + " ", out _));
    }

    private static RemoteCommand Parse(string json)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        return c!;
    }

    // ---- --teste-pc (D7) ----

    [Fact]
    public void LinhaDeComando_TestePc()
    {
        Assert.True(CommandLine.Parse(["--teste-pc"]).TestPc);
        Assert.True(CommandLine.Parse(["--teste-oculto", "--celular-local", "--teste-pc"]).TestPc);
        Assert.False(CommandLine.Parse(["--abrir"]).TestPc);
        Assert.False(CommandLine.Parse([]).TestPc);
        Assert.True(CommandLine.ParseLine("\"C:\\a b\\x.exe\" --teste-pc --sair").TestPc);
        Assert.Equal(AppCommand.Exit, CommandLine.Parse(["--teste-pc", "--sair"]).Command);
    }

    [Fact]
    public void ModoDeTeste_NuncaCriaORealPc()
    {
        var real = 0;
        var gravados = new List<string>();
        var pc = PcActionsFactory.Create(true, () => { real++; return new RecordingPc(_ => { }); }, () => new RecordingPc(gravados.Add));
        pc.Power(PowerAction.Shutdown);
        Assert.Equal(0, real);
        Assert.Equal(new[] { "teste-pc: energia desligar" }, gravados);
    }

    [Fact]
    public void SemModoDeTeste_UsaOReal()
    {
        var gravador = 0;
        PcActionsFactory.Create(false, () => new RecordingPc(_ => { }), () => { gravador++; return new RecordingPc(_ => { }); });
        Assert.Equal(0, gravador);
    }

    [Fact]
    public void RecordingPc_SoGravaNoLog()
    {
        var log = new List<string>();
        var pc = new RecordingPc(log.Add);
        pc.Power(PowerAction.Restart);
        pc.Power(PowerAction.Suspend);
        pc.Power(PowerAction.Lock);
        Assert.True(pc.SetProjection(ProjectionMode.Extend));
        pc.ScreensOff();
        pc.ScreensOn();
        Assert.Equal(new[]
        {
            "teste-pc: energia reiniciar", "teste-pc: energia suspender", "teste-pc: energia bloquear",
            "teste-pc: projecao estender", "teste-pc: telas apagar", "teste-pc: telas acordar",
        }, log);
    }
}
