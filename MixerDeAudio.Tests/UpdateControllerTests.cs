using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

public class UpdateControllerTests
{
    private sealed class FakeSource : IUpdateSource
    {
        public bool Installed { get; set; } = true;
        public string? NewVersion { get; set; }
        public string? LastError { get; set; }
        public CheckResult NextCheck { get; set; } = CheckResult.UpToDate;
        public bool DownloadOk { get; set; } = true;
        public int[] Progress { get; set; } = [10, 50, 100];
        public int Checks, Downloads, Restarts, ExitApplies;
        public bool RestartOk { get; set; } = true;

        public Task<CheckResult> CheckAsync() { Checks++; return Task.FromResult(NextCheck); }

        public Task<bool> DownloadAsync(Action<int> progress)
        {
            Downloads++;
            foreach (var p in Progress) progress(p);
            return Task.FromResult(DownloadOk);
        }

        public bool ApplyAndRestart(Action beforeRestart) { Restarts++; beforeRestart(); return RestartOk; }
        public void ApplyOnExit() => ExitApplies++;
    }

    private static (UpdateController c, FakeSource s) Make(Action? before = null, bool installed = true)
    {
        var s = new FakeSource { Installed = installed };
        return (new UpdateController(s, before ?? (() => { }), a => a()), s);
    }

    [Fact]
    public async Task CopiaNaoInstalada_FicaIndisponivelENaoAcessaNada()
    {
        var (c, s) = Make(installed: false);
        Assert.Equal(UpdateState.Unavailable, c.Status.State);
        await c.CheckAsync(manual: true);
        await c.DownloadAsync();
        c.Restart();
        Assert.Equal(0, s.Checks + s.Downloads + s.Restarts);
        Assert.Equal(UpdateState.Unavailable, c.Status.State);
        Assert.Null(c.LastCheck);
    }

    [Fact]
    public async Task VerificarSemNovidade_VoltaAParado()
    {
        var (c, s) = Make();
        var estados = new List<UpdateState>();
        c.Changed += () => estados.Add(c.Status.State);
        CheckResult? resultado = null;
        c.Checked += (r, _) => resultado = r;
        await c.CheckAsync(manual: false);
        Assert.Equal([UpdateState.Checking, UpdateState.Idle], estados);
        Assert.Equal(UpdateState.Idle, c.Status.State);
        Assert.Equal(CheckResult.UpToDate, resultado);
        Assert.NotNull(c.LastCheck);
        Assert.Equal(1, s.Checks);
    }

    [Fact]
    public async Task NovaVersao_GuardaAVersaoENaoBaixaSozinho()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        bool? manual = null;
        c.Checked += (_, m) => manual = m;
        await c.CheckAsync(manual: false);
        Assert.Equal(UpdateState.NewVersion, c.Status.State);
        Assert.Equal("1.1.1", c.Status.NewVersion);
        Assert.False(manual);
        Assert.Equal(0, s.Downloads);
    }

    [Fact]
    public async Task Baixar_MostraProgressoEFicaPronto()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        await c.CheckAsync(true);
        var percentuais = new List<int>();
        c.Changed += () => { if (c.Status.State == UpdateState.Downloading) percentuais.Add(c.Status.Percent); };
        await c.DownloadAsync();
        Assert.Equal([0, 10, 50, 100], percentuais);
        Assert.Equal(UpdateState.Ready, c.Status.State);
        Assert.Equal(100, c.Status.Percent);
        Assert.Equal("1.1.1", c.Status.NewVersion);
    }

    [Fact]
    public async Task ProgressoVemDeOutraThread_VaiPeloPost()
    {
        var s = new FakeSource { NextCheck = CheckResult.NewVersion, NewVersion = "1.1.1" };
        var postados = 0;
        var c = new UpdateController(s, () => { }, a => { postados++; a(); });
        await c.CheckAsync(true);
        await c.DownloadAsync();
        Assert.Equal(3, postados);
    }

    [Fact]
    public async Task ErroNaVerificacao_MostraMensagemCurta()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.Error;
        s.LastError = "sem rede\r\nmuito longo " + new string('x', 200);
        await c.CheckAsync(true);
        Assert.Equal(UpdateState.Error, c.Status.State);
        Assert.NotNull(c.Status.Error);
        Assert.DoesNotContain('\n', c.Status.Error);
        Assert.True(c.Status.Error!.Length <= 80);
        // Pode procurar de novo depois do erro.
        s.NextCheck = CheckResult.UpToDate;
        await c.CheckAsync(true);
        Assert.Equal(UpdateState.Idle, c.Status.State);
    }

    [Fact]
    public async Task ErroAoBaixar_VoltaANovaVersaoComMensagem()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        await c.CheckAsync(true);
        s.DownloadOk = false;
        s.LastError = "disco cheio";
        await c.DownloadAsync();
        Assert.Equal(UpdateState.NewVersion, c.Status.State);
        Assert.Equal("disco cheio", c.Status.Error);
        Assert.Equal(0, c.Status.Percent);
    }

    [Fact]
    public async Task Reiniciar_SoComVersaoPronta()
    {
        var antes = 0;
        var (c, s) = Make(() => antes++);
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        await c.CheckAsync(true);
        c.Restart(); // ainda não baixou
        Assert.Equal(0, s.Restarts);
        await c.DownloadAsync();
        c.Restart();
        Assert.Equal(1, s.Restarts);
        Assert.Equal(1, antes);
    }

    [Fact]
    public async Task ReiniciarQueFalha_ContinuaProntoComMensagem()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        s.RestartOk = false;
        s.LastError = "sem permissão";
        await c.CheckAsync(true);
        await c.DownloadAsync();
        c.Restart();
        Assert.Equal(UpdateState.Ready, c.Status.State);
        Assert.Equal("sem permissão", c.Status.Error);
    }

    [Fact]
    public async Task Pronto_NaoConfereDeNovo()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        await c.CheckAsync(true);
        await c.DownloadAsync();
        await c.CheckAsync(true);
        Assert.Equal(1, s.Checks);
        Assert.Equal(UpdateState.Ready, c.Status.State);
    }

    [Fact]
    public async Task Sair_SoAplicaSeEstaPronto()
    {
        var (c, s) = Make();
        s.NextCheck = CheckResult.NewVersion;
        s.NewVersion = "1.1.1";
        c.ApplyOnExit();
        await c.CheckAsync(true);
        c.ApplyOnExit();
        Assert.Equal(0, s.ExitApplies);
        await c.DownloadAsync();
        c.ApplyOnExit();
        Assert.Equal(1, s.ExitApplies);
    }
}
