using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MixerDeAudio.Audio;
using MixerDeAudio.Services;

namespace MixerDeAudio.ViewModels;

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
    private bool _startup;

    internal SettingsViewModel(SettingsStore store, DeviceTracker devices)
    {
        _store = store;
        _devices = devices;
        _startup = StartupRegistration.IsEnabled();
        RebuildFavorites();
        _devices.Changed += OnDevicesChanged;
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

    public void Dispose() => _devices.Changed -= OnDevicesChanged;
}
