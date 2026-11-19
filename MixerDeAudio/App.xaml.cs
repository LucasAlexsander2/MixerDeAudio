using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MixerDeAudio.Flyout;
using MixerDeAudio.Interop;
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
    private bool _noTrim;
    private bool _exiting;

    /// <summary>Fila da thread da tela, para receber a ativação redirecionada (que chega em outra thread).</summary>
    public static DispatcherQueue? UiQueue { get; private set; }

    public static App? Instance { get; private set; }

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
        _tray = new TrayIconService(onToggle: Toggle, onOpen: () => Open(activate: true, atCursor: true, checkGate: false), onExit: ExitApp);
        TrayIconService.SetEfficiencyMode(true);

        _trimTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _trimTimer.Interval = TrimDelay;
        _trimTimer.IsRepeating = false;
        _trimTimer.Tick += OnTrimTick;

        // Primeira cópia: sem argumento só cria o ícone; com argumento, executa o comando (D8).
        var commandLine = CommandLine.Parse(Program.StartupArguments);
        _noTrim = commandLine.NoTrim;
        if (commandLine.Command is not (AppCommand.Default or AppCommand.None)) Execute(commandLine);
        if (_flyout is null) ScheduleTrim();
    }

    /// <summary>Executa um comando da linha de comando, da primeira cópia ou da redirecionada (D8). Só na thread da tela.</summary>
    public void Execute(CommandLineResult commandLine)
    {
        if (_exiting) return;
        if (commandLine.NoTrim) _noTrim = true;
        switch (commandLine.Command)
        {
            case AppCommand.Default:
            case AppCommand.Open:
                Open(activate: true, atCursor: false, checkGate: false);
                break;
            case AppCommand.OpenNoFocus:
                Open(activate: false, atCursor: false, checkGate: false);
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

    private void Open(bool activate, bool atCursor, bool checkGate)
    {
        RunOnUi(() =>
        {
            if (_exiting) return;
            if (_flyout is not null)
            {
                if (activate) _flyout.Activate();
                return;
            }
            if (checkGate && !_gate.CanOpen()) return;

            _trimTimer?.Stop();
            TrayIconService.SetEfficiencyMode(false);

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var rect = ComputePlacement(atCursor);
            _flyout = new FlyoutWindow(OnFlyoutClosed);
            timer.Stop();
            Log.Write($"sessoes carregadas (apps={_flyout.LoadedApps}, sessoes={_flyout.LoadedSessions}, saidas={_flyout.LoadedDevices}, {timer.ElapsedMilliseconds} ms)");
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

    private static PixelRect ToPixelRect(RectInt32 r) => new(r.X, r.Y, r.Width, r.Height);

    private void OnFlyoutClosed(string reason, bool lostFocus)
    {
        _flyout = null;
        _gate.NotifyClosed(lostFocus);
        Log.Write($"janela fechada ({reason})");
        if (_exiting) return;
        TrayIconService.SetEfficiencyMode(true);
        ScheduleTrim();
    }

    /// <summary>2 s depois, com a janela ainda fechada, devolve a memória ao Windows (D17).</summary>
    private void ScheduleTrim()
    {
        if (_noTrim) return;
        _trimTimer?.Start();
    }

    private void OnTrimTick(DispatcherQueueTimer sender, object args)
    {
        if (_flyout is null && !_noTrim) MemoryTrimmer.Trim();
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
            _flyout?.RequestClose("saindo");
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
