using System;
using Microsoft.UI.Dispatching;
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
/// A janelinha (D14 e D15; Fase 8, D1): criada no primeiro clique e só escondida ao fechar. Escondida, não guarda nada do
/// áudio: o núcleo é liberado e as listas esvaziadas. Sem barra de título, sem aparecer na barra de tarefas nem no
/// Alt+Tab, Acrylic, cantos arredondados, altura que acompanha o conteúdo.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    public const double WidthDip = FlyoutSize.WidthDip;
    private static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);

    public SessionListViewModel ViewModel { get; }
    public DeviceListViewModel Devices { get; }
    public MasterVolumeViewModel Master { get; }

    /// <summary>Quantidade de apps (linhas) e de sessões lidas ao abrir, para o log (D23).</summary>
    public int LoadedApps => ViewModel.Rows.Count;
    public int LoadedSessions => _hub.Sessions.SessionCount;
    public int LoadedDevices => Devices.Buttons.Count;

    /// <summary>Mostrada (ou em abertura): o núcleo de áudio tem a janela como dona.</summary>
    public bool IsOpen => _open;

    private readonly Action<string, bool> _onClosed;
    private SettingsView? _settingsView;
    private bool _open;
    private bool _presented;
    private bool _realClose;
    private bool _isActive;
    private bool _openedWithoutFocus;
    private bool _userActivated;
    private string _reason = "sistema";
    private bool _lostFocus;
    private readonly AudioHub _hub;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _resizeTimer;
    private PixelRect _logicalRect;
    private PixelRect _actualRect;
    private double _workHeightPx = 1000;
    private double _scale = 1;
    private const string HubOwner = "janela";

    /// <param name="hub">Núcleo de áudio compartilhado (Fase 6, D4): a janela é um dos donos enquanto aberta.</param>
    /// <param name="onClosed">Chamado a cada vez que a janela é escondida, com o motivo e se foi por perda de foco.</param>
    internal FlyoutWindow(AudioHub hub, Action<string, bool> onClosed)
    {
        _hub = hub;
        _onClosed = onClosed;

        // Os monitores de áudio existem enquanto houver dono (D1): a janela pede em Attach e solta ao esconder.
        ViewModel = new SessionListViewModel(_hub, Favorites);
        Master = new MasterVolumeViewModel(_hub.SetMasterVolume, _hub.SetMasterMute);
        InitializeComponent();

        // Ligações feitas pelo código: o x:Bind no nível da janela só é aplicado no Loading, que pode vir tarde demais.
        Devices = new DeviceListViewModel(() => _hub.Devices, _hub.SetDefaultDevice, Favorites);
        DeviceList.ItemsSource = Devices.Buttons;
        MasterHost.ItemsSource = new[] { Master };
        Devices.PropertyChanged += OnDevicesChanged;
        UpdateNoDeviceText();
        SessionList.ItemsSource = ViewModel.Rows;
        ViewModel.PropertyChanged += OnViewModelChanged;
        UpdateEmptyText();

        _resizeTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _resizeTimer.Interval = ResizeDelay;
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick += (_, _) => ApplyHeight();

        Title = "Controladora PC";
        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(); // Fase 8, D4
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
        AppWindow.Closing += OnAppWindowClosing;
        Root.KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Prepara a abertura (D1): a janela vira dona do núcleo, as view models leem o estado e ligam os eventos, e o tema é
    /// reaplicado. Ainda não mostra nada.
    /// </summary>
    public void Attach()
    {
        if (_open) return;
        _open = true;
        _hub.Acquire(HubOwner);
        ViewModel.Attach();
        Devices.Attach();
        ViewModel.LayoutChanged += OnLayoutChanged;
        _hub.MasterChanged += Master.Apply;
        // O volume geral já foi lido pelo hub, antes da ligação acima: aplica o valor atual.
        Master.Apply(_hub.MasterAvailable, _hub.MasterVolume, _hub.MasterMuted);
        UpdateEmptyText();
        UpdateNoDeviceText();
        ApplyTheme();
        App.Settings.Changed += ApplyTheme;
        if (App.Updates is { } updates) updates.Changed += RefreshBanner;
        RefreshBanner();
        _isActive = false;
    }

    /// <summary>
    /// Altura da janela em DIP para o conteúdo atual (D2): a do <c>Measure</c> com 420 DIP de largura, entre o mínimo e o
    /// máximo; a página de configurações usa o máximo. Guarda a área útil e a escala para os ajustes seguintes.
    /// </summary>
    public double HeightDipFor(double workAreaHeightPx, double scale)
    {
        _workHeightPx = workAreaHeightPx;
        _scale = scale;
        return TargetHeightDip();
    }

    private double TargetHeightDip()
    {
        if (_settingsView is not null) return FlyoutSize.Max(_workHeightPx, _scale);
        Root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var desired = Root.DesiredSize.Height;
        var final = FlyoutSize.Height(desired, _workHeightPx, _scale);
        Log.Write($"janela: altura desejada {desired:0} DIP, final {final:0} DIP");
        return final;
    }

    /// <summary>
    /// Posiciona, dimensiona e mostra a janela. <paramref name="logical"/> é a posição calculada; <paramref name="actual"/>
    /// é onde a janela fica de fato (diferente no modo de teste oculto). Sem <paramref name="activate"/>, não rouba o foco (D18).
    /// </summary>
    public void Present(PixelRect logical, PixelRect actual, bool activate)
    {
        _logicalRect = logical;
        _actualRect = actual;
        _presented = true;
        _openedWithoutFocus = !activate;
        _userActivated = false;
        AppWindow.MoveAndResize(new RectInt32(actual.X, actual.Y, actual.Width, actual.Height));
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

    /// <summary>Esconde pedindo um motivo para o log e para a regra de alternância (D1). Sem efeito se já está escondida.</summary>
    public void RequestClose(string reason, bool lostFocus = false)
    {
        if (!_open) return;
        _reason = reason;
        _lostFocus = lostFocus;
        Hide();
    }

    /// <summary>Fecha de verdade (só ao sair do app).</summary>
    public void CloseForReal()
    {
        RequestClose("saindo");
        _realClose = true;
        Close();
    }

    private void Hide()
    {
        _open = false;
        _presented = false;
        _resizeTimer.Stop();
        AppWindow.Hide();
        _isActive = false;
        App.Settings.Changed -= ApplyTheme;
        if (App.Updates is { } updates) updates.Changed -= RefreshBanner;
        ViewModel.LayoutChanged -= OnLayoutChanged;
        _hub.MasterChanged -= Master.Apply;
        ReleaseSettings();
        // A página principal volta a ser a visível, para a próxima abertura.
        MainPage.Visibility = Visibility.Visible;
        Footer.Visibility = Visibility.Visible;
        // D1: soltar o áudio antes de avisar o App (que agenda a devolução de memória).
        ViewModel.Detach();
        Devices.Detach();
        SessionScroll.ChangeView(0, 0, null, true);
        _hub.Release(HubOwner); // o último dono solta tudo, na mesma ordem de antes (Fase 6, D1)
        _onClosed(_reason, _lostFocus);
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_realClose) return;
        args.Cancel = true; // Alt+F4 etc.: só esconde
        RequestClose("fechar");
    }

    private void OnLayoutChanged()
    {
        if (!_presented) return;
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    /// <summary>Refaz a altura (D2): mantém a borda de baixo, então a janela cresce para cima.</summary>
    private void ApplyHeight()
    {
        if (!_open || !_presented) return;
        var height = (int)Math.Round(TargetHeightDip() * _scale);
        if (height == _actualRect.Height) return;
        _actualRect = new PixelRect(_actualRect.X, _actualRect.Bottom - height, _actualRect.Width, height);
        _logicalRect = new PixelRect(_logicalRect.X, _logicalRect.Bottom - height, _logicalRect.Width, height);
        AppWindow.MoveAndResize(new RectInt32(_actualRect.X, _actualRect.Y, _actualRect.Width, _actualRect.Height));
        Log.Write($"janela: posicao calculada {_logicalRect.X},{_logicalRect.Y},{_logicalRect.Width},{_logicalRect.Height}");
    }

    /// <summary>Favoritas atuais (D5), lidas da configuração em memória.</summary>
    private static System.Collections.Generic.IReadOnlyCollection<string> Favorites() => App.Settings.Current.Favoritos;

    /// <summary>D10: o tema do conteúdo; o Acrylic segue o tema do conteúdo.</summary>
    private void ApplyTheme()
    {
        Root.RequestedTheme = App.Settings.Current.Tema switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    // ---- Faixa de atualização (Fase 11, D6) ----

    /// <summary>Mostra ou esconde a faixa conforme o estado da atualização; a altura da janela acompanha.</summary>
    private void RefreshBanner()
    {
        var status = App.Updates?.Status;
        var info = status is null ? new BannerInfo(false, "", null) : UpdateRules.Banner(status);
        var visibility = info.Visible ? Visibility.Visible : Visibility.Collapsed;
        var changed = UpdateBanner.Visibility != visibility;
        UpdateBanner.Visibility = visibility;
        UpdateBannerText.Text = info.Text;
        UpdateBannerButton.Content = info.Button ?? "";
        UpdateBannerButton.Visibility = info.Button is null ? Visibility.Collapsed : Visibility.Visible;
        if (changed) OnLayoutChanged();
    }

    private void OnUpdateBannerClick(object sender, RoutedEventArgs e)
    {
        var updates = App.Updates;
        if (updates is null) return;
        if (updates.Status.State == UpdateState.NewVersion) _ = updates.DownloadAsync();
        else if (updates.Status.State == UpdateState.Ready) updates.Restart();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>Troca a página principal pela de configurações (D9); a página é criada agora.</summary>
    public void ShowSettings()
    {
        if (!_open || _settingsView is not null) return;
        _settingsView = new SettingsView(App.Settings, _hub.Devices, App.Remote);
        _settingsView.BackRequested += ShowMain;
        SettingsHost.Children.Add(_settingsView);
        SettingsHost.Visibility = Visibility.Visible;
        MainPage.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
        ApplyHeight();
        FocusIfActive();
    }

    /// <summary>Volta à página principal, solta a de configurações e refaz o topo e as linhas (D7).</summary>
    public void ShowMain()
    {
        if (_settingsView is null) return;
        ReleaseSettings();
        MainPage.Visibility = Visibility.Visible;
        Footer.Visibility = Visibility.Visible;
        Devices.Refresh();
        ViewModel.RefreshRoutes();
        UpdateNoDeviceText();
        ApplyHeight();
        FocusIfActive();
    }

    /// <summary>
    /// Devolve o foco ao conteúdo (para o Esc continuar chegando) só quando a janela está ativa: com a janela aberta sem
    /// foco (roteiros), pedir foco aqui a ativaria e ela fecharia por perda de foco.
    /// </summary>
    private void FocusIfActive()
    {
        if (_isActive) Root.Focus(FocusState.Programmatic);
    }

    private void ReleaseSettings()
    {
        if (_settingsView is null) return;
        _settingsView.BackRequested -= ShowMain;
        SettingsHost.Children.Clear();
        SettingsHost.Visibility = Visibility.Collapsed;
        _settingsView.Release();
        _settingsView = null;
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
        if (!_open) return;
        _isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (args.WindowActivationState == WindowActivationState.PointerActivated) _userActivated = true;
        // Aberta sem foco (roteiros de teste): um aviso de ativação por código (o Invoke da UI Automation num botão faz
        // isso) seguido de "desativada" não é o usuário saindo da janela; só vale depois de um clique de verdade.
        if (args.WindowActivationState == WindowActivationState.Deactivated && (!_openedWithoutFocus || _userActivated))
            RequestClose("perda de foco", lostFocus: true);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        // Na página de configurações, Esc volta à página principal (D9); na principal, fecha.
        if (_settingsView is not null) ShowMain();
        else RequestClose("Esc");
    }
}
