using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using MixerDeAudio.Audio;
using MixerDeAudio.Services;

namespace MixerDeAudio.ViewModels;

/// <summary>
/// Lista de linhas na ordem do D12, alimentada pelos avisos do <see cref="SessionTracker"/>. Insere, remove e move
/// na posição certa, sem recriar a lista inteira (D19). Na Fase 4 também monta os botões de saída de cada linha (D11),
/// a partir do <see cref="DeviceTracker"/> e do <see cref="AppRoutingService"/>; tudo na thread da tela.
/// </summary>
public sealed class SessionListViewModel : ObservableObject, IDisposable
{
    private readonly SessionTracker _tracker;
    private readonly DeviceTracker _devices;
    private readonly AppRoutingService? _routing;
    private readonly Action<string, float> _setVolume;
    private readonly Action<string, bool> _setMute;

    internal SessionListViewModel(SessionTracker tracker, DeviceTracker devices, AppRoutingService? routing,
        Action<string, float> setVolume, Action<string, bool> setMute)
    {
        _tracker = tracker;
        _devices = devices;
        _routing = routing;
        _setVolume = setVolume;
        _setMute = setMute;
        Rows.CollectionChanged += OnRowsChanged;
        _tracker.Changed += OnTrackerChanged;
        _devices.Changed += OnDevicesChanged;
    }

    public ObservableCollection<SessionRowViewModel> Rows { get; } = new();

    public Visibility EmptyVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(EmptyVisibility));

    private void OnTrackerChanged(GroupChange change)
    {
        switch (change.Kind)
        {
            case GroupChangeKind.Added:
                var row = new SessionRowViewModel(change.Group, _setVolume, _setMute);
                Rows.Insert(Math.Min(change.Index, Rows.Count), row);
                row.LoadIcon();
                LoadRoutes(row);
                break;
            case GroupChangeKind.Changed:
                if (change.OldIndex < 0 || change.OldIndex >= Rows.Count) return;
                var existing = Rows[change.OldIndex];
                var oldName = existing.Name;
                existing.Apply(change.Group);
                if (change.Index != change.OldIndex && change.Index >= 0 && change.Index < Rows.Count)
                    Rows.Move(change.OldIndex, change.Index);
                if (!existing.RoutesLoaded) LoadRoutes(existing);
                else if (existing.Name != oldName) RebuildRoutes(existing);
                break;
            case GroupChangeKind.Removed:
                if (change.OldIndex >= 0 && change.OldIndex < Rows.Count) Rows.RemoveAt(change.OldIndex);
                break;
        }
    }

    // ---- Saída por app (Fase 4) ----

    /// <summary>Lê a escolha do app (D6) e monta os botões, se couber (D7). Sem pid, tenta de novo quando a linha mudar.</summary>
    private void LoadRoutes(SessionRowViewModel row)
    {
        if (_routing is null || row.IsSystem) return;
        var pids = _tracker.ProcessIds(row.Key);
        if (pids.Count == 0) return;
        row.RoutesLoaded = true;
        try { row.PreferredId = _routing.GetPreferred(pids); }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao ler a saida de {row.Name}: {ex.Message}");
            row.PreferredId = null;
        }
        RebuildRoutes(row);
    }

    private void RebuildRoutes(SessionRowViewModel row)
    {
        row.Routes.Clear();
        var show = AppRouting.ShowButtons(row.IsSystem, row.RoutesLoaded, _devices.Devices.Count, _routing is not null);
        if (show)
        {
            var effective = EffectiveOf(row);
            foreach (var d in _devices.Devices)
            {
                row.Routes.Add(new RouteButtonViewModel(d.Id, DeviceGlyph.For(d.FormFactor), row.Name, d.ShortName,
                    string.Equals(d.Id, effective, StringComparison.OrdinalIgnoreCase), id => OnRouteClick(row, id)));
            }
        }
        row.RouteVisibility = show && row.Routes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? EffectiveOf(SessionRowViewModel row) =>
        AppRouting.Effective(row.PreferredId, _devices.DefaultId, _devices.Devices.Select(d => d.Id));

    private void UpdateHighlight(SessionRowViewModel row)
    {
        var effective = EffectiveOf(row);
        foreach (var b in row.Routes)
            b.IsActive = string.Equals(b.DeviceId, effective, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Saídas que entram, saem ou mudam refazem os botões; a padrão que muda só move o destaque (D8).</summary>
    private void OnDevicesChanged(DeviceChange change)
    {
        foreach (var row in Rows)
        {
            if (!row.RoutesLoaded) continue;
            if (change.Kind == DeviceChangeKind.DefaultChanged) UpdateHighlight(row);
            else RebuildRoutes(row);
        }
    }

    private string ShortNameOf(string id) =>
        _devices.Devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))?.ShortName ?? id;

    /// <summary>Clique num botão (D3, D6, D12): grava ou apaga a escolha e relê para confirmar.</summary>
    private void OnRouteClick(SessionRowViewModel row, string clickedId)
    {
        if (_routing is null) return;
        var action = AppRouting.OnClick(clickedId, row.PreferredId, _devices.DefaultId);
        if (action.Kind == RouteActionKind.None) return;
        try
        {
            var pids = _tracker.ProcessIds(row.Key);
            if (action.Kind == RouteActionKind.Clear)
            {
                _routing.SetPreferred(pids, null);
                Log.Write($"saida de {row.Name} volta a seguir a padrao");
            }
            else
            {
                _routing.SetPreferred(pids, action.DeviceId);
                Log.Write($"saida de {row.Name} trocada para {ShortNameOf(clickedId)}");
            }
            row.PreferredId = _routing.GetPreferred(pids);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao trocar a saida de {row.Name}: {ex.Message}");
        }
        UpdateHighlight(row);
    }

    public void Dispose()
    {
        _tracker.Changed -= OnTrackerChanged;
        _devices.Changed -= OnDevicesChanged;
        Rows.CollectionChanged -= OnRowsChanged;
    }
}
