using System;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace MixerDeAudio.Services;

/// <summary>
/// Envolve o Velopack (Fase 11, D4): confere o GitHub Releases, baixa e aplica. Fora da cópia instalada (publicado\, bin\)
/// tudo fica desligado e nada acessa a rede (D10).
/// </summary>
public sealed class Updater : IUpdateSource
{
    public const string Repository = "https://github.com/LucasAlexsander2/MixerDeAudio";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;
    private UpdateInfo? _downloaded;

    public Updater()
    {
        try
        {
            // O UpdateManager só lê a instalação do Velopack; a rede só é usada em CheckForUpdatesAsync.
            _manager = new UpdateManager(new GithubSource(Repository, null, false));
        }
        catch (Exception)
        {
            _manager = null; // sem instalação do Velopack
        }
    }

    public bool Installed => _manager?.IsInstalled == true;

    /// <summary>Versão em execução: a instalada (Velopack) ou a do assembly (sem o sufixo "+hash").</summary>
    public string CurrentVersion
    {
        get
        {
            var v = _manager?.CurrentVersion?.ToString();
            if (!string.IsNullOrEmpty(v)) return v;
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var plus = info?.IndexOf('+') ?? -1;
            return string.IsNullOrEmpty(info) ? "1.1.0" : plus >= 0 ? info[..plus] : info;
        }
    }

    public string? NewVersion => _pending?.TargetFullRelease.Version.ToString();

    public string? LastError { get; private set; }

    public async Task<CheckResult> CheckAsync()
    {
        if (_manager is null || !_manager.IsInstalled) return CheckResult.NotInstalled;
        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
            _pending = info;
            LastError = null;
            return info is null ? CheckResult.UpToDate : CheckResult.NewVersion;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return CheckResult.Error;
        }
    }

    /// <summary>Baixa a versão encontrada sem aplicar nem fechar o app.</summary>
    public async Task<bool> DownloadAsync(Action<int> progress)
    {
        if (_manager is null || _pending is null) return false;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, progress).ConfigureAwait(true);
            _downloaded = _pending;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>Aplica a versão já baixada e reinicia. Só depois do clique do usuário (D7).</summary>
    public bool ApplyAndRestart(Action beforeRestart)
    {
        if (_manager is null || _downloaded is null) return false;
        try
        {
            beforeRestart();
            _manager.ApplyUpdatesAndRestart(_downloaded.TargetFullRelease);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>Com versão baixada, agenda a aplicação para quando este processo terminar, sem reiniciar (D7).</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _downloaded is null) return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_downloaded.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
