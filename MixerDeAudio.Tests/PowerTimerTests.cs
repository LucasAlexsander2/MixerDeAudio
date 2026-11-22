using MixerDeAudio.Pc;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, D8: máquina de estados do timer de energia, com relógio falso.</summary>
public class PowerTimerTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        public void Add(int seconds) => Now = Now.AddSeconds(seconds);
    }

    private static (PowerTimer Timer, Clock Clock) Make()
    {
        var c = new Clock();
        return (new PowerTimer(() => c.Now), c);
    }

    [Fact]
    public void SemPlano_NadaAcontece()
    {
        var (t, _) = Make();
        Assert.Null(t.Plan);
        Assert.Null(t.DelayToNext());
        Assert.Equal(PowerEventKind.None, t.Poll().Kind);
        Assert.False(t.Cancel());
    }

    [Fact]
    public void Programar_GuardaOPlano()
    {
        var (t, c) = Make();
        var plan = t.Schedule(PowerAction.Shutdown, 300);
        Assert.Equal(PowerAction.Shutdown, plan.Action);
        Assert.Equal(c.Now.AddSeconds(300), plan.DueAt);
        Assert.Same(plan, t.Plan);
    }

    [Fact]
    public void Dispara_NaHora_EConsomeOPlano()
    {
        var (t, c) = Make();
        t.Schedule(PowerAction.Lock, 5);
        c.Add(4);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind);
        Assert.NotNull(t.Plan);
        c.Add(1);
        var ev = t.Poll();
        Assert.Equal(PowerEventKind.Execute, ev.Kind);
        Assert.Equal(PowerAction.Lock, ev.Action);
        Assert.Null(t.Plan);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind); // não repete
    }

    [Fact]
    public void Substituir_TrocaOPlano()
    {
        var (t, c) = Make();
        t.Schedule(PowerAction.Shutdown, 60);
        c.Add(10);
        var novo = t.Schedule(PowerAction.Restart, 120);
        Assert.Equal(PowerAction.Restart, t.Plan!.Action);
        Assert.Equal(c.Now.AddSeconds(120), novo.DueAt);
        c.Add(59);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind); // o antigo (aos 60 s) não dispara
    }

    [Fact]
    public void Cancelar_LimpaOPlano_ENaoExecuta()
    {
        var (t, c) = Make();
        t.Schedule(PowerAction.Suspend, 60);
        Assert.True(t.Cancel());
        Assert.Null(t.Plan);
        Assert.Null(t.DelayToNext());
        c.Add(120);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind);
    }

    [Fact]
    public void Aviso_SoAcimaDeUmMinuto()
    {
        var (t, _) = Make();
        Assert.Null(t.Schedule(PowerAction.Shutdown, 60).WarnAt);
        Assert.Null(t.Schedule(PowerAction.Shutdown, 5).WarnAt);
        Assert.NotNull(t.Schedule(PowerAction.Shutdown, 61).WarnAt);
        Assert.NotNull(t.Schedule(PowerAction.Shutdown, 3600).WarnAt);
    }

    [Fact]
    public void Aviso_UmMinutoAntes_SaiUmaVez_EDepoisExecuta()
    {
        var (t, c) = Make();
        t.Schedule(PowerAction.Restart, 300);
        Assert.Equal(TimeSpan.FromSeconds(240), t.DelayToNext());
        c.Add(239);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind);
        c.Add(1);
        var warn = t.Poll();
        Assert.Equal(PowerEventKind.Warn, warn.Kind);
        Assert.Equal(PowerAction.Restart, warn.Action);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind); // só uma vez
        Assert.Equal(TimeSpan.FromSeconds(60), t.DelayToNext());
        c.Add(60);
        Assert.Equal(PowerEventKind.Execute, t.Poll().Kind);
    }

    [Fact]
    public void RelogioPulou_ExecutaDireto()
    {
        var (t, c) = Make();
        t.Schedule(PowerAction.Shutdown, 600);
        c.Add(3600);
        Assert.Equal(PowerEventKind.Execute, t.Poll().Kind);
    }

    [Fact]
    public void DelayNuncaEhNegativo()
    {
        var (t, c) = Make();
        t.Schedule(PowerAction.Shutdown, 10);
        c.Add(100);
        Assert.Equal(TimeSpan.Zero, t.DelayToNext());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(86401)]
    public void Programar_ForaDaFaixa_Recusa(int seg)
    {
        var (t, _) = Make();
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Schedule(PowerAction.Shutdown, seg));
        Assert.Null(t.Plan);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(86400, true)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(86401, false)]
    [InlineData(-1, false)]
    public void FaixaDeSegundos(int seg, bool valido) => Assert.Equal(valido, PowerRules.IsValidSeconds(seg));

    [Fact]
    public void Ids_IdaEVolta()
    {
        foreach (var a in Enum.GetValues<PowerAction>())
        {
            Assert.True(PowerRules.TryParse(PowerRules.Id(a), out var volta));
            Assert.Equal(a, volta);
        }
        Assert.False(PowerRules.TryParse("hibernar", out _));
        Assert.False(PowerRules.TryParse(null, out _));
    }

    [Theory]
    [InlineData(30, "30 s")]
    [InlineData(60, "1 min")]
    [InlineData(300, "5 min")]
    [InlineData(3600, "1 h")]
    [InlineData(7500, "2 h 5 min")]
    public void Duracao(int seg, string texto) => Assert.Equal(texto, PowerRules.Duration(seg));

    [Fact]
    public void TextosDaBandeja()
    {
        Assert.Equal("O PC vai desligar em 5 min.", PowerRules.BalloonText(PowerAction.Shutdown, 300));
        Assert.Equal("Cancelar desligamento programado", PowerRules.CancelText(PowerAction.Shutdown));
        Assert.Equal("Cancelar suspensão programada", PowerRules.CancelText(PowerAction.Suspend));
    }
}
