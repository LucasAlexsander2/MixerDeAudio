using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, parte 9c: timer de sono (D24): faixa de minutos, curva do volume, plano e o comando <c>sono</c>.</summary>
public class SleepTimerTests
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(30, true)]
    [InlineData(240, true)]
    [InlineData(241, false)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void Minutos_De5A240(int min, bool ok) => Assert.Equal(ok, SleepRules.IsValidMinutes(min));

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(0, false)]
    public void SegundosDeTeste_De5A60(int s, bool ok) => Assert.Equal(ok, SleepRules.IsValidTestSeconds(s));

    [Theory]
    [InlineData(300, 30)]
    [InlineData(30, 30)]
    [InlineData(10, 10)]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    public void Descida_DuraNoMaximo30s(int total, int fade) => Assert.Equal(fade, SleepRules.FadeFor(total));

    [Fact]
    public void Curva_ComecaNoOriginalETerminaEmZero()
    {
        Assert.Equal(0.8f, SleepRules.Volume(0.8f, 0, 30));
        Assert.Equal(0f, SleepRules.Volume(0.8f, 30, 30));
        Assert.Equal(0f, SleepRules.Volume(0.8f, 99, 30));
    }

    [Fact]
    public void Curva_ELinearEDecrescente()
    {
        Assert.Equal(0.4f, SleepRules.Volume(0.8f, 15, 30), 5);
        var anterior = float.MaxValue;
        for (var step = 0; step <= 30; step++)
        {
            var v = SleepRules.Volume(0.6f, step, 30);
            Assert.InRange(v, 0f, 0.6f);
            Assert.True(v <= anterior);
            anterior = v;
        }
    }

    [Fact]
    public void Curva_ValoresForaDaFaixa_SaoCortados()
    {
        Assert.Equal(1f, SleepRules.Volume(5f, 0, 10));
        Assert.Equal(0f, SleepRules.Volume(-1f, 0, 10));
        Assert.Equal(0f, SleepRules.Volume(0.5f, 3, 0));
        Assert.Equal(0.5f, SleepRules.Volume(0.5f, -2, 10));
    }

    [Fact]
    public void Curva_DescidaCurtaDe5Passos()
    {
        Assert.Equal(0.8f, SleepRules.Volume(1f, 1, 5), 5);
        Assert.Equal(0.2f, SleepRules.Volume(1f, 4, 5), 5);
        Assert.Equal(0f, SleepRules.Volume(1f, 5, 5));
    }

    [Fact]
    public void Plano_DescidaComecaTrintaSegundosAntes()
    {
        var due = new DateTimeOffset(2026, 11, 21, 23, 0, 0, TimeSpan.Zero);
        var plan = new SleepPlan(due, 300);
        Assert.Equal(30, plan.FadeSeconds);
        Assert.Equal(due.AddSeconds(-30), plan.FadeStartsAt);
    }

    [Fact]
    public void Plano_CurtoDesceDuranteTodoOTempo()
    {
        var due = new DateTimeOffset(2026, 11, 21, 23, 0, 0, TimeSpan.Zero);
        var plan = new SleepPlan(due, 8);
        Assert.Equal(8, plan.FadeSeconds);
        Assert.Equal(due.AddSeconds(-8), plan.FadeStartsAt);
    }

    // ---- Protocolo ----

    [Fact]
    public void Comando_Minutos()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"sono\",\"min\":30}", out var cmd));
        var c = Assert.IsType<SleepCommand>(cmd);
        Assert.Equal((30, 0), (c.Minutes, c.Seconds));
    }

    [Fact]
    public void Comando_ZeroCancela()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"sono\",\"min\":0}", out var cmd));
        var c = Assert.IsType<SleepCommand>(cmd);
        Assert.Equal((0, 0), (c.Minutes, c.Seconds));
    }

    [Fact]
    public void Comando_SegundosDeTeste()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"sono\",\"seg\":8}", out var cmd));
        var c = Assert.IsType<SleepCommand>(cmd);
        Assert.Equal((0, 8), (c.Minutes, c.Seconds));
    }

    [Theory]
    [InlineData("{\"t\":\"sono\"}")]
    [InlineData("{\"t\":\"sono\",\"min\":4}")]
    [InlineData("{\"t\":\"sono\",\"min\":241}")]
    [InlineData("{\"t\":\"sono\",\"min\":-1}")]
    [InlineData("{\"t\":\"sono\",\"min\":7.5}")]
    [InlineData("{\"t\":\"sono\",\"min\":\"30\"}")]
    [InlineData("{\"t\":\"sono\",\"seg\":4}")]
    [InlineData("{\"t\":\"sono\",\"seg\":61}")]
    [InlineData("{\"t\":\"sono\",\"seg\":0}")]
    public void Comando_ForaDaFaixa_Recusado(string json) =>
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out _));

    [Fact]
    public void Estado_TrazSonoSoComOGrupoEstadoLigado()
    {
        var com = new RemoteState(Array.Empty<RemoteOutput>(), true, 50, false, Array.Empty<RemoteApp>(), new[] { "estado" }, true,
            new RemotePcState(InfoOn: true, Sleep: new RemoteSleep(1_700_000_000_000, 245)));
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(com));
        var sono = doc.RootElement.GetProperty("pc").GetProperty("sono");
        Assert.Equal(1_700_000_000_000, sono.GetProperty("em").GetInt64());
        Assert.Equal(245, sono.GetProperty("rest").GetInt32());

        var sem = new RemoteState(Array.Empty<RemoteOutput>(), true, 50, false, Array.Empty<RemoteApp>(), new[] { "estado" }, true,
            new RemotePcState(InfoOn: true));
        using var doc2 = JsonDocument.Parse(RemoteProtocol.BuildState(sem));
        Assert.Equal(JsonValueKind.Null, doc2.RootElement.GetProperty("pc").GetProperty("sono").ValueKind);

        var off = new RemoteState(Array.Empty<RemoteOutput>(), true, 50, false, Array.Empty<RemoteApp>(), new[] { "energia" }, true,
            new RemotePcState(PowerOn: true));
        using var doc3 = JsonDocument.Parse(RemoteProtocol.BuildState(off));
        Assert.False(doc3.RootElement.GetProperty("pc").TryGetProperty("sono", out _));
    }

    // ---- RecordingPc ----

    [Fact]
    public async Task RecordingPc_SonoSoGravaNoLog()
    {
        var log = new List<string>();
        var pc = new RecordingPc(log.Add);
        pc.SleepVolume(0.5f);
        pc.SleepVolume(0f);
        var paused = await pc.SleepPauseAll();
        Assert.Equal(0, paused);
        Assert.Equal(new[] { "teste-pc: sono volume 0.50", "teste-pc: sono volume 0.00", "teste-pc: sono pausar" }, log);
    }

    [Fact]
    public void RecordingPc_JanelaSoGravaAAcaoSemOTitulo()
    {
        var log = new List<string>();
        var pc = new RecordingPc(log.Add);
        pc.ControlWindow(new WindowItem(1, "Titulo secreto", "notepad", null, false), WindowAction.Front);
        pc.ControlWindow(new WindowItem(1, "Titulo secreto", "notepad", null, false), WindowAction.Close);
        Assert.Equal(new[] { "teste-pc: janela frente", "teste-pc: janela fechar" }, log);
    }
}
