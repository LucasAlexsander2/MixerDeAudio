using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MixerDeAudio.Interop;
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

    private readonly Action<string, bool> _onClosed;
    private string _reason = "sistema";
    private bool _lostFocus;
    private bool _closedRaised;

    /// <param name="onClosed">Chamado uma vez, ao fechar, com o motivo e se foi por perda de foco.</param>
    public FlyoutWindow(Action<string, bool> onClosed)
    {
        _onClosed = onClosed;
        InitializeComponent();

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
        if (_closedRaised) return;
        _closedRaised = true;
        _onClosed(_reason, _lostFocus);
    }
}
