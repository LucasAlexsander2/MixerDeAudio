using MixerDeAudio.Remote;
using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

/// <summary>Fase 10b: porta fixa (D12) e regra "parear x reconectar" (D11).</summary>
public class SessionPortRulesTests
{
    [Fact]
    public void PortaCelular_Padrao_8750()
    {
        Assert.Equal(8750, AppSettings.Default.PortaCelular);
    }

    [Theory]
    [InlineData(8750, 8750)]
    [InlineData(8755, 8755)]
    [InlineData(8759, 8759)]
    [InlineData(8749, 8750)]
    [InlineData(8760, 8750)]
    [InlineData(0, 8750)]
    [InlineData(-5, 8750)]
    [InlineData(80, 8750)]
    public void PortaCelular_ForaDaFaixa_Vira8750(int entrada, int esperado)
    {
        var s = new AppSettings { PortaCelular = entrada }.Normalized();
        Assert.Equal(esperado, s.PortaCelular);
        Assert.Equal(esperado, RemoteRules.NormalizePort(entrada));
    }

    [Fact]
    public void PortaCelular_ArquivoAntigoSemCampo_Vira8750()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"Tema\":\"escuro\",\"CelularLigado\":true}");
        Assert.Equal(8750, s.PortaCelular);
    }

    [Fact]
    public void PortaCelular_Salva_SobreviveAoRoundTrip()
    {
        var lido = AppSettings.Parse(new AppSettings { PortaCelular = 8753 }.Serialize());
        Assert.Equal(8753, lido.PortaCelular);
    }

    [Fact]
    public void PortOrder_SalvaPrimeiro_DepoisAsOutrasEmOrdem()
    {
        var o = RemoteRules.PortOrder(8753);
        Assert.Equal(10, o.Count);
        Assert.Equal(8753, o[0]);
        Assert.Equal(new[] { 8750, 8751, 8752, 8754, 8755, 8756, 8757, 8758, 8759 }, o.Skip(1));
        Assert.Equal(8750, RemoteRules.PortOrder(9999)[0]);
    }

    [Theory]
    [InlineData(401, RemoteRules.PageAction.Pair)]
    [InlineData(204, RemoteRules.PageAction.Connect)]
    [InlineData(200, RemoteRules.PageAction.Connect)]
    [InlineData(null, RemoteRules.PageAction.Reconnect)]   // sem resposta
    [InlineData(500, RemoteRules.PageAction.Reconnect)]
    [InlineData(503, RemoteRules.PageAction.Reconnect)]
    [InlineData(502, RemoteRules.PageAction.Reconnect)]
    [InlineData(409, RemoteRules.PageAction.Reconnect)]    // ocupado
    [InlineData(429, RemoteRules.PageAction.Reconnect)]
    [InlineData(404, RemoteRules.PageAction.Reconnect)]
    public void DecidePage_SoO401LevaAoPareamento(int? status, RemoteRules.PageAction esperado)
    {
        Assert.Equal(esperado, RemoteRules.DecidePage(status));
    }

    [Fact]
    public void ReconnectDelay_CresceAte5s()
    {
        Assert.Equal(new[] { 500, 1000, 2000, 4000, 5000, 5000 }, Enumerable.Range(0, 6).Select(RemoteRules.ReconnectDelayMs));
        Assert.Equal(500, RemoteRules.ReconnectDelayMs(-3));
    }
}
