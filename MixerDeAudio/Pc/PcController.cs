using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Remote;
using MixerDeAudio.Services;

namespace MixerDeAudio.Pc;

/// <summary>
/// O que o celular faz no PC (Fase 9, D7 a D10), na thread da tela. Criado no primeiro uso (quando um grupo de ferramentas
/// está ligado). Guarda o timer de energia (um único <see cref="DispatcherQueueTimer"/> de disparo único, que só existe
/// enquanto há algo programado e continua com o celular desconectado) e a última leitura da projeção. As ações vão para o
/// <see cref="IPcActions"/>: o <c>RealPc</c>, ou o <c>RecordingPc</c> com <c>--teste-pc</c> (o <c>RealPc</c> nunca é criado).
/// </summary>
internal sealed class PcController
{
    private readonly DispatcherQueue _ui;
    private readonly bool _testMode;
    private readonly Action<string, string> _balloon;
    private readonly PowerTimer _power = new();
    private IPcActions? _actions;
    private BrightnessWorker? _brightness;
    private DispatcherQueueTimer? _timer;
    private ProjectionInfo _projection;
    private bool _hadDisplay;
    private DispatcherQueueTimer? _awakeTimer;
    private bool _awake;
    private WindowList? _windows;
    private bool _hadShortcuts;

    // Timer de sono (9c): um único timer; só existe enquanto há algo programado
    private enum SleepPhase { None, Waiting, Fading, Pausing }
    private const string SleepOwner = "sono";
    private DispatcherQueueTimer? _sleepTimer;
    private SleepPlan? _sleep;
    private SleepPhase _sleepPhase;
    private int _sleepStep, _sleepSteps, _sleepGen;
    private float _sleepOriginal = -1f;
    private bool _sleepOwner;

    public PcController(DispatcherQueue ui, bool testMode, Action<string, string> balloon)
    {
        _ui = ui;
        _testMode = testMode;
        _balloon = balloon;
    }

    /// <summary>O plano de energia ou a projeção mudou (para atualizar o celular). Thread da tela.</summary>
    public event Action? Changed;

    /// <summary>O plano de energia mudou (para o item "Cancelar" da bandeja). Nulo = sem plano.</summary>
    public event Action<PowerPlan?>? PowerPlanChanged;

    public bool TestMode => _testMode;
    public PowerPlan? Plan => _power.Plan;
    public ProjectionInfo Projection => _projection;

    /// <summary>"Manter a tela ligada" está ativo (D16).</summary>
    public bool Awake => _awake;

    /// <summary>As ações do PC (criadas no primeiro uso; no modo de teste, só o log). Pedir só na thread da tela.</summary>
    public IPcActions Actions => _actions ??= PcActionsFactory.Create(_testMode, () => new RealPc(), () => new RecordingPc(Log.Write));

    // ---- Energia (D8) ----

    /// <summary>Agora (0) ou daqui a <paramref name="seconds"/>. Um timer por vez: o novo substitui o anterior.</summary>
    public void SchedulePower(PowerAction action, int seconds)
    {
        var id = PowerRules.Id(action);
        Log.Write($"celular: energia {id} em {seconds} s");
        if (seconds == 0)
        {
            Run(action);
            return;
        }
        _power.Schedule(action, seconds);
        Notify("Mixer de Áudio", PowerRules.BalloonText(action, seconds));
        Arm();
        PlanChanged();
    }

    /// <summary>Cancela o timer de energia (celular ou item da bandeja).</summary>
    public void CancelPower(string? reason = null)
    {
        if (!_power.Cancel()) return;
        Log.Write(reason is null ? "celular: energia cancelada" : $"celular: energia cancelada ({reason})");
        Disarm();
        PlanChanged();
    }

    private void Run(PowerAction action)
    {
        Log.Write($"celular: energia {PowerRules.Id(action)} executada");
        try { Actions.Power(action); }
        catch (Exception ex) { Log.Write($"ERRO celular: energia: {ex.Message}"); }
    }

    private void Arm()
    {
        if (_power.DelayToNext() is not { } delay)
        {
            Disarm();
            return;
        }
        if (_timer is null)
        {
            _timer = _ui.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += OnTick;
        }
        _timer.Stop();
        _timer.Interval = delay < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : delay;
        _timer.Start();
    }

    private void Disarm()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var ev = _power.Poll();
        switch (ev.Kind)
        {
            case PowerEventKind.Warn:
                Notify("Mixer de Áudio", PowerRules.BalloonText(ev.Action, PowerRules.WarnSeconds));
                Arm();
                break;
            case PowerEventKind.Execute:
                Disarm();
                PlanChanged();
                Run(ev.Action);
                break;
            default:
                Arm();
                break;
        }
    }

    private void PlanChanged()
    {
        PowerPlanChanged?.Invoke(_power.Plan);
        Changed?.Invoke();
    }

    private void Notify(string title, string text)
    {
        Log.Write($"celular: balao: {text}");
        try { _balloon(title, text); }
        catch (Exception ex) { Log.Write($"ERRO celular: balao: {ex.Message}"); }
    }

    // ---- Monitores (D9, D10) ----

    /// <summary>Lê a projeção atual do Windows (leitura pura, também no modo de teste).</summary>
    public void RefreshProjection()
    {
        _projection = DisplayTopology.Read();
        Changed?.Invoke();
        Worker().Refresh(); // Fase 12, D8: a leitura do brilho roda no worker e avisa por Changed
    }

    // ---- Brilho (Fase 12, D6 a D8 e D12) ----

    /// <summary>Último brilho conhecido de cada tela (0 a 100; nulo = sem DDC/CI). Vazio até a primeira leitura.</summary>
    public IReadOnlyList<int?> BrightnessValues => _brightness?.Values ?? Array.Empty<int?>();

    /// <summary>Pede o brilho da tela (cortado em 0 a 100); o worker junta os pedidos e aplica um por vez. Thread da tela.</summary>
    public void RequestBrightness(int tela, int pct) => Worker().Request(tela, pct);

    /// <summary>A escrita do brilho falhou na tela dada (D12). Chamado na thread da tela.</summary>
    public event Action<int>? BrightnessFailed;

    private BrightnessWorker Worker()
    {
        if (_brightness is { } b) return b;
        var actions = Actions;
        b = new BrightnessWorker(actions.ReadBrightness, actions.SetBrightness);
        b.Changed += () => _ui.TryEnqueue(() => Changed?.Invoke());
        b.Failed += tela =>
        {
            Log.Write($"ERRO celular: brilho da tela {tela}");
            _ui.TryEnqueue(() => BrightnessFailed?.Invoke(tela));
        };
        _brightness = b;
        return b;
    }

    /// <summary>Troca a projeção. Devolve falso se o Windows recusou.</summary>
    public bool SetProjection(ProjectionMode mode)
    {
        Log.Write($"celular: projecao {DisplayTopology.Id(mode)}");
        bool ok;
        try { ok = Actions.SetProjection(mode); }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: projecao: {ex.Message}");
            ok = false;
        }
        RefreshProjection();
        return ok;
    }

    public void ScreensOff()
    {
        Log.Write("celular: telas apagadas");
        try { Actions.ScreensOff(); }
        catch (Exception ex) { Log.Write($"ERRO celular: telas: {ex.Message}"); }
    }

    public void ScreensOn()
    {
        Log.Write("celular: telas acordadas");
        try { Actions.ScreensOn(); }
        catch (Exception ex) { Log.Write($"ERRO celular: telas: {ex.Message}"); }
    }

    // ---- Manter a tela ligada (D16) ----

    /// <summary>
    /// Liga ou desliga. Um único timer de disparo único desliga sozinho em 4 h; continua com o celular desconectado (para o
    /// vídeo não parar quando o celular apaga a tela). Thread da tela.
    /// </summary>
    public void SetAwake(bool on, string? reason = null)
    {
        if (on == _awake) return;
        Log.Write(reason is null ? $"celular: acordado {(on ? "on" : "off")}" : $"celular: acordado off ({reason})");
        _awake = on;
        try { Actions.KeepAwake(on); }
        catch (Exception ex) { Log.Write($"ERRO celular: acordado: {ex.Message}"); }
        if (on)
        {
            _awakeTimer ??= CreateAwakeTimer();
            _awakeTimer.Stop();
            _awakeTimer.Start();
        }
        else
        {
            DisarmAwake();
        }
        Changed?.Invoke();
    }

    private DispatcherQueueTimer CreateAwakeTimer()
    {
        var t = _ui.CreateTimer();
        t.IsRepeating = false;
        t.Interval = KeepAwake.MaxDuration;
        t.Tick += OnAwakeTick;
        return t;
    }

    private void OnAwakeTick(DispatcherQueueTimer sender, object args) => SetAwake(false, "passaram 4 h");

    private void DisarmAwake()
    {
        if (_awakeTimer is null) return;
        _awakeTimer.Stop();
        _awakeTimer.Tick -= OnAwakeTick;
        _awakeTimer = null;
    }

    // ---- Janelas (Fase 9c, D21 e D22) ----

    /// <summary>A lista de janelas para o celular (criada no primeiro pedido). Thread da tela.</summary>
    public IReadOnlyList<WindowItem> ListWindows() => (_windows ??= new WindowList()).List(_testMode);

    /// <summary>
    /// Frente, minimizar ou fechar. O id é conferido contra a lista que o celular viu e a lista relida agora, com o mesmo processo;
    /// senão é ignorado. No <c>--teste-pc</c> só a janela da <c>janela-teste.exe</c> (título <c>MX-teste</c>) recebe minimizar e
    /// fechar de verdade; todas as outras, e a ação "frente" de qualquer uma, só vão para o log. Nunca mata processo.
    /// </summary>
    public void ControlWindow(long id, WindowAction action)
    {
        var target = _windows?.Resolve(id, _testMode);
        if (target is null)
        {
            Log.Write("celular: janela ignorada (fora da lista)");
            return;
        }
        Log.Write($"celular: janela {WindowFilter.ActionId(action)} ({target.Process})");
        try
        {
            Actions.ControlWindow(target, action);
            if (_testMode && action != WindowAction.Front && WindowFilter.IsTestTitle(target.Title)
                && string.Equals(target.Process, TestWindowProcess, StringComparison.OrdinalIgnoreCase))
                WindowList.Perform(new IntPtr(target.Id), action);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: janela: {ex.Message}");
        }
    }

    private const string TestWindowProcess = "janela-teste";

    // ---- Atalhos (Fase 9c, D23) ----

    public string ShortcutFolderPath => ShortcutFolder.PathFor(AppPaths.DataFolder, _testMode);

    /// <summary>Os nomes mostrados no celular (lidos agora; a pasta não é observada).</summary>
    public List<string> ShortcutNames() => ShortcutRules.DisplayNames(ShortcutFolder.Files(ShortcutFolderPath));

    /// <summary>Abre o atalho se o nome estiver na lista da pasta; o caminho é montado aqui, nunca vem do celular.</summary>
    public void OpenShortcut(string name)
    {
        var folder = ShortcutFolderPath;
        var file = ShortcutRules.Resolve(ShortcutFolder.Files(folder), name);
        if (file is null)
        {
            Log.Write("celular: atalho ignorado (fora da lista)");
            return;
        }
        Log.Write($"celular: atalho aberto {name}");
        try { Actions.OpenShortcut(Path.Combine(folder, file), name); }
        catch (Exception ex) { Log.Write($"ERRO celular: atalho: {ex.Message}"); }
    }

    // ---- Timer de sono (Fase 9c, D24) ----

    /// <summary>O timer de sono em andamento (esperando, descendo o volume ou pausando); nulo se não há.</summary>
    public SleepPlan? Sleep => _sleep;

    /// <summary>
    /// Programa o timer de sono para daqui a <paramref name="seconds"/> (um por vez: o novo substitui o anterior). Um único
    /// <see cref="DispatcherQueueTimer"/> espera sem observar nada; nos últimos 30 s o hub é adquirido, o volume geral desce
    /// em passos de 1 s, os apps tocando são pausados e o volume volta ao original. Continua com o celular desconectado.
    /// </summary>
    public void ScheduleSleep(int seconds, bool testSeconds)
    {
        CancelSleepCore();
        _sleep = new SleepPlan(DateTimeOffset.UtcNow.AddSeconds(seconds), seconds);
        Log.Write(testSeconds ? $"celular: sono em {seconds} s" : $"celular: sono em {seconds / 60} min");
        _sleepPhase = SleepPhase.Waiting;
        _sleepOriginal = -1f;
        _sleepStep = _sleepSteps = 0;
        ArmSleep(_sleep.FadeStartsAt - DateTimeOffset.UtcNow, repeating: false);
        Changed?.Invoke();
    }

    /// <summary>Cancela o timer de sono (celular, grupo desligado ou saída do app). Se já descia o volume, ele volta ao original.</summary>
    public void CancelSleep(string? reason = null)
    {
        if (_sleep is null) return;
        CancelSleepCore();
        Log.Write(reason is null ? "celular: sono cancelado" : $"celular: sono cancelado ({reason})");
        Changed?.Invoke();
    }

    private void CancelSleepCore()
    {
        if (_sleep is null) return;
        _sleepGen++;
        DisarmSleep();
        if (_sleepOwner)
        {
            if (_sleepOriginal >= 0 && _sleepPhase >= SleepPhase.Fading) SetSleepVolume(_sleepOriginal);
            ReleaseSleepHub();
        }
        _sleep = null;
        _sleepPhase = SleepPhase.None;
    }

    private void ArmSleep(TimeSpan interval, bool repeating)
    {
        if (_sleepTimer is null)
        {
            _sleepTimer = _ui.CreateTimer();
            _sleepTimer.Tick += OnSleepTick;
        }
        _sleepTimer.Stop();
        _sleepTimer.IsRepeating = repeating;
        _sleepTimer.Interval = interval < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : interval;
        _sleepTimer.Start();
    }

    private void DisarmSleep()
    {
        if (_sleepTimer is null) return;
        _sleepTimer.Stop();
        _sleepTimer.Tick -= OnSleepTick;
        _sleepTimer = null;
    }

    private void OnSleepTick(DispatcherQueueTimer sender, object args)
    {
        if (_sleep is null) return;
        if (_sleepPhase == SleepPhase.Waiting)
        {
            // Começa a descida: o hub existe só a partir daqui (e até o fim)
            App.Hub.Acquire(SleepOwner);
            _sleepOwner = true;
            _sleepSteps = Math.Max(1, _sleep.FadeSeconds);
            _sleepStep = 0;
            _sleepOriginal = App.Hub.MasterAvailable ? App.Hub.MasterVolume : -1f;
            _sleepPhase = SleepPhase.Fading;
            ArmSleep(TimeSpan.FromSeconds(1), repeating: true);
            return;
        }
        if (_sleepPhase != SleepPhase.Fading) return;
        _sleepStep++;
        if (_sleepOriginal < 0 && App.Hub.MasterAvailable) _sleepOriginal = App.Hub.MasterVolume; // ainda não tinha sido lido; nada foi mexido
        if (_sleepOriginal >= 0) SetSleepVolume(SleepRules.Volume(_sleepOriginal, _sleepStep, _sleepSteps));
        if (_sleepStep >= _sleepSteps) _ = FinishSleepAsync();
    }

    private async Task FinishSleepAsync()
    {
        var gen = _sleepGen;
        DisarmSleep();
        _sleepPhase = SleepPhase.Pausing;
        var paused = 0;
        try { paused = await Actions.SleepPauseAll(); }
        catch (Exception ex) { Log.Write($"ERRO celular: sono: {ex.Message}"); }
        if (gen != _sleepGen) return; // cancelado durante a pausa: o cancelamento já devolveu o volume e soltou o hub
        if (_sleepOriginal >= 0) SetSleepVolume(_sleepOriginal);
        ReleaseSleepHub();
        _sleep = null;
        _sleepPhase = SleepPhase.None;
        Log.Write($"celular: sono executado ({paused} apps pausados)");
        Changed?.Invoke();
    }

    private void SetSleepVolume(float volume)
    {
        try { Actions.SleepVolume(volume); }
        catch (Exception ex) { Log.Write($"ERRO celular: sono: {ex.Message}"); }
    }

    private void ReleaseSleepHub()
    {
        if (!_sleepOwner) return;
        _sleepOwner = false;
        App.Hub.Release(SleepOwner);
    }

    // ---- Área de transferência e arquivo (Fase 9d, D25 a D28) ----

    /// <summary>
    /// O texto do PC para o celular, já cortado em 15.000 caracteres. <c>Failed</c>: o Windows não deixou ler. Thread da tela.
    /// </summary>
    public (string Text, bool Cut, bool Failed) ReadClipboard()
    {
        string? text = null;
        try { text = Actions.ReadClipboard(); }
        catch (Exception ex) { Log.Write($"ERRO celular: area de transferencia: {ex.Message}"); }
        if (text is null) return ("", false, true);
        var (limited, cut) = ClipboardBridge.Limit(text);
        Log.Write($"celular: area de transferencia lida ({limited.Length} caracteres)");
        return (limited, cut, false);
    }

    /// <summary>Grava o texto do celular (cortado em 15.000). Devolve falso se o Windows recusou. Thread da tela.</summary>
    public bool WriteClipboard(string text)
    {
        var (limited, _) = ClipboardBridge.Limit(text);
        var ok = false;
        try { ok = Actions.WriteClipboard(limited); }
        catch (Exception ex) { Log.Write($"ERRO celular: area de transferencia: {ex.Message}"); }
        if (ok) Log.Write($"celular: area de transferencia gravada ({limited.Length} caracteres)");
        return ok;
    }

    private FileReceiver? _files;

    /// <summary>O recebedor de arquivos (criado no primeiro envio). Thread da tela.</summary>
    internal FileReceiver Files => _files ??= new FileReceiver(_testMode);

    /// <summary>Balão da bandeja (no <c>--teste-oculto</c> só o log). Thread da tela.</summary>
    public void Balloon(string text, string logText)
    {
        Log.Write($"celular: balao: {logText}"); // o nome do arquivo não vai para o log (D12)
        try { _balloon("Mixer de Áudio", text); }
        catch (Exception ex) { Log.Write($"ERRO celular: balao: {ex.Message}"); }
    }

    // ---- Interruptores (D2) ----

    /// <summary>
    /// A configuração mudou: desligar um grupo cancela o que dele estava em andamento; desligar o controle pelo celular
    /// inteiro cancela tudo. Thread da tela.
    /// </summary>
    public void OnSettingsChanged(IReadOnlyList<string> tools, bool remoteOn)
    {
        if (!remoteOn || !RemoteTools.IsEnabled(tools, RemoteTools.Power))
            CancelPower(remoteOn ? "grupo desligado" : "controle pelo celular desligado");
        var display = remoteOn && RemoteTools.IsEnabled(tools, RemoteTools.Display);
        if (display && !_hadDisplay) RefreshProjection();
        _hadDisplay = display;
        if (!remoteOn || !RemoteTools.IsEnabled(tools, RemoteTools.Input))
            SetAwake(false, remoteOn ? "grupo desligado" : "controle pelo celular desligado");
        if (!remoteOn || !RemoteTools.IsEnabled(tools, RemoteTools.Info))
            CancelSleep(remoteOn ? "grupo desligado" : "controle pelo celular desligado");
        if (!remoteOn || !RemoteTools.IsEnabled(tools, RemoteTools.Windows)) _windows?.Clear();
        var shortcuts = remoteOn && RemoteTools.IsEnabled(tools, RemoteTools.Shortcuts);
        if (shortcuts && !_hadShortcuts) ShortcutFolder.Ensure(ShortcutFolderPath); // D23: a pasta nasce ao ligar o grupo
        _hadShortcuts = shortcuts;
    }

    /// <summary>Ao sair do app: o timer de energia e o de sono não são salvos, então sair cancela.</summary>
    public void Shutdown()
    {
        CancelPower("saindo do app");
        Disarm();
        SetAwake(false, "saindo do app");
        CancelSleep("saindo do app");
        _brightness?.Dispose();
        _windows?.Clear();
        _files = null;
    }
}
