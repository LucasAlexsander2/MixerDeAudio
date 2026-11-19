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
    public const double WidthDip = 360;
    public const double HeightDip = 480;

    public SessionListViewModel ViewModel { get; }

    /// <summary>Quantidade de apps (linhas) e de sessões lidas ao abrir, para o log (D23).</summary>
    public int LoadedApps => ViewModel.Rows.Count;
    public int LoadedSessions => _tracker.SessionCount;

    private readonly Action<string, bool> _onClosed;
    private string _reason = "sistema";
    private bool _lostFocus;
    private bool _closedRaised;
    private readonly SessionTracker _tracker = new();
    private AudioSessionMonitor? _monitor;

    /// <param name="onClosed">Chamado uma vez, ao fechar, com o motivo e se foi por perda de foco.</param>
    public FlyoutWindow(Action<string, bool> onClosed)
    {
        _onClosed = onClosed;

        // O monitor de áudio existe só com a janela aberta (D1): criado aqui, solto no Closed.
        _monitor = new AudioSessionMonitor(DispatcherQueue, _tracker);
        ViewModel = new SessionListViewModel(_tracker, _monitor.SetVolume, _monitor.SetMute);
        InitializeComponent();

        // Ligações feitas pelo código: o x:Bind no nível da janela só é aplicado no Loading, que pode vir tarde demais.
        SessionList.ItemsSource = ViewModel.Rows;
        ViewModel.PropertyChanged += OnViewModelChanged;
        UpdateEmptyText();
        try { _monitor.Start(); }
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
        ViewModel.Dispose();
        _monitor?.Dispose();
        _monitor = null;
        if (_closedRaised) return;
        _closedRaised = true;
        _onClosed(_reason, _lostFocus);
    }
}
