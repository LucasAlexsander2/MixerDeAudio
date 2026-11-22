using MixerDeAudio.Pc;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9b, D17 a D19: qualidades, tamanho do quadro, ordem das telas e ponto absoluto do mouse.</summary>
public class ScreenRulesTests
{
    [Theory]
    [InlineData(1920, 1080, 640, 640, 360)]
    [InlineData(1920, 1080, 5000, 1920, 1080)]
    [InlineData(1920, 1080, 1280, 1280, 720)]
    [InlineData(3440, 1440, 3000, 1920, 804)]    // ultra larga: corta em 1920
    [InlineData(1920, 1080, 0, 640, 360)]       // sem largura: 640
    [InlineData(1920, 1080, -20, 640, 360)]
    [InlineData(1920, 1080, 50, 160, 90)]       // mínimo de 160
    [InlineData(2560, 1440, 1000, 1000, 563)]   // 1440 x 1000 / 2560 = 562,5
    [InlineData(800, 600, 1280, 800, 600)]      // nunca amplia
    [InlineData(1080, 1920, 640, 640, 1138)]    // tela em pé
    [InlineData(3840, 1080, 1280, 1280, 360)]   // ultra larga
    public void TamanhoDoQuadro(int sw, int sh, int pedida, int w, int h) => Assert.Equal((w, h), ScreenRules.FrameSize(sw, sh, pedida));

    [Fact]
    public void TamanhoDoQuadro_LarguraAbsurda() => Assert.Equal((1920, 1080), ScreenRules.FrameSize(1920, 1080, int.MaxValue));

    [Fact]
    public void TamanhoDoQuadro_TelaInvalida() => Assert.Equal((0, 0), ScreenRules.FrameSize(0, 1080, 640));

    [Theory]
    [InlineData(3840, 2160, 3000, "eco", 960, 540)]
    [InlineData(3840, 2160, 3000, "normal", 1280, 720)]
    [InlineData(3840, 2160, 3000, "alta", 1920, 1080)]
    [InlineData(1600, 900, 3000, "alta", 1600, 900)]    // nunca maior que a tela
    [InlineData(1920, 1080, 100, "alta", 160, 90)]      // mínimo de 160
    [InlineData(1920, 1080, 1000, "eco", 960, 540)]     // pedido 1000, corte da econômica 960
    public void LarguraFinalPorQualidade(int sw, int sh, int pedida, string q, int w, int h) =>
        Assert.Equal((w, h), ScreenRules.FrameSize(sw, sh, pedida, ScreenRules.QualityFor(q).MaxWidth));

    [Fact]
    public void Qualidades_Tabela()
    {
        var eco = ScreenRules.QualityFor("eco");
        var normal = ScreenRules.QualityFor("normal");
        var alta = ScreenRules.QualityFor("alta");
        Assert.Equal(("eco", 500.0, 960, 0.5f), (eco.Id, eco.Interval.TotalMilliseconds, eco.MaxWidth, eco.Jpeg));
        Assert.Equal(("normal", 125.0, 1280, 0.7f), (normal.Id, normal.Interval.TotalMilliseconds, normal.MaxWidth, normal.Jpeg));
        Assert.Equal(("alta", 83.0, 1920, 0.8f), (alta.Id, alta.Interval.TotalMilliseconds, alta.MaxWidth, alta.Jpeg));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ultra")]
    [InlineData("ALTA")]
    public void Qualidade_DesconhecidaViraNormal(string? id) => Assert.Equal("normal", ScreenRules.QualityFor(id).Id);

    private static readonly ScreenInfo[] Duas =
    {
        new(0, -1920, 0, 1920, 1080, false),  // à esquerda da principal (posição negativa)
        new(1, 0, 0, 2560, 1440, true),
    };

    [Fact]
    public void Ordem_PrincipalPrimeiroEOutrasPelaEsquerda()
    {
        var ordem = ScreenRules.Order(new ScreenInfo[]
        {
            new(0, 2560, 0, 1920, 1080, false), new(0, 0, 0, 2560, 1440, true), new(0, -1920, 200, 1920, 1080, false),
        });
        Assert.True(ordem[0].Primary);
        Assert.Equal(new[] { 0, 1, 2 }, ordem.Select(s => s.Index));
        Assert.Equal(new[] { 0, -1920, 2560 }, ordem.Select(s => s.Left));
    }

    [Fact]
    public void AreaVirtual_UniaoDasTelas()
    {
        Assert.Equal((-1920, 0, 4480, 1440), ScreenRules.VirtualBounds(Duas));
        Assert.Equal((0, 0, 0, 0), ScreenRules.VirtualBounds(Array.Empty<ScreenInfo>()));
    }

    [Fact]
    public void AreaVirtual_TelaAcimaComPosicaoNegativaEmY()
    {
        var telas = new ScreenInfo[] { new(0, 0, 0, 1920, 1080, true), new(1, 100, -1080, 1920, 1080, false) };
        Assert.Equal((0, -1080, 2020, 2160), ScreenRules.VirtualBounds(telas));
    }

    [Fact]
    public void PontoAbsoluto_CantosDaTelaPrincipalComAEsquerdaNegativa()
    {
        // área virtual: x de -1920 a 2559 (4480 de largura), y de 0 a 1439
        var topoEsq = ScreenRules.AbsolutePoint(Duas, 1, 0, 0)!.Value;
        var baixoDir = ScreenRules.AbsolutePoint(Duas, 1, 1, 1)!.Value;
        Assert.Equal((int)Math.Floor((1920 + 0.5) * 65536.0 / 4480), topoEsq.X);
        Assert.Equal((int)Math.Floor(0.5 * 65536.0 / 1440), topoEsq.Y);
        Assert.Equal((int)Math.Floor((1920 + 2559 + 0.5) * 65536.0 / 4480), baixoDir.X);
        Assert.Equal((int)Math.Floor((1439 + 0.5) * 65536.0 / 1440), baixoDir.Y);
        Assert.InRange(baixoDir.X, 65500, 65535);
    }

    [Fact]
    public void PontoAbsoluto_TelaAEsquerdaFicaNoComecoDaAreaVirtual()
    {
        var esq = ScreenRules.AbsolutePoint(Duas, 0, 0, 0)!.Value;
        Assert.InRange(esq.X, 0, 10);
        var meio = ScreenRules.AbsolutePoint(Duas, 0, 0.5, 0.5)!.Value;
        // metade da tela da esquerda = x -960 -> (-960 + 1920) / 4480 do total
        Assert.InRange(meio.X, (int)(960.0 / 4480 * 65536) - 20, (int)(960.0 / 4480 * 65536) + 20);
    }

    [Fact]
    public void PontoAbsoluto_ForaDaFaixaECortado()
    {
        var a = ScreenRules.AbsolutePoint(Duas, 1, -3, 9)!.Value;
        var b = ScreenRules.AbsolutePoint(Duas, 1, 0, 1)!.Value;
        Assert.Equal(ScreenRules.AbsolutePoint(Duas, 1, 0, 0)!.Value.X, a.X);
        Assert.Equal(b.Y, a.Y);
    }

    [Fact]
    public void PontoAbsoluto_UmaTelaSoEIndiceInvalido()
    {
        var uma = new[] { new ScreenInfo(0, 0, 0, 1920, 1080, true) };
        var c = ScreenRules.AbsolutePoint(uma, 0, 0.5, 0.5)!.Value;
        Assert.InRange(c.X, 32767 - 40, 32768 + 40);
        Assert.InRange(c.Y, 32767 - 40, 32768 + 40);
        Assert.Null(ScreenRules.AbsolutePoint(uma, 1, 0.5, 0.5));
        Assert.Null(ScreenRules.AbsolutePoint(uma, -1, 0.5, 0.5));
        Assert.Null(ScreenRules.AbsolutePoint(Array.Empty<ScreenInfo>(), 0, 0, 0));
    }

    [Fact]
    public void PontoAbsoluto_Nan() => Assert.NotNull(ScreenRules.AbsolutePoint(Duas, 0, double.NaN, double.NaN));

    [Fact]
    public void Intervalos()
    {
        Assert.Equal(1920, ScreenRules.MaxWidth);
        Assert.Equal(10, ScreenRules.IdleEnd.TotalSeconds);
    }
}
