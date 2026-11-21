using System;
using System.Collections.Generic;
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
    private readonly AudioHub _hub;
    private SessionTracker? _tracker;
    private DeviceTracker? _devices;
    private bool _attached;
    private readonly Func<IReadOnlyCollection<string>> _favorites;

    /// <summary>
    /// Fase 8, D1: a janela é reaproveitada, então a view model vive entre aberturas e só liga ao núcleo em
    /// <see cref="Attach"/> (com o hub ativo) e desliga em <see cref="Detach"/>. Os comandos apontam para o hub.
    /// </summary>
    internal SessionListViewModel(AudioHub hub, Func<IReadOnlyCollection<string>> favorites)
    {
        _favorites = favorites;
        _hub = hub;
        Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>
    /// Fase 6, D3 e D4: as linhas nascem do hub, que pode já estar ativo (celular conectado): as que já existem entram
    /// primeiro, e as novas chegam pelos avisos do <see cref="SessionTracker"/>. Só com o hub ativo.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _tracker = _hub.Sessions;
        _devices = _hub.Devices;
        foreach (var group in _tracker.Groups) AddRow(group, Rows.Count);
        _tracker.Changed += OnTrackerChanged;
        _devices.Changed += OnDevicesChanged;
        _hub.RouteChanged += OnRouteChanged;
        _hub.MediaChanged += OnMediaChanged;
        SyncMedia();
    }

    /// <summary>Desliga os eventos do núcleo e esvazia as linhas (a janela escondida não guarda nada do áudio).</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        if (_tracker is not null) _tracker.Changed -= OnTrackerChanged;
        if (_devices is not null) _devices.Changed -= OnDevicesChanged;
        _hub.RouteChanged -= OnRouteChanged;
        _hub.MediaChanged -= OnMediaChanged;
        _tracker = null;
        _devices = null;
        foreach (var row in Rows) row.PropertyChanged -= OnRowPropertyChanged;
        Rows.Clear();
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    /// <summary>A quantidade de linhas, ou a 3ª linha de mídia de alguma, mudou (a altura da janela pode mudar; Fase 8, D2).</summary>
    public event Action? LayoutChanged;

    public ObservableCollection<SessionRowViewModel> Rows { get; } = new();

    public Visibility EmptyVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(EmptyVisibility));
        if (e.OldItems is not null)
            foreach (SessionRowViewModel row in e.OldItems) row.PropertyChanged -= OnRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (SessionRowViewModel row in e.NewItems) row.PropertyChanged += OnRowPropertyChanged;
        if (e.Action != NotifyCollectionChangedAction.Move && _attached) LayoutChanged?.Invoke();
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionRowViewModel.MediaVisibility) && _attached) LayoutChanged?.Invoke();
    }

    private void OnTrackerChanged(GroupChange change)
    {
        switch (change.Kind)
        {
            case GroupChangeKind.Added:
                AddRow(change.Group, change.Index);
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

    private void AddRow(GroupSnapshot group, int index)
    {
        var row = new SessionRowViewModel(group, _hub.SetVolume, _hub.SetMute, OnMediaCommand,
            iconKey: _hub.GroupIconKey(group.Key, group.ExePath));
        Rows.Insert(Math.Min(index, Rows.Count), row);
        row.LoadIcon();
        LoadRoutes(row);
        row.ApplyMedia(_hub.Media.FirstOrDefault(m => m.Key == row.Key));
    }

    // ---- Mídia (Fase 7): botões nas linhas com mídia e linhas só de mídia no fim da lista (D6) ----
    // As linhas só de mídia ficam sempre depois das linhas de som: os índices do SessionTracker continuam valendo.

    private const string MediaKeyPrefix = "midia:";

    private void OnMediaCommand(string aumid, MediaAction action)
    {
        switch (action)
        {
            case MediaAction.Previous: _hub.MediaPrevious(aumid); break;
            case MediaAction.PlayPause: _hub.MediaPlayPause(aumid); break;
            case MediaAction.Next: _hub.MediaNext(aumid); break;
        }
    }

    private void OnMediaChanged() => SyncMedia();

    /// <summary>Aplica a mídia às linhas de som e mantém as linhas só de mídia (as sem linha de som ligada).</summary>
    private void SyncMedia()
    {
        var media = _hub.Media;
        foreach (var row in Rows)
            if (!row.IsMediaOnly) row.ApplyMedia(media.FirstOrDefault(m => m.Key == row.Key));

        var only = media.Where(m => m.Key is null).ToList();
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (Rows[i].IsMediaOnly && !only.Any(m => MediaKeyPrefix + m.Aumid == Rows[i].Key)) Rows.RemoveAt(i);

        foreach (var m in only)
        {
            var key = MediaKeyPrefix + m.Aumid;
            var row = Rows.FirstOrDefault(r => r.Key == key);
            if (row is null)
            {
                var snapshot = new GroupSnapshot(key, _hub.MediaAppName(m.Aumid), _hub.MediaExePath(m.Aumid), 0f, false, false, 0);
                row = new SessionRowViewModel(snapshot, _hub.SetVolume, _hub.SetMute, OnMediaCommand, mediaOnly: true,
                    iconKey: _hub.MediaIconKey(m.Aumid));
                Rows.Add(row);
                row.LoadIcon();
            }
            row.ApplyMedia(m);
        }
    }

    // ---- Saída por app (Fase 4; a escolha mora no hub desde a Fase 6, D3) ----

    /// <summary>Lê a escolha do app pelo hub (D6) e monta os botões, se couber (D7). Sem pid, tenta de novo quando a linha mudar.</summary>
    private void LoadRoutes(SessionRowViewModel row)
    {
        if (_hub.Routing is null || row.IsSystem) return;
        if (!_hub.TryLoadRoute(row.Key, row.IsSystem)) return;
        row.RoutesLoaded = true;
        RebuildRoutes(row);
    }

    /// <summary>A escolha de um app mudou (por esta janela ou pelo celular): move o destaque (ou refaz a linha).</summary>
    private void OnRouteChanged(string key)
    {
        foreach (var row in Rows)
        {
            if (row.Key != key) continue;
            if (row.RoutesLoaded) UpdateHighlight(row);
            return;
        }
    }

    /// <summary>Saídas que aparecem na linha (D5 e D6): as favoritas e, no fim, a efetiva do app se não for favorita.</summary>
    private List<DeviceInfo> VisibleFor(SessionRowViewModel row) =>
        FavoriteFilter.Visible(_devices!.Devices, _favorites(), EffectiveOf(row));

    private void RebuildRoutes(SessionRowViewModel row)
    {
        row.Routes.Clear();
        var visible = VisibleFor(row);
        var show = AppRouting.ShowButtons(row.IsSystem, row.RoutesLoaded, visible.Count, _hub.Routing is not null);
        if (show)
        {
            var effective = EffectiveOf(row);
            foreach (var d in visible)
            {
                row.Routes.Add(new RouteButtonViewModel(d.Id, DeviceGlyph.For(d.FormFactor), row.Name, d.ShortName,
                    string.Equals(d.Id, effective, StringComparison.OrdinalIgnoreCase), id => OnRouteClick(row, id)));
            }
        }
        row.RouteVisibility = show && row.Routes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Refaz os botões de todas as linhas (volta da página de configurações, D7).</summary>
    public void RefreshRoutes()
    {
        foreach (var row in Rows)
            if (row.RoutesLoaded) RebuildRoutes(row);
    }

    private string? EffectiveOf(SessionRowViewModel row) =>
        AppRouting.Effective(_hub.PreferredOf(row.Key), _devices!.DefaultId, _devices.Devices.Select(d => d.Id));

    /// <summary>
    /// Move o destaque; se o conjunto de botões visíveis mudou (a efetiva entrou ou saiu do "extra" do P2), refaz a linha.
    /// </summary>
    private void UpdateHighlight(SessionRowViewModel row)
    {
        var visible = VisibleFor(row);
        var same = visible.Count == row.Routes.Count;
        for (var i = 0; same && i < visible.Count; i++)
            same = string.Equals(visible[i].Id, row.Routes[i].DeviceId, StringComparison.OrdinalIgnoreCase);
        if (!same)
        {
            RebuildRoutes(row);
            return;
        }
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

    /// <summary>Clique num botão (D3, D6, D12): o hub grava ou apaga a escolha, relê e avisa (o destaque se move em <see cref="OnRouteChanged"/>).</summary>
    private void OnRouteClick(SessionRowViewModel row, string clickedId) => _hub.ClickRoute(row.Key, clickedId);

    public void Dispose()
    {
        Detach();
        Rows.CollectionChanged -= OnRowsChanged;
    }
}
