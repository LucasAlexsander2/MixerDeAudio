using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MixerDeAudio.Audio;
using MixerDeAudio.Remote;
using QRCoder;
using Windows.Storage.Streams;
using MixerDeAudio.Services;

namespace MixerDeAudio.ViewModels;

/// <summary>Um item do cartão "Novidades" (Fase 11, D8): o ícone (glifo da Segoe Fluent), o título e o texto.</summary>
public sealed record NoteItemView(string Glyph, string Title, string Text);

/// <summary>Uma saída na lista de favoritas da página de configurações (D9).</summary>
public sealed class FavoriteItemViewModel : ObservableObject
{
    private readonly Action<FavoriteItemViewModel> _changed;
    private bool _isChecked;
    private bool _isEnabled = true;

    public FavoriteItemViewModel(string id, string shortName, string fullName, bool connected, bool isChecked, Action<FavoriteItemViewModel> changed)
    {
        Id = id;
        ShortName = shortName;
        Connected = connected;
        // Favorita desligada: o último nome salvo e "(desconectada)" (D9).
        Label = connected ? shortName : $"{shortName} (desconectada)";
        FullName = connected ? fullName : Label;
        _isChecked = isChecked;
        _changed = changed;
    }

    public string Id { get; }
    public string ShortName { get; }
    public string Label { get; }
    public string FullName { get; }
    public bool Connected { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            _changed(this);
        }
    }

    /// <summary>Com 3 marcadas, as outras ficam desabilitadas (a marcada sempre pode ser desmarcada).</summary>
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
}

/// <summary>
/// Página de configurações (D9): favoritas, tema e iniciar com o Windows. Toda mudança vale na hora, sem botão
/// "Salvar". Criada só quando a página abre e solta ao voltar.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly DeviceTracker _devices;
    private bool _rebuilding;
    private readonly RemoteServer? _remote;
    private readonly UpdateController? _updates;
    private readonly string _currentVersion;
    private readonly DispatcherQueue? _ui;
    private bool _startup;
    private bool _disposed;
    private Visibility _remoteVisibility = Visibility.Collapsed;
    private Visibility _remoteDisconnectVisibility = Visibility.Collapsed;
    private string _remoteAddress = "";
    private string _remoteCode = "";
    private string _remoteCount = "";
    private ImageSource? _remoteQr;
    private string? _qrUrl;

    internal SettingsViewModel(SettingsStore store, DeviceTracker devices, RemoteServer? remote = null,
        UpdateController? updates = null, string currentVersion = "1.1.0")
    {
        _store = store;
        _devices = devices;
        _remote = remote;
        _updates = updates;
        _currentVersion = currentVersion;
        _ui = DispatcherQueue.GetForCurrentThread();
        _startup = StartupRegistration.IsEnabled();
        RebuildFavorites();
        _devices.Changed += OnDevicesChanged;
        if (_remote is not null)
        {
            _remote.ConnectionsChanged += OnConnectionsChanged;
            _remote.StateChanged += OnRemoteStateChanged;
        }
        RefreshRemote();
        if (_updates is not null) _updates.Changed += OnUpdateChanged;
        LoadNotes();
    }

    public ObservableCollection<FavoriteItemViewModel> Favorites { get; } = new();

    // ---- Favoritas ----

    private void OnDevicesChanged(DeviceChange change) => RebuildFavorites();

    private void RebuildFavorites()
    {
        _rebuilding = true;
        try
        {
            var settings = _store.Current;
            Favorites.Clear();
            foreach (var d in _devices.Devices)
            {
                Favorites.Add(new FavoriteItemViewModel(d.Id, d.ShortName, d.FullName, true,
                    settings.Favoritos.Contains(d.Id, StringComparer.Ordinal), OnFavoriteChanged));
            }
            foreach (var id in settings.Favoritos)
            {
                if (_devices.Devices.Any(d => string.Equals(d.Id, id, StringComparison.Ordinal))) continue;
                var name = settings.NomesFavoritos.TryGetValue(id, out var n) ? n : DeviceInfo.UnknownName;
                Favorites.Add(new FavoriteItemViewModel(id, name, name, false, true, OnFavoriteChanged));
            }
            UpdateEnabled();
        }
        finally { _rebuilding = false; }
    }

    private void UpdateEnabled()
    {
        var full = Favorites.Count(f => f.IsChecked) >= AppSettings.MaxFavorites;
        foreach (var f in Favorites) f.IsEnabled = f.IsChecked || !full;
    }

    private void OnFavoriteChanged(FavoriteItemViewModel item)
    {
        if (_rebuilding) return;
        var ids = Favorites.Where(f => f.IsChecked).Select(f => f.Id).ToList();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Favorites.Where(f => f.IsChecked)) names[f.Id] = f.ShortName;
        _store.Update(s => s with { Favoritos = ids, NomesFavoritos = names });
        UpdateEnabled();
        Log.Write($"favoritos alterados ({_store.Current.Favoritos.Count})");
    }

    // ---- Tema ----

    public bool IsThemeAuto { get => Theme == AppTheme.Auto; set { if (value) SetTheme(AppTheme.Auto); } }
    public bool IsThemeLight { get => Theme == AppTheme.Light; set { if (value) SetTheme(AppTheme.Light); } }
    public bool IsThemeDark { get => Theme == AppTheme.Dark; set { if (value) SetTheme(AppTheme.Dark); } }

    private string Theme => _store.Current.Tema;

    private void SetTheme(string theme)
    {
        if (Theme == theme) return;
        _store.Update(s => s with { Tema = theme });
        Log.Write($"tema alterado para {theme}");
        OnPropertyChanged(nameof(IsThemeAuto));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
    }

    // ---- Iniciar com o Windows ----

    public bool StartupEnabled
    {
        get => _startup;
        set
        {
            if (value == _startup) return;
            var ok = StartupRegistration.SetEnabled(value);
            // A fonte da verdade é o registro: relê o estado real depois de gravar.
            _startup = ok ? StartupRegistration.IsEnabled() : !value;
            if (ok) Log.Write(_startup ? "iniciar com o Windows ligado" : "iniciar com o Windows desligado");
            OnPropertyChanged();
        }
    }

    // ---- Atualização (Fase 11, D5 a D8) ----

    public ObservableCollection<NoteItemView> NoteItems { get; } = new();
    public Visibility NotesVisibility { get; private set; } = Visibility.Collapsed;
    public string NotesTitle { get; private set; } = "";

    /// <summary>"Versão 1.1.0" da seção Atualização.</summary>
    public string UpdateVersionText => $"Versão {_currentVersion}";

    public string UpdateStatusText => _updates is null
        ? "Atualização indisponível nesta cópia (não instalada)."
        : UpdateRules.StatusText(_updates.Status, _updates.LastCheck is not null);

    public bool UpdateAvailableHere => _updates is not null && _updates.Status.State != UpdateState.Unavailable;

    /// <summary>"Procurar agora" funciona mesmo com a conferência automática desligada (D5).</summary>
    public bool CanCheckNow => UpdateAvailableHere && _updates!.Status.State is UpdateState.Idle or UpdateState.Error or UpdateState.NewVersion;

    public Visibility CheckVisibility => UpdateAvailableHere ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DownloadVisibility => _updates?.Status.State == UpdateState.NewVersion ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RestartVisibility => _updates?.Status.State == UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProgressVisibility => _updates?.Status.State == UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
    public double ProgressValue => _updates?.Status.Percent ?? 0;

    public bool AutoUpdate
    {
        get => _store.Current.AtualizarAuto;
        set
        {
            if (value == _store.Current.AtualizarAuto) return;
            _store.Update(s => s with { AtualizarAuto = value });
            Log.Write(value ? "atualizacao: conferencia automatica ligada" : "atualizacao: conferencia automatica desligada");
            OnPropertyChanged();
        }
    }

    public void CheckNow() => _ = _updates?.CheckAsync(manual: true);

    public void DownloadUpdate() => _ = _updates?.DownloadAsync();

    public void RestartToUpdate() => _updates?.Restart();

    private void OnUpdateChanged()
    {
        if (_disposed) return;
        foreach (var name in new[]
        {
            nameof(UpdateStatusText), nameof(CanCheckNow), nameof(CheckVisibility), nameof(DownloadVisibility),
            nameof(RestartVisibility), nameof(ProgressVisibility), nameof(ProgressValue),
        }) OnPropertyChanged(name);
    }

    /// <summary>Cartão "Novidades da versão X" (D8): uma vez, quando a versão atual é maior que a última vista; grava a versão vista.</summary>
    private void LoadNotes()
    {
        var settings = _store.Current;
        if (!UpdateRules.ShouldShowNotes(settings.UltimaVersaoVista, _currentVersion, _store.ExistedOnLoad)) return;
        var notes = UpdateRules.NotesFor(NotesResource.Load(), _currentVersion);
        if (notes is { Itens.Count: > 0 })
        {
            foreach (var item in notes.Itens)
                NoteItems.Add(new NoteItemView(GlyphFrom(item.Icone), item.Titulo, item.Texto));
            NotesTitle = $"Novidades da versão {_currentVersion}";
            NotesVisibility = Visibility.Visible;
        }
        var version = _currentVersion;
        _store.Update(s => s with { UltimaVersaoVista = version });
        Log.Write($"novidades: cartao da versao {version} {(NotesVisibility == Visibility.Visible ? "mostrado" : "sem entrada")}");
    }

    private static string GlyphFrom(string hex) =>
        int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var code)
            ? char.ConvertFromUtf32(code)
            : "";

    // ---- Controle pelo celular (Fase 6, D17 e D18) ----

    /// <summary>Rodapé das Configurações (Fase 8, D15), lido do assembly.</summary>
    public string VersionLabel => $"Versão {_currentVersion}";

    /// <summary>A seção só aparece quando o servidor existe (sempre, no app).</summary>
    public Visibility RemoteAvailableVisibility => _remote is not null ? Visibility.Visible : Visibility.Collapsed;

    public bool RemoteEnabled
    {
        get => _store.Current.CelularLigado;
        set
        {
            if (value == _store.Current.CelularLigado) return;
            // O servidor liga ou desliga na hora pelo evento Changed da configuração (D10)
            _store.Update(s => s with { CelularLigado = value });
            Log.Write(value ? "celular: recurso ligado nas configuracoes" : "celular: recurso desligado nas configuracoes");
            RefreshRemote();
            OnPropertyChanged();
        }
    }

    public Visibility RemoteVisibility { get => _remoteVisibility; private set => SetProperty(ref _remoteVisibility, value); }
    public string RemoteAddress { get => _remoteAddress; private set => SetProperty(ref _remoteAddress, value); }
    public string RemoteCode { get => _remoteCode; private set => SetProperty(ref _remoteCode, value); }
    public string RemoteCount { get => _remoteCount; private set => SetProperty(ref _remoteCount, value); }
    public ImageSource? RemoteQr { get => _remoteQr; private set => SetProperty(ref _remoteQr, value); }

    /// <summary>"Gerar novo código": troca o código, esquece todos os celulares e desconecta quem estava conectado.</summary>
    public void NewCode()
    {
        _remote?.RegenerateCode();
        RefreshRemote();
    }

    private void RefreshRemote()
    {
        if (_remote is null) return;
        var settings = _store.Current;
        RemoteVisibility = settings.CelularLigado ? Visibility.Visible : Visibility.Collapsed;
        if (!settings.CelularLigado)
        {
            _qrUrl = null;
            RemoteQr = null;
            return;
        }
        RemoteCode = settings.CodigoCelular ?? "";
        UpdateCount();
        if (!_remote.IsRunning || string.IsNullOrEmpty(settings.CodigoCelular))
        {
            RemoteAddress = _remote.IsStarting
                ? "Ligando o servidor… (esperando a porta ficar livre)"
                : "Servidor não iniciou (sem porta livre de 8750 a 8759)";
            _qrUrl = null;
            RemoteQr = null;
            return;
        }
        var ip = LocalAddress();
        RemoteAddress = RemoteRules.BuildAddress(ip, _remote.Port);
        // A imagem só é gerada quando a seção é mostrada e o endereço ou o código mudaram (D18)
        var url = RemoteRules.BuildPairingUrl(ip, _remote.Port, settings.CodigoCelular!);
        if (url != _qrUrl)
        {
            _qrUrl = url;
            _ = BuildQrAsync(url);
        }
    }

    private async Task BuildQrAsync(string url)
    {
        try
        {
            byte[] png;
            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M))
                png = new PngByteQRCode(data).GetGraphic(10);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            if (!_disposed && url == _qrUrl) RemoteQr = bitmap;
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: QR code: {ex.Message}");
        }
    }

    /// <summary>"Conectado: Android" ou "Nenhum celular conectado" (Fase 9, D4); o botão Desconectar só aparece com alguém conectado.</summary>
    private void UpdateCount()
    {
        var name = _remote?.ConnectedPhoneName;
        RemoteCount = name is null ? "Nenhum celular conectado" : $"Conectado: {(string.IsNullOrWhiteSpace(name) ? "celular" : name)}";
        RemoteDisconnectVisibility = name is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public Visibility RemoteDisconnectVisibility { get => _remoteDisconnectVisibility; private set => SetProperty(ref _remoteDisconnectVisibility, value); }

    /// <summary>"Desconectar": o celular vê "Desconectado"; a chave continua pareada.</summary>
    public void Disconnect() => _remote?.DisconnectAll();

    // ---- Ferramentas do PC (Fase 9, D2) ----

    public bool ToolPower { get => IsTool(RemoteTools.Power); set => SetTool(RemoteTools.Power, value); }
    public bool ToolDisplay { get => IsTool(RemoteTools.Display); set => SetTool(RemoteTools.Display, value); }
    public bool ToolInput { get => IsTool(RemoteTools.Input); set => SetTool(RemoteTools.Input, value); }
    public bool ToolScreen { get => IsTool(RemoteTools.Screen); set => SetTool(RemoteTools.Screen, value); }
    public bool ToolWindows { get => IsTool(RemoteTools.Windows); set => SetTool(RemoteTools.Windows, value); }
    public bool ToolShortcuts { get => IsTool(RemoteTools.Shortcuts); set => SetTool(RemoteTools.Shortcuts, value); }
    public bool ToolTransfer { get => IsTool(RemoteTools.Transfer); set => SetTool(RemoteTools.Transfer, value); }
    public bool ToolInfo { get => IsTool(RemoteTools.Info); set => SetTool(RemoteTools.Info, value); }

    private bool IsTool(string id) => RemoteTools.IsEnabled(_store.Current.FerramentasCelular, id);

    private void SetTool(string id, bool on, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (IsTool(id) == on) return;
        // O servidor e o celular reagem pelo evento Changed da configuração; desligar cancela o que estava em andamento
        _store.Update(s => s with { FerramentasCelular = RemoteTools.With(s.FerramentasCelular, id, on) });
        Log.Write($"celular: ferramenta {id} {(on ? "ligada" : "desligada")}");
        OnPropertyChanged(property);
        if (id == RemoteTools.Shortcuts) OnPropertyChanged(nameof(ShortcutsFolderVisibility));
    }

    /// <summary>"Abrir pasta de atalhos" (Fase 9c, D23): só com o grupo atalhos ligado.</summary>
    public Visibility ShortcutsFolderVisibility => ToolShortcuts ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Abre a pasta de atalhos no Explorer (cria se não existir). No <c>--teste-pc</c> só cria: nenhuma janela abre.</summary>
    public void OpenShortcutsFolder()
    {
        var folder = Pc.ShortcutFolder.PathFor(AppPaths.DataFolder, App.TestPc);
        Pc.ShortcutFolder.Ensure(folder);
        if (App.TestPc)
        {
            Log.Write("celular: pasta de atalhos nao aberta (modo de teste)");
            return;
        }
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            Log.Write("celular: pasta de atalhos aberta");
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: pasta de atalhos: {ex.Message}");
        }
    }

    // Vem de qualquer thread (rede)
    private void OnConnectionsChanged() => _ui?.TryEnqueue(() => { if (!_disposed) UpdateCount(); });

    private void OnRemoteStateChanged() => _ui?.TryEnqueue(() => { if (!_disposed) RefreshRemote(); });

    /// <summary>IPv4 da placa que tem gateway padrão (D17); sem nenhuma, o endereço local.</summary>
    internal static string LocalAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                if (!hasGateway) continue;
                var unicast = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (unicast is not null) return unicast.Address.ToString();
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: endereco local: {ex.Message}");
        }
        return IPAddress.Loopback.ToString();
    }

    public void Dispose()
    {
        _disposed = true;
        _devices.Changed -= OnDevicesChanged;
        if (_updates is not null) _updates.Changed -= OnUpdateChanged;
        if (_remote is not null)
        {
            _remote.ConnectionsChanged -= OnConnectionsChanged;
            _remote.StateChanged -= OnRemoteStateChanged;
        }
    }
}
