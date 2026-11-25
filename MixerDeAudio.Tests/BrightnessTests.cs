using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 12: conversão do brilho, agrupamento do worker, comando e estado do protocolo.</summary>
public class BrightnessTests
{
    // ---- BrightnessRules (D5) ----

    [Theory]
    [InlineData(0, 0, 100, 0)]
    [InlineData(37, 0, 100, 37)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(0, 20, 80, 20)]
    [InlineData(50, 20, 80, 50)]
    [InlineData(100, 20, 80, 80)]
    [InlineData(25, 20, 80, 35)]
    [InlineData(-10, 20, 80, 20)]
    [InlineData(250, 20, 80, 80)]
    [InlineData(40, 7, 7, 7)]
    public void ToRaw(int pct, int min, int max, int esperado) => Assert.Equal(esperado, BrightnessRules.ToRaw(pct, min, max));

    [Theory]
    [InlineData(0, 0, 100, 0)]
    [InlineData(80, 0, 100, 80)]
    [InlineData(20, 20, 80, 0)]
    [InlineData(50, 20, 80, 50)]
    [InlineData(80, 20, 80, 100)]
    [InlineData(5, 20, 80, 0)]
    [InlineData(200, 20, 80, 100)]
    [InlineData(7, 7, 7, 100)]
    public void ToPercent(int raw, int min, int max, int esperado) => Assert.Equal(esperado, BrightnessRules.ToPercent(raw, min, max));

    [Fact]
    public void IdaEVolta_NaFaixa20a80()
    {
        for (var p = 0; p <= 100; p += 5) Assert.Equal(p, BrightnessRules.ToPercent(BrightnessRules.ToRaw(p, 20, 80), 20, 80));
    }

    // ---- BrightnessWorker (D6, D8) ----

    private static BrightnessWorker Criar(int telas, Func<int, int, bool> write)
    {
        var w = new BrightnessWorker(() => Enumerable.Repeat<int?>(50, telas).ToArray(), write);
        w.Refresh();
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        return w;
    }

    [Fact]
    public void Worker_LeValoresIniciais_ETelaInexistenteEhIgnorada()
    {
        using var w = Criar(2, (_, _) => true);
        Assert.Equal(new int?[] { 50, 50 }, w.Values);
        Assert.False(w.Request(2, 10));
        Assert.False(w.Request(-1, 10));
    }

    [Fact]
    public void Worker_TelaSemSuporte_NaoAceitaPedido()
    {
        using var w = new BrightnessWorker(() => new int?[] { null, 30 }, (_, _) => true);
        w.Refresh();
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.False(w.Request(0, 10));
        Assert.True(w.Request(1, 10));
    }

    [Fact]
    public void Worker_DezPedidosSeguidos_AplicaSoOPrimeiroEOUltimo_SemSobreposicao()
    {
        var aplicados = new List<(int Tela, int Pct)>();
        var entrou = new ManualResetEventSlim();
        var liberar = new ManualResetEventSlim();
        var dentro = 0;
        var sobrepôs = false;
        using var w = Criar(1, (t, p) =>
        {
            if (Interlocked.Increment(ref dentro) > 1) sobrepôs = true;
            lock (aplicados) aplicados.Add((t, p));
            entrou.Set();
            liberar.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref dentro);
            return true;
        });
        Assert.True(w.Request(0, 1));
        Assert.True(entrou.Wait(TimeSpan.FromSeconds(5)));   // o primeiro já está sendo aplicado
        for (var v = 2; v <= 10; v++) Assert.True(w.Request(0, v));
        liberar.Set();
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { (0, 1), (0, 10) }, aplicados);
        Assert.False(sobrepôs);
        Assert.Equal(10, w.Values[0]);
    }

    [Fact]
    public void Worker_CadaTelaTemASuaFila_UmaPorVez()
    {
        var aplicados = new List<(int Tela, int Pct)>();
        var entrou = new ManualResetEventSlim();
        var liberar = new ManualResetEventSlim();
        using var w = Criar(2, (t, p) =>
        {
            lock (aplicados) aplicados.Add((t, p));
            entrou.Set();
            liberar.Wait(TimeSpan.FromSeconds(5));
            return true;
        });
        w.Request(0, 11);
        Assert.True(entrou.Wait(TimeSpan.FromSeconds(5)));
        w.Request(1, 21);
        w.Request(0, 12);
        w.Request(1, 22);
        liberar.Set();
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { (0, 11), (0, 12), (1, 22) }, aplicados);
        Assert.Equal(new int?[] { 12, 22 }, w.Values);
    }

    [Fact]
    public void Worker_ValorForaDaFaixaEhCortado()
    {
        var aplicados = new List<int>();
        using var w = Criar(1, (_, p) => { lock (aplicados) aplicados.Add(p); return true; });
        w.Request(0, 500);
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        w.Request(0, -5);
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { 100, 0 }, aplicados);
    }

    [Fact]
    public void Worker_FalhaNaEscrita_AvisaEMantemOValor()
    {
        var falhas = new List<int>();
        using var w = Criar(1, (_, _) => false);
        w.Failed += falhas.Add;
        w.Request(0, 10);
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { 0 }, falhas);
        Assert.Equal(50, w.Values[0]);
    }

    [Fact]
    public void Worker_ExcecaoNaEscrita_ContaComoFalhaENaoMataOWorker()
    {
        var falhas = new List<int>();
        var primeira = true;
        using var w = Criar(1, (_, _) =>
        {
            if (primeira) { primeira = false; throw new InvalidOperationException(); }
            return true;
        });
        w.Failed += falhas.Add;
        w.Request(0, 10);
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        w.Request(0, 20);
        Assert.True(w.WaitIdle(TimeSpan.FromSeconds(5)));
        Assert.Single(falhas);
        Assert.Equal(20, w.Values[0]);
    }

    [Fact]
    public void RecordingPc_Brilho_GravaNoLogEGuardaEmMemoria()
    {
        var log = new List<string>();
        var pc = new RecordingPc(log.Add);
        Assert.Equal(new int?[] { 50 }, pc.ReadBrightness());
        Assert.True(pc.SetBrightness(0, 30));
        Assert.False(pc.SetBrightness(1, 30));
        Assert.Equal(new int?[] { 30 }, pc.ReadBrightness());
        Assert.Equal(new[] { "teste-pc: brilho 0 30" }, log);
    }

    // ---- Protocolo (D10) ----

    private static RemoteCommand Parse(string json)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        return c!;
    }

    [Fact]
    public void Comando_Valido()
    {
        var c = Assert.IsType<BrightnessCommand>(Parse("{\"t\":\"brilho\",\"tela\":1,\"valor\":30}"));
        Assert.Equal(1, c.Screen);
        Assert.Equal(30, c.Percent);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-20, 0)]
    public void Comando_ValorForaDaFaixaEhCortado(int enviado, int esperado)
    {
        var c = Assert.IsType<BrightnessCommand>(Parse("{\"t\":\"brilho\",\"tela\":0,\"valor\":" + enviado + "}"));
        Assert.Equal(esperado, c.Percent);
    }

    [Theory]
    [InlineData("{\"t\":\"brilho\",\"tela\":-1,\"valor\":30}")]
    [InlineData("{\"t\":\"brilho\",\"valor\":30}")]
    [InlineData("{\"t\":\"brilho\",\"tela\":0}")]
    [InlineData("{\"t\":\"brilho\",\"tela\":\"0\",\"valor\":30}")]
    [InlineData("{\"t\":\"brilho\",\"tela\":0.5,\"valor\":30}")]
    [InlineData("{\"t\":\"brilho\",\"tela\":0,\"valor\":\"alto\"}")]
    public void Comando_Invalido(string json) => Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out _));

    [Fact]
    public void Grupo_DoComandoEhMonitores() =>
        Assert.Equal("monitores", RemoteCommandGroups.GroupOf(new BrightnessCommand(0, 10)));

    private static RemoteState Estado(RemotePcState pc) =>
        new(Array.Empty<RemoteOutput>(), false, 0, false, Array.Empty<RemoteApp>(), new[] { "monitores" }, false, pc);

    [Fact]
    public void Estado_Brilho_ComValorENulo()
    {
        var pc = new RemotePcState(Projection: new RemoteProjection("estender", 2),
            Brightness: new[] { new RemoteBrightness(0, 80), new RemoteBrightness(1, null) });
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Estado(pc)));
        var b = doc.RootElement.GetProperty("pc").GetProperty("brilho");
        Assert.Equal(2, b.GetArrayLength());
        Assert.Equal(0, b[0].GetProperty("tela").GetInt32());
        Assert.Equal(80, b[0].GetProperty("valor").GetInt32());
        Assert.Equal(1, b[1].GetProperty("tela").GetInt32());
        Assert.Equal(JsonValueKind.Null, b[1].GetProperty("valor").ValueKind);
    }

    [Fact]
    public void Estado_SemGrupoMonitores_NaoTemBrilho()
    {
        var pc = new RemotePcState(PowerOn: true);
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Estado(pc)));
        Assert.False(doc.RootElement.GetProperty("pc").TryGetProperty("brilho", out _));
    }
}
