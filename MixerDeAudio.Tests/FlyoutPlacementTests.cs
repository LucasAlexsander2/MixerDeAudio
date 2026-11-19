using MixerDeAudio.Flyout;

namespace MixerDeAudio.Tests;

public class FlyoutPlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void BarraEmbaixo_CursorNoMeio_CentradoEBaseNaMargem()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var r = FlyoutPlacement.Compute(new PixelPoint(960, 1050), Monitor, work, 1.0, 360, 480);
        Assert.Equal(new PixelRect(780, 540, 360, 480), r);
        Assert.Equal(1032 - 12, r.Bottom);
    }

    [Fact]
    public void CursorNoCantoDireito_PresoAMargemDireita()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var r = FlyoutPlacement.Compute(new PixelPoint(1900, 1050), Monitor, work, 1.0, 360, 480);
        Assert.Equal(1920 - 12, r.Right);
        Assert.Equal(1548, r.X);
    }

    [Fact]
    public void CursorNoCantoEsquerdo_PresoAMargemEsquerda()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var r = FlyoutPlacement.Compute(new PixelPoint(5, 1050), Monitor, work, 1.0, 360, 480);
        Assert.Equal(12, r.X);
    }

    [Fact]
    public void Escala150_TamanhoEMargemMultiplicados()
    {
        var monitor = new PixelRect(0, 0, 2880, 1620);
        var work = new PixelRect(0, 0, 2880, 1548);
        var r = FlyoutPlacement.Compute(new PixelPoint(1440, 1600), monitor, work, 1.5, 360, 480);
        Assert.Equal(540, r.Width);
        Assert.Equal(720, r.Height);
        Assert.Equal(1548 - 18, r.Bottom);
        Assert.Equal(1170, r.X);
    }

    [Fact]
    public void BarraEmCima_AbaixoDoTopoComMargem()
    {
        var work = new PixelRect(0, 48, 1920, 1032);
        var r = FlyoutPlacement.Compute(new PixelPoint(960, 20), Monitor, work, 1.0, 360, 480);
        Assert.Equal(60, r.Y);
        Assert.Equal(780, r.X);
    }

    [Fact]
    public void BarraAEsquerda_EncostadaComMargemEYCentrado()
    {
        var work = new PixelRect(48, 0, 1872, 1080);
        var r = FlyoutPlacement.Compute(new PixelPoint(20, 500), Monitor, work, 1.0, 360, 480);
        Assert.Equal(60, r.X);
        Assert.Equal(260, r.Y);
    }

    [Fact]
    public void BarraADireita_EncostadaComMargemEYCentrado()
    {
        var work = new PixelRect(0, 0, 1872, 1080);
        var r = FlyoutPlacement.Compute(new PixelPoint(1900, 500), Monitor, work, 1.0, 360, 480);
        Assert.Equal(1872 - 12, r.Right);
        Assert.Equal(260, r.Y);
    }

    [Fact]
    public void BarraAEsquerda_YPresoNasMargens()
    {
        var work = new PixelRect(48, 0, 1872, 1080);
        var r = FlyoutPlacement.Compute(new PixelPoint(20, 1070), Monitor, work, 1.0, 360, 480);
        Assert.Equal(1080 - 12, r.Bottom);
    }

    [Fact]
    public void BarraOculta_ComoEmbaixo()
    {
        var r = FlyoutPlacement.Compute(new PixelPoint(960, 1075), Monitor, Monitor, 1.0, 360, 480);
        Assert.Equal(new PixelRect(780, 1080 - 480 - 12, 360, 480), r);
    }

    [Fact]
    public void JanelaMaisAltaQueAArea_AlturaReduzida()
    {
        var monitor = new PixelRect(0, 0, 1920, 448);
        var work = new PixelRect(0, 0, 1920, 400);
        var r = FlyoutPlacement.Compute(new PixelPoint(960, 420), monitor, work, 1.0, 360, 480);
        Assert.Equal(400 - 24, r.Height);
        Assert.Equal(12, r.Y);
        Assert.Equal(400 - 12, r.Bottom);
    }

    [Fact]
    public void MonitorSecundarioComCoordenadasNegativas()
    {
        var monitor = new PixelRect(-1920, 0, 1920, 1080);
        var work = new PixelRect(-1920, 0, 1920, 1032);
        var r = FlyoutPlacement.Compute(new PixelPoint(-100, 1050), monitor, work, 1.0, 360, 480);
        Assert.Equal(0 - 12, r.Right);
        Assert.Equal(540, r.Y);
        Assert.True(r.X >= work.X);
    }
}
