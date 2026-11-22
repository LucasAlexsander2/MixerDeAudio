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
}
