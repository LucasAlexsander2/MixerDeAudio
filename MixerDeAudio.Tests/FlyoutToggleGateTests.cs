using MixerDeAudio.Flyout;

namespace MixerDeAudio.Tests;

public class FlyoutToggleGateTests
{
    private DateTimeOffset _agora = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private FlyoutToggleGate Criar() => new(() => _agora);

    [Fact]
    public void SemFechamentoRecente_Permite() => Assert.True(Criar().CanOpen());

    [Fact]
    public void DuzentosMsDepoisDePerdaDeFoco_Ignora()
    {
        var gate = Criar();
        gate.NotifyClosed(lostFocus: true);
        _agora += TimeSpan.FromMilliseconds(200);
        Assert.False(gate.CanOpen());
    }

    [Fact]
    public void SeiscentosMsDepoisDePerdaDeFoco_Permite()
    {
        var gate = Criar();
        gate.NotifyClosed(lostFocus: true);
        _agora += TimeSpan.FromMilliseconds(600);
        Assert.True(gate.CanOpen());
    }

    [Fact]
    public void LogoDepoisDeFecharPorEsc_Permite()
    {
        var gate = Criar();
        gate.NotifyClosed(lostFocus: false);
        Assert.True(gate.CanOpen());
    }

    [Fact]
    public void DoisFechamentosSeguidos_ValeOUltimo()
    {
        var gate = Criar();
        gate.NotifyClosed(lostFocus: true);
        _agora += TimeSpan.FromMilliseconds(100);
        gate.NotifyClosed(lostFocus: false);
        Assert.True(gate.CanOpen());

        gate.NotifyClosed(lostFocus: false);
        _agora += TimeSpan.FromMilliseconds(100);
        gate.NotifyClosed(lostFocus: true);
        Assert.False(gate.CanOpen());
    }
}
