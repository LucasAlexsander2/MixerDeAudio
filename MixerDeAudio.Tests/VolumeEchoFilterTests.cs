using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class VolumeEchoFilterTests
{
    private DateTime _agora = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    private VolumeEchoFilter Novo() => new(() => _agora);

    [Fact]
    public void EcoExatoEIgnorado()
    {
        var f = Novo();
        f.Register("g", 0.5f);
        Assert.True(f.IsEcho("g", 0.5f));
    }

    [Fact]
    public void EcoAteMeioPorCentoEIgnorado()
    {
        var f = Novo();
        f.Register("g", 0.5f);
        Assert.True(f.IsEcho("g", 0.504f));
        Assert.True(f.IsEcho("g", 0.496f));
        Assert.False(f.IsEcho("g", 0.51f));
    }

    [Fact]
    public void EcoAtrasadoDeUmValorAnteriorDoArrasteEIgnorado()
    {
        var f = Novo();
        f.Register("g", 0.50f);
        f.Register("g", 0.52f);
        Assert.True(f.IsEcho("g", 0.52f));
        Assert.True(f.IsEcho("g", 0.50f)); // o eco do 50 chega depois do 52
    }

    [Fact]
    public void ValorDiferenteDeTodosPassa()
    {
        var f = Novo();
        f.Register("g", 0.5f);
        f.Register("g", 0.6f);
        Assert.False(f.IsEcho("g", 0.8f));
    }

    [Fact]
    public void DepoisDeUmSegundoOEcoPassa()
    {
        var f = Novo();
        f.Register("g", 0.5f);
        _agora += TimeSpan.FromMilliseconds(1001);
        Assert.False(f.IsEcho("g", 0.5f));
    }

    [Fact]
    public void AntesDeUmSegundoAindaEEco()
    {
        var f = Novo();
        f.Register("g", 0.5f);
        _agora += TimeSpan.FromMilliseconds(900);
        Assert.True(f.IsEcho("g", 0.5f));
    }

    [Fact]
    public void SoOsUltimosOitoValoresContam()
    {
        var f = Novo();
        for (var i = 0; i < 9; i++) f.Register("g", 0.1f * (i + 1) / 2); // 0,05 a 0,45
        Assert.False(f.IsEcho("g", 0.05f)); // o primeiro saiu
        Assert.True(f.IsEcho("g", 0.10f));
        Assert.True(f.IsEcho("g", 0.45f));
    }

    [Fact]
    public void GruposNaoSeMisturam()
    {
        var f = Novo();
        f.Register("a", 0.5f);
        Assert.False(f.IsEcho("b", 0.5f));
        Assert.False(f.IsEcho("nunca", 0.5f));
    }
}
