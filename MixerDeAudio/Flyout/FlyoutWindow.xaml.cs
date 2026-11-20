using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MixerDeAudio.Audio;
using MixerDeAudio.Interop;
using MixerDeAudio.Services;
using MixerDeAudio.ViewModels;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace MixerDeAudio.Flyout;

/// <summary>
/// A janelinha (D14 e D15): criada a cada abertura e liberada ao fechar. Sem barra de título, sem aparecer na barra
/// de tarefas nem no Alt+Tab, Mica, cantos arredondados.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    public const double WidthDip = 420;
    public const double HeightDip = 480;

    public SessionListViewModel ViewModel { get; }
    public DeviceListViewModel Devices { get; }
    public MasterVolumeViewModel Master { get; }

    /// <summary>Quantidade de apps (linhas) e de sessões lidas ao abrir, para o log (D23).</summary>
    public int LoadedApps => ViewModel.Rows.Count;
    public int LoadedSessions => _tracker.SessionCount;
    public int LoadedDevices => Devices.Buttons.Count;

    private readonly Action<string, bool> _onClosed;
    private string _reason = "sistema";
    private bool _lostFocus;
    private bool _closedRaised;
    private readonly SessionTracker _tracker = new();
    private DeviceMonitor? _deviceMonitor;
    private AudioSessionMonitor? _monitor;
    private AppRoutingService? _routing;

    /// <param name="onClosed">Chamado uma vez, ao fechar, com o motivo e se foi por perda de foco.</param>
    public FlyoutWindow(Action<string, bool> onClosed)
    {
        _onClosed = onClosed;

        // Os monitores de áudio existem só com a janela aberta (D1): criados aqui, soltos no Closed.
        // O DeviceMonitor vem primeiro (dono do enumerador) e recebe o Dispose por último (Fase 3, D1).
        _deviceMonitor = new DeviceMonitor(DispatcherQueue);
        _monitor = new AudioSessionMonitor(DispatcherQueue, _tracker);
        _routing = AppRoutingService.TryCreate(); // Fase 4, D5: uma vez por abertura, na thread da tela
        ViewModel = new SessionListViewModel(_tracker, _deviceMonitor.Tracker, _routing, _monitor.SetVolume, _monitor.SetMute);
        Master = new MasterVolumeViewModel(_deviceMonitor.SetMasterVolume, _deviceMonitor.SetMasterMute);
        InitializeComponent();

        // Ligações feitas pelo código: o x:Bind no nível da janela só é aplicado no Loading, que pode vir tarde demais.
        try { _deviceMonitor.Start(); }
        catch (Exception ex) { Log.Write($"ERRO COM ao listar saídas: {ex}"); }
        Devices = new DeviceListViewModel(_deviceMonitor.Tracker, _deviceMonitor.SetDefaultDevice);
        DeviceList.ItemsSource = Devices.Buttons;
        MasterHost.ItemsSource = new[] { Master };
        Devices.PropertyChanged += OnDevicesChanged;
        UpdateNoDeviceText();
        _deviceMonitor.MasterChanged += Master.Apply;
        _deviceMonitor.DeviceAdded += _monitor.AddDevice;
        _deviceMonitor.DeviceRemoved += _monitor.RemoveDevice;
        // O volume geral já foi lido no Start, antes da ligação acima: aplica o valor atual.
        _deviceMonitor.PublishCurrentMaster();

        SessionList.ItemsSource = ViewModel.Rows;
        ViewModel.PropertyChanged += OnViewModelChanged;
        UpdateEmptyText();
        try { _monitor.Start(_deviceMonitor.Devices); }
        catch (Exception ex) { Log.Write($"ERRO COM ao listar sessões: {ex}"); }

        Title = "Mixer de Áudio";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        // Sem borda: com (true, false) o Windows deixa uma moldura invisível de ~7 px nos lados e embaixo, e a janela
        // fica fora da posição calculada. Os cantos arredondados vêm do DWM (abaixo).
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;

        var hwnd = WindowNative.GetWindowHandle(this);
        var round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        Activated += OnActivated;
        Closed += OnClosed;
        Root.KeyDown += OnKeyDown;
    }

    /// <summary>Posiciona, dimensiona e mostra a janela. Sem <paramref name="activate"/>, não rouba o foco (D18).</summary>
    public void Present(PixelRect rect, bool activate)
    {
        AppWindow.MoveAndResize(new RectInt32(rect.X, rect.Y, rect.Width, rect.Height));
        if (activate)
        {
            Activate();
            Root.Focus(FocusState.Programmatic);
        }
        else
        {
            AppWindow.Show(false);
        }
    }

    /// <summary>Fecha pedindo um motivo para o log e para a regra de alternância.</summary>
    public void RequestClose(string reason, bool lostFocus = false)
    {
        _reason = reason;
        _lostFocus = lostFocus;
        Close();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionListViewModel.EmptyVisibility)) UpdateEmptyText();
    }

    private void UpdateEmptyText() => EmptyText.Visibility = ViewModel.EmptyVisibility;

    private void OnDevicesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceListViewModel.NoDeviceVisibility)) UpdateNoDeviceText();
    }

    private void UpdateNoDeviceText()
    {
        NoDeviceText.Visibility = Devices.NoDeviceVisibility;
        DeviceList.Visibility = Devices.ListVisibility;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            RequestClose("perda de foco", lostFocus: true);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        RequestClose("Esc");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnClosed;
        Root.KeyDown -= OnKeyDown;
        // D1: soltar o áudio antes de avisar o App (que agenda a devolução de memória).
        ViewModel.PropertyChanged -= OnViewModelChanged;
        Devices.PropertyChanged -= OnDevicesChanged;
        if (_deviceMonitor is not null)
        {
            _deviceMonitor.MasterChanged -= Master.Apply;
            if (_monitor is not null)
            {
                _deviceMonitor.DeviceAdded -= _monitor.AddDevice;
                _deviceMonitor.DeviceRemoved -= _monitor.RemoveDevice;
            }
        }
        ViewModel.Dispose();
        Devices.Dispose();
        _routing?.Dispose();
        _routing = null;
        _monitor?.Dispose();
        _monitor = null;
        _deviceMonitor?.Dispose(); // por último: é o dono do enumerador (D1)
        _deviceMonitor = null;
        if (_closedRaised) return;
        _closedRaised = true;
        _onClosed(_reason, _lostFocus);
    }
}
