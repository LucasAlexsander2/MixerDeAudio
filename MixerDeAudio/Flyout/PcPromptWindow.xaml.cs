using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MixerDeAudio.Interop;
using MixerDeAudio.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace MixerDeAudio.Flyout;

/// <summary>
/// Janelinha de pergunta do PC (Fase 16, D9): título, texto e dois botões, por cima de tudo, no tema do app, no canto da
/// área de trabalho. Aparece SEM ativar (não rouba o foco de um jogo em tela cheia) e com o som de aviso do Windows. A
/// resposta volta por <see cref="Task"/>: <c>true</c> = o botão principal; <c>false</c> = o secundário, o fechar ou o prazo
/// (conforme <c>resultOnTimeout</c>). Usada pelo pareamento (Permitir/Recusar) e, na 16b, pela energia (Cancelar). Só na
/// thread da tela.
/// </summary>
public sealed partial class PcPromptWindow : Window
{
    private const double WidthDip = 380;
    private const int MarginDip = 16;

    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherQueueTimer _timer;
    private readonly bool _resultOnTimeout;
    private readonly Func<int, string>? _countdown;
    private int _left;
    private bool _done;

    private PcPromptWindow(string title, string text, string primary, string secondary, int timeoutSeconds, bool resultOnTimeout, Func<int, string>? countdownText)
    {
        _resultOnTimeout = resultOnTimeout;
        _countdown = countdownText;
        _left = Math.Max(1, timeoutSeconds);
        InitializeComponent();

        Root.RequestedTheme = App.Settings.Current.Tema switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        TitleText.Text = title;
        BodyText.Text = _countdown?.Invoke(_left) ?? text;
        PrimaryButton.Content = primary;
        SecondaryButton.Content = secondary;
        if (string.IsNullOrEmpty(secondary)) SecondaryButton.Visibility = Visibility.Collapsed; // só um botão (energia: Cancelar)

        Title = "Controladora PC";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        var round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        Closed += (_, _) => Finish(false);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Mostra a pergunta e espera a resposta. <paramref name="timeoutSeconds"/> é o prazo; <paramref name="countdownText"/>
    /// (opcional) monta o texto a cada segundo a partir dos segundos que faltam. <paramref name="hidden"/> (modo de teste
    /// oculto) põe a janela fora de todas as telas e sem som. <paramref name="secondary"/> vazio esconde o segundo botão.
    /// <paramref name="close"/> cancelado fecha a janela com <c>false</c> (o plano acabou ou foi cancelado em outro lugar).
    /// </summary>
    public static Task<bool> AskAsync(string title, string text, string primary, string secondary, int timeoutSeconds,
        bool resultOnTimeout = false, Func<int, string>? countdownText = null, bool hidden = false, CancellationToken close = default)
    {
        var window = new PcPromptWindow(title, text, primary, secondary, timeoutSeconds, resultOnTimeout, countdownText);
        window.Present(hidden);
        if (close.CanBeCanceled)
        {
            var queue = window.DispatcherQueue;
            close.Register(() => queue.TryEnqueue(() => window.Finish(false)));
        }
        return window._result.Task;
    }

    private void Present(bool hidden)
    {
        var area = DisplayArea.Primary;
        var work = area.WorkArea;
        var scale = NativeMethods.GetScaleAt(work.X + work.Width - 1, work.Y + work.Height - 1);
        var width = (int)Math.Round(WidthDip * scale);
        Root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var height = (int)Math.Round(Math.Max(Root.DesiredSize.Height, 120) * scale);
        var margin = (int)Math.Round(MarginDip * scale);
        var x = work.X + work.Width - width - margin;
        var y = work.Y + work.Height - height - margin;
        if (hidden) { x = area.OuterBounds.X - 6000; y = area.OuterBounds.Y + 2000; }
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        AppWindow.Show(false); // sem ativar: o foco continua onde estava
        if (!hidden) NativeMethods.MessageBeep(0x40);
        _timer.Start();
        Log.Write("janelinha: pergunta mostrada");
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        _left--;
        if (_left <= 0) { Finish(_resultOnTimeout); return; }
        if (_countdown is not null) BodyText.Text = _countdown(_left);
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => Finish(true);

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => Finish(false);

    private void Finish(bool answer)
    {
        if (_done) return;
        _done = true;
        _timer.Stop();
        _result.TrySetResult(answer);
        try { Close(); } catch { /* já fechada */ }
    }
}
