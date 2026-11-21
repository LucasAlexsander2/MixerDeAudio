using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MixerDeAudio.Audio;
using MixerDeAudio.Flyout;
using MixerDeAudio.Interop;
using MixerDeAudio.Remote;
using MixerDeAudio.Services;
using MixerDeAudio.Tray;
using Windows.Graphics;

namespace MixerDeAudio;

/// <summary>
/// O app não tem janela principal (D7): só o ícone da bandeja. A janelinha é criada ao abrir e liberada ao fechar.
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
    private bool _exiting;

    /// <summary>Fila da thread da tela, para receber a ativação redirecionada (que chega em outra thread).</summary>
    public static DispatcherQueue? UiQueue { get; private set; }

    public static App? Instance { get; private set; }

    /// <summary>Servidor do celular (Fase 6), para a seção "Controle pelo celular" das Configurações.</summary>
    internal static RemoteServer? Remote => Instance?._remote;

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
        _remote = new RemoteServer(_hub, Settings, UiQueue!, commandLine.CelularLocal);
        Settings.Changed += OnSettingsChanged;
        _remote.Apply();
        if (commandLine.Command is not (AppCommand.Default or AppCommand.None)) Execute(commandLine);
        if (!_hub.IsActive) ScheduleTrim();
    }

    private void OnSettingsChanged() => _remote?.Apply();

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
            if (_flyout is not null) _flyout.RequestClose("ícone");
            else Open(activate: true, atCursor: true, checkGate: true);
        });
    }

    private void Open(bool activate, bool atCursor, bool checkGate, bool settings = false)
    {
        RunOnUi(() =>
        {
            if (_exiting) return;
            if (_hiddenTest) activate = false; // modo de teste oculto: nunca ativa a janela
            if (_flyout is not null)
            {
                if (settings) _flyout.ShowSettings();
                if (activate) _flyout.Activate();
                return;
            }
            if (checkGate && !_gate.CanOpen()) return;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var rect = ComputePlacement(atCursor);
            _flyout = new FlyoutWindow(_hub, OnFlyoutClosed);
            if (settings) _flyout.ShowSettings();
            timer.Stop();
            Log.Write($"sessoes carregadas (apps={_flyout.LoadedApps}, sessoes={_flyout.LoadedSessions}, saidas={_flyout.LoadedDevices}, {timer.ElapsedMilliseconds} ms)");
            Log.Write($"janela: posicao calculada {rect.X},{rect.Y},{rect.Width},{rect.Height}");
            if (_hiddenTest)
            {
                try { rect = HiddenRect(rect); }
                catch (Exception ex) { Log.Write($"ERRO no modo oculto: {ex}"); }
            }
            Log.Write($"janela: apresentando em {rect.X},{rect.Y}");
            _flyout.Present(rect, activate);
            Log.Write("janela aberta");
        });
    }

    private static PixelRect ComputePlacement(bool atCursor)
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
        return FlyoutPlacement.Compute(cursor, ToPixelRect(area.OuterBounds), ToPixelRect(area.WorkArea), scale,
            FlyoutWindow.WidthDip, FlyoutWindow.HeightDip);
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
        _flyout = null;
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
            _exiting = true;
            Log.Write("saindo");
            _trimTimer?.Stop();
            _remote?.Dispose();
            _flyout?.RequestClose("saindo");
            _hub.ReleaseAll();
            _tray?.Dispose();
            _tray = null;
            Application.Current.Exit();
        });
    }

    private static void RunOnUi(Action action)
    {
        var queue = UiQueue;
        if (queue is null || queue.HasThreadAccess) action();
        else queue.TryEnqueue(() => action());
    }
}
