using System.Text;
using MixerDeAudio.Pc;

namespace MixerDeAudio.Tests;

/// <summary>Fase 10, D1, D2, D4 e D5: mensagens do canal, decisão do laço, comparação de quadros e refazer buffers.</summary>
public class ScreenStreamRulesTests
{
    private static StreamMessage P(string json) => ScreenStreamRules.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Mensagem_Configuracao() =>
        Assert.Equal(new StreamMessage(StreamMessageKind.Config, 1, 1280, "alta"), P("{\"mon\":1,\"w\":1280,\"q\":\"alta\"}"));

    [Fact]
    public void Mensagem_ConfiguracaoParcialUsaZeroEVazio() =>
        Assert.Equal(new StreamMessage(StreamMessageKind.Config, 0, 640, ""), P("{\"w\":640}"));

    [Fact]
    public void Mensagem_Ok() => Assert.Equal(StreamMessageKind.Ok, P("{\"ok\":1}").Kind);

    [Theory]
    [InlineData("")]
    [InlineData("nao e json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"mon\":\"x\",\"w\":\"y\"}")]
    [InlineData("{\"w\":99999999999}")]
    public void Mensagem_Invalida(string json) => Assert.Equal(StreamMessageKind.Invalid, P(json).Kind);

    [Fact]
    public void Mensagem_NegativosViramZero()
    {
        var m = P("{\"mon\":-3,\"w\":-9,\"q\":\"eco\"}");
        Assert.Equal((0, 0, "eco"), (m.Monitor, m.Width, m.Quality));
    }

    [Theory]
    [InlineData(true, 125, 125, true)]
    [InlineData(true, 500, 125, true)]
    [InlineData(true, 100, 125, false)]    // intervalo não venceu
    [InlineData(false, 9999, 125, false)]  // sem "ok" do quadro anterior
    public void PodeCapturar(bool ok, int sinceMs, int intervalMs, bool esperado) =>
        Assert.Equal(esperado, ScreenStreamRules.CanCapture(ok, TimeSpan.FromMilliseconds(sinceMs), TimeSpan.FromMilliseconds(intervalMs)));

    [Fact]
    public void QuantoFalta()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(25), ScreenStreamRules.RemainingDelay(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(125)));
        Assert.Equal(TimeSpan.Zero, ScreenStreamRules.RemainingDelay(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(125)));
    }

    [Fact]
    public void Comparacao_MesmoTamanho()
    {
        var a = new byte[1000]; var b = new byte[1000];
        Assert.True(ScreenStreamRules.SameFrame(a, b));
        b[999] = 1;
        Assert.False(ScreenStreamRules.SameFrame(a, b));
    }

    [Fact]
    public void Comparacao_TamanhosDiferentes() => Assert.False(ScreenStreamRules.SameFrame(new byte[1000], new byte[1004]));

    [Theory]
    [InlineData(false, true, FrameAction.Skip)]
    [InlineData(false, false, FrameAction.Send)]
    [InlineData(true, true, FrameAction.Send)]   // buffers refeitos: sempre vai
    [InlineData(true, false, FrameAction.Send)]
    public void QuadroIgualNaoVai(bool refeitos, bool igual, FrameAction esperado) =>
        Assert.Equal(esperado, ScreenStreamRules.Decide(refeitos, igual));

    [Theory]
    [InlineData(0, 1280, 720, 0, 1280, 720, false)]
    [InlineData(0, 1280, 720, 1, 1280, 720, true)]    // outra tela
    [InlineData(0, 1280, 720, 0, 960, 540, true)]     // outra largura
    [InlineData(-1, 0, 0, 0, 1280, 720, true)]        // primeiro quadro
    public void RefazBuffers(int m, int w, int h, int nm, int nw, int nh, bool esperado) =>
        Assert.Equal(esperado, ScreenStreamRules.NeedsRebuild(m, w, h, nm, nw, nh));

    // ---- Fase 15c, D16: tamanho do ponteiro (cur) e posição do ponteiro (p) ----

    [Fact]
    public void Config_SemCurNemP_ComoSempre()
    {
        var m = P("{\"mon\":0,\"w\":960,\"q\":\"normal\"}");
        Assert.Equal((1.0, false), (m.Cursor, m.Pointer));
    }

    [Theory]
    [InlineData("1", 1.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("2", 2.0)]
    [InlineData("3", 3.0)]
    [InlineData("2.0", 2.0)]
    [InlineData("0", 1.0)]
    [InlineData("4", 1.0)]
    [InlineData("-2", 1.0)]
    [InlineData("1.7", 1.0)]
    [InlineData("\"2\"", 1.0)]
    [InlineData("null", 1.0)]
    [InlineData("true", 1.0)]
    public void Config_Cur_CadaValorEInvalidoViraUm(string cur, double esperado)
    {
        var m = P("{\"mon\":0,\"w\":960,\"q\":\"eco\",\"cur\":" + cur + "}");
        Assert.Equal(StreamMessageKind.Config, m.Kind);
        Assert.Equal(esperado, m.Cursor);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("2", false)]
    [InlineData("\"1\"", false)]
    [InlineData("null", false)]
    public void Config_P(string p, bool esperado) =>
        Assert.Equal(esperado, P("{\"w\":960,\"p\":" + p + "}").Pointer);

    [Fact]
    public void Config_ComCurEPJuntos() =>
        Assert.Equal(new StreamMessage(StreamMessageKind.Config, 1, 1280, "alta", 2.0, true), P("{\"mon\":1,\"w\":1280,\"q\":\"alta\",\"cur\":2,\"p\":1}"));

    [Fact]
    public void Config_SoComCur_Vale() => Assert.Equal(StreamMessageKind.Config, P("{\"cur\":3}").Kind);

    [Fact]
    public void Ok_NaoCarregaCurNemP()
    {
        var m = P("{\"ok\":1,\"cur\":3,\"p\":1}");
        Assert.Equal(StreamMessageKind.Ok, m.Kind);
        Assert.Equal((1.0, false), (m.Cursor, m.Pointer));
    }

    [Theory]
    [InlineData(960, 540, 0, 0, 1920, 1080, 0.5, 0.5)]
    [InlineData(0, 0, 0, 0, 1920, 1080, 0.0, 0.0)]            // canto
    [InlineData(1920, 1080, 0, 0, 1920, 1080, 1.0, 1.0)]      // borda de baixo e da direita
    [InlineData(2500, 1500, 0, 0, 1920, 1080, 1.0, 1.0)]      // fora do quadro: corta
    [InlineData(-200, -50, 0, 0, 1920, 1080, 0.0, 0.0)]       // antes do início: corta
    [InlineData(2880, 540, 1920, 0, 1920, 1080, 0.5, 0.5)]    // segunda tela à direita
    [InlineData(100, 540, 1920, 0, 1920, 1080, 0.0, 0.5)]     // ponteiro está na outra tela
    [InlineData(-960, -540, -1920, -1080, 1920, 1080, 0.5, 0.5)]
    public void PosicaoDoPonteiro(int x, int y, int left, int top, int w, int h, double ex, double ey)
    {
        var (px, py) = ScreenStreamRules.PointerFraction(x, y, left, top, w, h);
        Assert.Equal(ex, px, 6);
        Assert.Equal(ey, py, 6);
    }

    [Fact]
    public void PosicaoDoPonteiro_TelaSemTamanhoDaOCentro() =>
        Assert.Equal((0.5, 0.5), ScreenStreamRules.PointerFraction(10, 10, 0, 0, 0, 0));

    [Theory]
    [InlineData(0.5, 0.25, "{\"p\":[0.5,0.25]}")]
    [InlineData(0.0, 1.0, "{\"p\":[0,1]}")]
    [InlineData(0.123456, 0.987654, "{\"p\":[0.1235,0.9877]}")]
    [InlineData(-3.0, 7.0, "{\"p\":[0,1]}")]
    public void TextoDoPonteiro_UsaPontoEQuatroCasas(double x, double y, string esperado)
    {
        var antes = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            Assert.Equal(esperado, ScreenStreamRules.PointerMessage(x, y));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = antes; }
        using var doc = System.Text.Json.JsonDocument.Parse(ScreenStreamRules.PointerMessage(x, y));
        Assert.Equal(2, doc.RootElement.GetProperty("p").GetArrayLength());
    }
}
