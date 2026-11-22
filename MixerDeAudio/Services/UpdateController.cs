using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MixerDeAudio.Services;

/// <summary>Resultado de uma verificação de atualização.</summary>
public enum CheckResult
{
    NotInstalled,
    UpToDate,
    NewVersion,
    Error
}

/// <summary>O que o <see cref="UpdateController"/> precisa do Velopack (separado para testar os estados sem ele).</summary>
public interface IUpdateSource
{
    bool Installed { get; }
    string? NewVersion { get; }
    string? LastError { get; }
    Task<CheckResult> CheckAsync();
    Task<bool> DownloadAsync(Action<int> progress);
    bool ApplyAndRestart(Action beforeRestart);
    void ApplyOnExit();
}

public enum UpdateState
{
    Unavailable,
    Idle,
    Checking,
    NewVersion,
    Downloading,
    Ready,
    Error
}

/// <summary>Retrato do estado da atualização, lido pelas Configurações e pela faixa da janelinha.</summary>
public sealed record UpdateStatus(UpdateState State, string? NewVersion, int Percent, double? SecondsLeft, string? Error);

/// <summary>
/// Guarda o estado da atualização num só lugar, um download por vez (Fase 11, D4). Quem mostra só assina <see cref="Changed"/>
/// e lê <see cref="Status"/>; fechar a janela não interrompe o download. Nunca baixa nem instala sozinho (D5).
/// </summary>
public sealed class UpdateController
{
    private readonly IUpdateSource _source;
    private readonly Action _beforeRestart;
    private readonly Action<Action> _post;
    private readonly Stopwatch _clock = new();
    private DownloadEstimate _estimate = new();
    private UpdateStatus _status;

    /// <param name="post">Entrega uma ação na thread da tela (o progresso do Velopack chega de outra thread).</param>
    public UpdateController(IUpdateSource source, Action beforeRestart, Action<Action>? post = null)
    {
        _source = source;
        _beforeRestart = beforeRestart;
        _post = post ?? (a => a());
        _status = new UpdateStatus(source.Installed ? UpdateState.Idle : UpdateState.Unavailable, null, 0, null, null);
    }

    public UpdateStatus Status => _status;
    public DateTime? LastCheck { get; private set; }
    public bool Busy => _status.State is UpdateState.Checking or UpdateState.Downloading;

    /// <summary>Qualquer mudança de estado ou de progresso (na thread da tela).</summary>
    public event Action? Changed;

    /// <summary>Fim de uma verificação: resultado e se foi pedida pelo usuário (para o balão).</summary>
    public event Action<CheckResult, bool>? Checked;

    private void Set(UpdateStatus status)
    {
        _status = status;
        Changed?.Invoke();
    }

    public async Task CheckAsync(bool manual)
    {
        // Ready: já há versão baixada esperando o reinício; conferir de novo só a apagaria da tela.
        if (Busy || !_source.Installed || _status.State == UpdateState.Ready) return;
        Set(_status with { State = UpdateState.Checking, Error = null });
        var result = await _source.CheckAsync();
        LastCheck = DateTime.UtcNow;
        _status = result switch
        {
            CheckResult.NewVersion => new UpdateStatus(UpdateState.NewVersion, _source.NewVersion, 0, null, null),
            CheckResult.Error => new UpdateStatus(UpdateState.Error, null, 0, null, UpdateRules.Summarize(_source.LastError)),
            CheckResult.NotInstalled => new UpdateStatus(UpdateState.Unavailable, null, 0, null, null),
            _ => new UpdateStatus(UpdateState.Idle, null, 0, null, null)
        };
        Changed?.Invoke();
        Checked?.Invoke(result, manual);
    }

    public async Task DownloadAsync()
    {
        if (_status.State != UpdateState.NewVersion || _source.NewVersion is null) return;
        _estimate = new DownloadEstimate();
        _clock.Restart();
        Set(_status with { State = UpdateState.Downloading, Percent = 0, SecondsLeft = null, Error = null });
        var ok = await _source.DownloadAsync(p => _post(() =>
        {
            // Um progresso atrasado pode chegar depois do fim do download.
            if (_status.State != UpdateState.Downloading) return;
            Set(_status with { Percent = p, SecondsLeft = _estimate.Register(p, _clock.Elapsed) });
        }));
        if (ok)
        {
            Set(_status with { State = UpdateState.Ready, Percent = 100, SecondsLeft = null, Error = null });
        }
        else
        {
            // Falhou: o app segue rodando e volta a "versão nova", com a mensagem.
            Set(_status with { State = UpdateState.NewVersion, Percent = 0, SecondsLeft = null, Error = UpdateRules.Summarize(_source.LastError) });
        }
    }

    /// <summary>Aplica a versão baixada e reinicia. Se falhar, continua em Ready com a mensagem.</summary>
    public void Restart()
    {
        if (_status.State != UpdateState.Ready) return;
        if (!_source.ApplyAndRestart(_beforeRestart)) Set(_status with { Error = UpdateRules.Summarize(_source.LastError) });
    }

    /// <summary>No "Sair": com versão baixada, agenda a aplicação para o fim do processo (D7).</summary>
    public void ApplyOnExit()
    {
        if (_status.State == UpdateState.Ready) _source.ApplyOnExit();
    }
}
