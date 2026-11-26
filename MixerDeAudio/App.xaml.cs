using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MixerDeAudio.Audio;
using MixerDeAudio.Flyout;
using MixerDeAudio.Interop;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;
using MixerDeAudio.Services;
using MixerDeAudio.Tray;
using Windows.Graphics;

namespace MixerDeAudio;

/// <summary>
/// O app não tem janela principal (D7): só o ícone da bandeja. A janelinha é criada no primeiro clique e só escondida ao fechar (Fase 8, D1).
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(2);

    private readonly FlyoutToggleGate _gate = new();
    private DispatcherQueueTimer? _trimTimer;
    private TrayIconService? _tray;
    private FlyoutWindow? _flyout;
    private AudioHub _hub = null!;
    private RemoteServer? _remote;
    private bool _noTrim;
    private bool _hiddenTest;
    private bool _testPc;
    private PcController? _pc;
    private bool _exiting;
    private Updater? _updater;
    private UpdateController? _updates;
    private DispatcherQueueTimer? _updateTimer;
    private bool _phoneOn, _screenOn; // Fase 16, D10: estado do ícone da bandeja

    /// <summary>Fila da thread da tela, para receber a ativação redirecionada (que chega em outra thread).</summary>
    public static DispatcherQueue? UiQueue { get; private set; }

    public static App? Instance { get; private set; }

    /// <summary>Atualização pelo próprio app (Fase 11): estado único lido pelas Configurações e pela faixa da janelinha.</summary>
    internal static UpdateController? Updates => Instance?._updates;

    /// <summary>Versão em execução (a instalada, pelo Velopack, ou a do assembly).</summary>
    internal static string CurrentVersion => Instance?._updater?.CurrentVersion ?? "1.1.0";

    /// <summary>Servidor do celular (Fase 6), para a seção "Controle pelo celular" das Configurações.</summary>
    internal static RemoteServer? Remote => Instance?._remote;

    /// <summary>Modo <c>--teste-pc</c> (Fase 9, D7): as ações do PC só vão para o log.</summary>
    internal static bool TestPc => Instance?._testPc ?? false;

    /// <summary>Núcleo de áudio compartilhado (Fase 6); o timer de sono (Fase 9c) o adquire só enquanto desce o volume.</summary>
    internal static AudioHub Hub => Instance!._hub;

    /// <summary>Ações do PC pelo celular (Fase 9). Criado no primeiro uso; só na thread da tela.</summary>
    internal static PcController Pc => Instance!._pc ??= Instance.CreatePc();

    /// <summary>Configuração do usuário em memória (Fase 5, D2); lida uma vez, na partida.</summary>
    public static SettingsStore Settings { get; private set; } = new();

    public App()
    {
        InitializeComponent();
        Instance = this;
        UiQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += (_, e) => Log.Write($"ERRO NAO TRATADO: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"ERRO NAO TRATADO: {e.ExceptionObject}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // O processo não termina quando a janelinha fecha (D7).
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        Log.Write("iniciado");
        Settings = SettingsStore.Load();
        Log.Write($"config carregada (favoritos={Settings.Current.Favoritos.Count}, tema={Settings.Current.Tema})");
        // Primeira cópia: sem argumento só cria o ícone; com argumento, executa o comando (D8).
        var commandLine = CommandLine.Parse(Program.StartupArguments);
        _noTrim = commandLine.NoTrim;
        _hiddenTest = commandLine.HiddenTest;
        _testPc = commandLine.TestPc;
        if (_testPc) Log.Write("teste-pc: modo de teste ligado");
        // Modo de teste oculto (Fase 7): sem ícone na bandeja.
        if (!_hiddenTest)
            _tray = new TrayIconService(onToggle: Toggle, onOpen: () => Open(activate: true, atCursor: true, checkGate: false),
                onSettings: () => Open(activate: true, atCursor: true, checkGate: false, settings: true), onExit: ExitApp);
        TrayIconService.SetEfficiencyMode(true);

        _trimTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _trimTimer.Interval = TrimDelay;
        _trimTimer.IsRepeating = false;
        _trimTimer.Tick += OnTrimTick;

        // Núcleo de áudio compartilhado (Fase 6, D1): o modo de eficiência fica desligado enquanto houver dono (janela ou celular)
        _hub = new AudioHub(DispatcherQueue.GetForCurrentThread());
        _hub.BecameActive += OnHubActive;
        _hub.BecameIdle += OnHubIdle;

        // Servidor do celular (Fase 6, D10 e D12): desligado por padrão; liga na partida e quando o interruptor muda
        _remote = new RemoteServer(_hub, Settings, UiQueue!, commandLine.CelularLocal, AskPairing);
        // Fase 16, D10: avisos no PC (balão e ícone da bandeja) de celular novo, celular conectado e tela transmitida
        _remote.PhonePaired += name => RunOnUi(() => _tray?.ShowBalloon("Mixer de Áudio", $"Novo celular pareado: {name}"));
        _remote.PhoneConnected += name => RunOnUi(() => _tray?.ShowBalloon("Mixer de Áudio", $"Celular conectado: {name}"));
        _remote.ConnectionsChanged += () => RunOnUi(() => { _phoneOn = _remote?.ConnectionCount > 0; UpdateTrayState(); });
        _remote.ScreenSharingChanged += on => RunOnUi(() =>
        {
            _screenOn = on;
            if (on) _tray?.ShowBalloon("Mixer de Áudio", "A tela do PC está sendo vista no celular");
            UpdateTrayState();
        });
        Settings.Changed += OnSettingsChanged;
        _remote.Apply();

        StartUpdates();
        if (commandLine.Command is not (AppCommand.Default or AppCommand.None)) Execute(commandLine);
        if (!_hub.IsActive) ScheduleTrim();
    }


    /// <summary>
    /// Atualização (Fase 11): cria o controle (um só) e, na cópia instalada e fora do <c>--teste-oculto</c>, conferência
    /// automática 60 s depois de abrir e a cada 6 h (D5, D10). A conferência só procura; baixar e reiniciar são cliques.
    /// </summary>
    private void StartUpdates()
    {
        _updater = new Updater();
        _updates = new UpdateController(_updater, PrepareRestart, RunOnUi);
        _updates.Checked += OnUpdateChecked;
        RememberVersionOnFreshInstall();
        // D10: cópia não instalada ou modo de teste oculto: nada de timer e nada de rede.
        if (_hiddenTest || !_updater.Installed) return;
        _updateTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _updateTimer.Interval = UpdateRules.FirstCheckDelay;
        _updateTimer.IsRepeating = true;
        _updateTimer.Tick += OnUpdateTick;
        _updateTimer.Start();
    }

    private void OnUpdateTick(DispatcherQueueTimer sender, object args)
    {
        sender.Interval = UpdateRules.Interval;
        if (_hiddenTest || _updates is null) return;
        if (UpdateRules.ShouldCheck(DateTime.UtcNow, _updates.LastCheck, Settings.Current.AtualizarAuto))
            _ = _updates.CheckAsync(manual: false);
    }

    /// <summary>Instalação nova (sem config): grava a versão como já vista, para o cartão de novidades não aparecer (D8).</summary>
    private void RememberVersionOnFreshInstall()
    {
        if (!Settings.ExistedOnLoad && string.IsNullOrEmpty(Settings.Current.UltimaVersaoVista))
            Settings.Update(s => s with { UltimaVersaoVista = CurrentVersion });
    }

    /// <summary>Fim de uma verificação: o balão só sai numa verificação automática, uma vez por versão (D6).</summary>
    private void OnUpdateChecked(CheckResult result, bool manual)
    {
        if (result != CheckResult.NewVersion || manual || _updates is null) return;
        var version = _updates.Status.NewVersion;
        if (!UpdateRules.ShouldNotify(Settings.Current.BalaoVersao, version)) return;
        Settings.Update(s => s with { BalaoVersao = version });
        _tray?.ShowBalloon("Mixer de Áudio", $"Versão {version} disponível. Clique no ícone para atualizar.");
        Log.Write($"atualizacao: versao {version} disponivel (balao mostrado)");
    }

    /// <summary>Antes de reiniciar para atualizar (D7): fecha direito, soltando a porta do celular e o ícone.</summary>
    private void PrepareRestart()
    {
        Log.Write("atualizacao: reiniciando para aplicar");
        Shutdown(exitApplication: false);
    }

    /// <summary>
    /// Fase 16, D4 e D9: pergunta no PC se o celular pode parear (a janelinha aparece sem roubar o foco, com som e balão).
    /// Sem resposta em 60 s conta como recusa. Chamada na thread da tela pelo servidor do celular.
    /// </summary>
    private Task<bool> AskPairing(string phone)
    {
        var title = $"Permitir o {phone} a controlar este PC?";
        _tray?.ShowBalloon("Mixer de Áudio", title);
        return Flyout.PcPromptWindow.AskAsync(title,
            "Um celular com o código certo quer parear. Se não foi você, recuse.", "Permitir", "Recusar", 60, resultOnTimeout: false, hidden: _hiddenTest);
    }

    /// <summary>
    /// Fase 16, D13: a janelinha do "agora" da energia ("Desligando em 10 s" e Cancelar), por cima de tudo e sem roubar o foco.
    /// <c>true</c> = o usuário cancelou. Também aparece com <c>--teste-pc</c> (o PC de teste só registra no log).
    /// </summary>
    private Task<bool> AskPowerNow(PowerAction action, int seconds, System.Threading.CancellationToken close) =>
        Flyout.PcPromptWindow.AskAsync("Mixer de Áudio", PowerRules.NowPromptText(action, seconds), "Cancelar", "", seconds,
            resultOnTimeout: false, countdownText: left => PowerRules.NowPromptText(action, left), hidden: _hiddenTest, close: close);

    /// <summary>Fase 16, D10: a tela transmitida vale mais que o celular conectado. Thread da tela.</summary>
    private void UpdateTrayState() =>
        _tray?.SetState(_screenOn ? TrayState.ScreenShared : _phoneOn ? TrayState.PhoneConnected : TrayState.Normal);

    private void OnSettingsChanged()
    {
        _remote?.Apply();
        _pc?.OnSettingsChanged(Settings.Current.FerramentasCelular, Settings.Current.CelularLigado);
    }

    private PcController CreatePc()
    {
        var pc = new PcController(UiQueue!, _testPc, (title, text) => _tray?.ShowBalloon(title, text), AskPowerNow);
        pc.PowerPlanChanged += plan => _tray?.SetCancelItem(plan is null ? null : PowerRules.CancelText(plan.Action), () => RunOnUi(() => _pc?.CancelPower()));
        pc.OnSettingsChanged(Settings.Current.FerramentasCelular, Settings.Current.CelularLigado);
        return pc;
    }

    /// <summary>O primeiro dono chegou: cancela o trim pendente e desliga o modo de eficiência (D1).</summary>
    private void OnHubActive()
    {
        _trimTimer?.Stop();
        TrayIconService.SetEfficiencyMode(false);
    }

    /// <summary>O último dono saiu e os monitores foram soltos: modo de eficiência de volta e trim em 2 s (D1, Fase 1 D17).</summary>
    private void OnHubIdle()
    {
        if (_exiting) return;
        TrayIconService.SetEfficiencyMode(true);
        ScheduleTrim();
    }

    /// <summary>Executa um comando da linha de comando, da primeira cópia ou da redirecionada (D8). Só na thread da tela.</summary>
    public void Execute(CommandLineResult commandLine)
    {
        if (_exiting) return;
        if (commandLine.NoTrim) _noTrim = true;
        if (commandLine.HiddenTest) _hiddenTest = true;
        switch (commandLine.Command)
        {
            case AppCommand.Default:
            case AppCommand.Open:
                Open(activate: true, atCursor: false, checkGate: false);
                break;
            case AppCommand.OpenNoFocus:
                Open(activate: false, atCursor: false, checkGate: false);
                break;
            case AppCommand.Settings:
                Open(activate: true, atCursor: false, checkGate: false, settings: true);
                break;
            case AppCommand.SettingsNoFocus:
                Open(activate: false, atCursor: false, checkGate: false, settings: true);
                break;
            case AppCommand.Close:
                _flyout?.RequestClose("comando");
                break;
            case AppCommand.Exit:
                ExitApp();
                break;
        }
    }

    /// <summary>Clique esquerdo no ícone: abre se fechada, fecha se aberta (D12, com a regra D16).</summary>
    private void Toggle()
    {
        RunOnUi(() =>
        {
            if (_flyout is { IsOpen: true }) _flyout.RequestClose("ícone");
            else Open(activate: true, atCursor: true, checkGate: true);
        });
    }

    private void Open(bool activate, bool atCursor, bool checkGate, bool settings = false)
    {
        RunOnUi(() =>
        {
            if (_exiting) return;
            if (_hiddenTest) activate = false; // modo de teste oculto: nunca ativa a janela
            if (_flyout is { IsOpen: true })
            {
                if (settings) _flyout.ShowSettings();
                if (activate) _flyout.Activate();
                return;
            }
            if (checkGate && !_gate.CanOpen()) return;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            // Fase 8, D1: a janela é criada no primeiro pedido e reaproveitada depois.
            var created = _flyout is null;
            _flyout ??= new FlyoutWindow(_hub, OnFlyoutClosed);
            Log.Write(created ? "janela: criada" : "janela: reaproveitada");
            _flyout.Attach();
            if (settings) _flyout.ShowSettings();
            var rect = ComputePlacement(atCursor, _flyout);
            timer.Stop();
            Log.Write($"sessoes carregadas (apps={_flyout.LoadedApps}, sessoes={_flyout.LoadedSessions}, saidas={_flyout.LoadedDevices}, {timer.ElapsedMilliseconds} ms)");
            Log.Write($"janela: posicao calculada {rect.X},{rect.Y},{rect.Width},{rect.Height}");
            var actual = rect;
            if (_hiddenTest)
            {
                try { actual = HiddenRect(rect); }
                catch (Exception ex) { Log.Write($"ERRO no modo oculto: {ex}"); }
            }
            Log.Write($"janela: apresentando em {actual.X},{actual.Y}");
            _flyout.Present(rect, actual, activate);
            Log.Write("janela aberta");
        });
    }

    private static PixelRect ComputePlacement(bool atCursor, FlyoutWindow flyout)
    {
        DisplayArea area;
        PixelPoint cursor;
        if (atCursor && NativeMethods.GetCursorPos(out var p))
        {
            cursor = new PixelPoint(p.X, p.Y);
            area = DisplayArea.GetFromPoint(new PointInt32(p.X, p.Y), DisplayAreaFallback.Nearest);
        }
        else
        {
            // Sem clique de verdade: o "cursor" é o canto inferior direito da área de trabalho do monitor principal (D19).
            area = DisplayArea.Primary;
            cursor = new PixelPoint(area.WorkArea.X + area.WorkArea.Width, area.WorkArea.Y + area.WorkArea.Height);
        }

        var scale = NativeMethods.GetScaleAt(cursor.X, cursor.Y);
        // Fase 8, D2: a altura vem do conteúdo (limitada pela área útil do monitor).
        var heightDip = flyout.HeightDipFor(area.WorkArea.Height, scale);
        return FlyoutPlacement.Compute(cursor, ToPixelRect(area.OuterBounds), ToPixelRect(area.WorkArea), scale,
            FlyoutWindow.WidthDip, heightDip);
    }

    /// <summary>
    /// Modo de teste oculto: mesmo tamanho, mas abaixo e à esquerda da união de todos os monitores (x = mínimo - 4000), onde
    /// nenhuma tela mostra. A posição calculada de verdade já foi para o log.
    /// </summary>
    private static PixelRect HiddenRect(PixelRect rect)
    {
        var minX = int.MaxValue;
        var maxBottom = int.MinValue;
        var areas = DisplayArea.FindAll();
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            minX = Math.Min(minX, area.OuterBounds.X);
            maxBottom = Math.Max(maxBottom, area.OuterBounds.Y + area.OuterBounds.Height);
        }
        if (minX == int.MaxValue) { minX = 0; maxBottom = 0; }
        return new PixelRect(minX - 4000, maxBottom + 4000, rect.Width, rect.Height);
    }

    private static PixelRect ToPixelRect(RectInt32 r) => new(r.X, r.Y, r.Width, r.Height);

    private void OnFlyoutClosed(string reason, bool lostFocus)
    {
        _gate.NotifyClosed(lostFocus);
        Log.Write($"janela fechada ({reason})");
        // O modo de eficiência e o trim voltam quando o hub fica sem dono (OnHubIdle), não só com a janela fechada.
    }

    /// <summary>2 s depois, com a janela ainda fechada, devolve a memória ao Windows (D17).</summary>
    private void ScheduleTrim()
    {
        if (_noTrim) return;
        _trimTimer?.Start();
    }

    private void OnTrimTick(DispatcherQueueTimer sender, object args)
    {
        if (!_hub.IsActive && !_noTrim) MemoryTrimmer.Trim();
    }

    /// <summary>"Sair" (D9): fecha a janela, remove o ícone da bandeja e encerra.</summary>
    private void ExitApp()
    {
        RunOnUi(() =>
        {
            if (_exiting) return;
            // D7: com versão já baixada, "Sair" agenda a aplicação para o fim do processo.
            _updates?.ApplyOnExit();
            Shutdown(exitApplication: true);
        });
    }

    /// <summary>Fecha a janela, solta o celular e o áudio e remove o ícone; <paramref name="exitApplication"/> encerra o app (na tela).</summary>
    private void Shutdown(bool exitApplication)
    {
        if (_exiting) return;
        _exiting = true;
        Log.Write("saindo");
        _trimTimer?.Stop();
        _updateTimer?.Stop();
        _pc?.Shutdown();
        _remote?.Dispose();
        _flyout?.CloseForReal();
        _flyout = null;
        _hub.ReleaseAll();
        _tray?.Dispose();
        _tray = null;
        if (exitApplication) Application.Current.Exit();
    }

    private static void RunOnUi(Action action)
    {
        var queue = UiQueue;
        if (queue is null || queue.HasThreadAccess) action();
        else queue.TryEnqueue(() => action());
    }
}
