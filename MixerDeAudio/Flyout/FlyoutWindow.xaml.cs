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
    public const double HeightDip = 520;

    public SessionListViewModel ViewModel { get; }
    public DeviceListViewModel Devices { get; }
    public MasterVolumeViewModel Master { get; }

    /// <summary>Quantidade de apps (linhas) e de sessões lidas ao abrir, para o log (D23).</summary>
    public int LoadedApps => ViewModel.Rows.Count;
    public int LoadedSessions => _hub.Sessions.SessionCount;
    public int LoadedDevices => Devices.Buttons.Count;

    private readonly Action<string, bool> _onClosed;
    private SettingsView? _settingsView;
    private bool _isActive;
    private bool _openedWithoutFocus;
    private bool _userActivated;
    private string _reason = "sistema";
    private bool _lostFocus;
    private bool _closedRaised;
    private readonly AudioHub _hub;
    private const string HubOwner = "janela";

    /// <param name="hub">Núcleo de áudio compartilhado (Fase 6, D4): a janela é um dos donos.</param>
    /// <param name="onClosed">Chamado uma vez, ao fechar, com o motivo e se foi por perda de foco.</param>
    internal FlyoutWindow(AudioHub hub, Action<string, bool> onClosed)
    {
        _hub = hub;
        _onClosed = onClosed;

        // Os monitores de áudio existem enquanto houver dono (D1): a janela pede no construtor e solta no Closed.
        _hub.Acquire(HubOwner);
        ViewModel = new SessionListViewModel(_hub, Favorites);
        Master = new MasterVolumeViewModel(_hub.SetMasterVolume, _hub.SetMasterMute);
        InitializeComponent();

        // Ligações feitas pelo código: o x:Bind no nível da janela só é aplicado no Loading, que pode vir tarde demais.
        Devices = new DeviceListViewModel(_hub.Devices, _hub.SetDefaultDevice, Favorites);
        DeviceList.ItemsSource = Devices.Buttons;
        MasterHost.ItemsSource = new[] { Master };
        Devices.PropertyChanged += OnDevicesChanged;
        UpdateNoDeviceText();
        _hub.MasterChanged += Master.Apply;
        // O volume geral já foi lido pelo hub, antes da ligação acima: aplica o valor atual.
        Master.Apply(_hub.MasterAvailable, _hub.MasterVolume, _hub.MasterMuted);

        SessionList.ItemsSource = ViewModel.Rows;
        ViewModel.PropertyChanged += OnViewModelChanged;
        UpdateEmptyText();

        ApplyTheme();
        App.Settings.Changed += ApplyTheme;

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
            _openedWithoutFocus = true;
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

    /// <summary>Favoritas atuais (D5), lidas da configuração em memória.</summary>
    private static System.Collections.Generic.IReadOnlyCollection<string> Favorites() => App.Settings.Current.Favoritos;

    /// <summary>D10: o tema do conteúdo; a Mica segue o tema do conteúdo.</summary>
    private void ApplyTheme()
    {
        Root.RequestedTheme = App.Settings.Current.Tema switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => ShowSettings();

    /// <summary>Troca a página principal pela de configurações (D9); a página é criada agora.</summary>
    public void ShowSettings()
    {
        if (_settingsView is not null) return;
        _settingsView = new SettingsView(App.Settings, _hub.Devices, App.Remote);
        _settingsView.BackRequested += ShowMain;
        SettingsHost.Children.Add(_settingsView);
        SettingsHost.Visibility = Visibility.Visible;
        MainPage.Visibility = Visibility.Collapsed;
        Footer.Visibility = Visibility.Collapsed;
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Activated -= OnActivated;
        Closed -= OnClosed;
        Root.KeyDown -= OnKeyDown;
        App.Settings.Changed -= ApplyTheme;
        ReleaseSettings();
        // D1: soltar o áudio antes de avisar o App (que agenda a devolução de memória).
        ViewModel.PropertyChanged -= OnViewModelChanged;
        Devices.PropertyChanged -= OnDevicesChanged;
        _hub.MasterChanged -= Master.Apply;
        ViewModel.Dispose();
        Devices.Dispose();
        _hub.Release(HubOwner); // o último dono solta tudo, na mesma ordem de antes (Fase 6, D1)
        if (_closedRaised) return;
        _closedRaised = true;
        _onClosed(_reason, _lostFocus);
    }
}
