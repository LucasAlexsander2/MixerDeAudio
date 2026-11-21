using MixerDeAudio.Flyout;

namespace MixerDeAudio.Tests;

public class FlyoutSizeTests
{
    [Fact]
    public void ConteudoPequeno_VaiParaOMinimo()
        => Assert.Equal(260, FlyoutSize.Height(100, 1032, 1.0));

    [Fact]
    public void ConteudoMedio_FicaComoPediu()
        => Assert.Equal(400, FlyoutSize.Height(400, 1032, 1.0));

    [Fact]
    public void ConteudoGrande_VaiParaOMaximoAbsoluto()
        => Assert.Equal(560, FlyoutSize.Height(900, 1400, 1.0));

    [Fact]
    public void AreaPequena_Usa70PorCento()
        => Assert.Equal(0.7 * 600, FlyoutSize.Height(900, 600, 1.0), 6);

    [Fact]
    public void Escala150_ConverteAAreaParaDip()
    {
        // 1548 px / 1,5 = 1032 DIP; 70% = 722 -> limita no 560
        Assert.Equal(560, FlyoutSize.Height(900, 1548, 1.5));
        // 720 px / 1,5 = 480 DIP; 70% = 336
        Assert.Equal(336, FlyoutSize.Height(900, 720, 1.5), 6);
    }

    [Fact]
    public void Escala200_ConverteAAreaParaDip()
    {
        // 800 px / 2 = 400 DIP; 70% = 280
        Assert.Equal(280, FlyoutSize.Height(900, 800, 2.0), 6);
        Assert.Equal(260, FlyoutSize.Height(100, 800, 2.0), 6);
    }

    [Fact]
    public void MaximoAbaixoDoMinimo_ValeOMaximo()
        => Assert.Equal(0.7 * 300, FlyoutSize.Height(500, 300, 1.0), 6);

    [Fact]
    public void ExatamenteNosLimites()
    {
        Assert.Equal(260, FlyoutSize.Height(260, 1032, 1.0));
        Assert.Equal(560, FlyoutSize.Height(560, 1400, 1.0));
    }

    [Fact]
    public void Max_CombinaOsDoisLimites()
    {
        Assert.Equal(560, FlyoutSize.Max(1032, 1.0));
        Assert.Equal(0.7 * 1032 / 2.0, FlyoutSize.Max(1032, 2.0), 6);
    }
}
