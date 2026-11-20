using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using MixerDeAudio.Audio;

namespace MixerDeAudio.ViewModels;

/// <summary>
/// Botões das saídas na ordem do D7, alimentados pelos avisos do <see cref="DeviceTracker"/>: insere, remove e move
/// na posição certa, sem recriar a lista (D15). Na Fase 5 só mostra as favoritas e a saída ativa (D5).
/// </summary>
public sealed class DeviceListViewModel : ObservableObject, IDisposable
{
    private readonly DeviceTracker _tracker;
    private readonly Action<string> _select;
    private readonly Func<IReadOnlyCollection<string>> _favorites;

    /// <param name="favorites">Ids das favoritas no momento (Fase 5, D5); vazio mostra todas as saídas.</param>
    public DeviceListViewModel(DeviceTracker tracker, Action<string> select, Func<IReadOnlyCollection<string>> favorites)
    {
        _tracker = tracker;
        _select = select;
        _favorites = favorites;
        Refresh();
        Buttons.CollectionChanged += OnButtonsChanged;
        _tracker.Changed += OnTrackerChanged;
    }

    public ObservableCollection<DeviceButtonViewModel> Buttons { get; } = new();

    /// <summary>"Nenhuma saída de áudio" aparece quando não há botões (D13).</summary>
    public Visibility NoDeviceVisibility => Buttons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListVisibility => Buttons.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    private void OnButtonsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(NoDeviceVisibility));
        OnPropertyChanged(nameof(ListVisibility));
    }

    private void OnTrackerChanged(DeviceChange change) => Refresh();

    /// <summary>
    /// Reconcilia os botões com o que o D5 manda mostrar (favoritas ativas e, no fim, a ativa que não é favorita):
    /// tira, insere e move na posição certa, sem recriar os botões que continuam. Também é chamada ao voltar da
    /// página de configurações (D7).
    /// </summary>
    public void Refresh()
    {
        var desired = FavoriteFilter.Visible(_tracker.Devices, _favorites(), _tracker.ActiveId);
        for (var i = Buttons.Count - 1; i >= 0; i--)
        {
            var id = Buttons[i].Id;
            if (!desired.Exists(d => string.Equals(d.Id, id, StringComparison.Ordinal))) Buttons.RemoveAt(i);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            var d = desired[i];
            var at = IndexOf(d.Id);
            if (at < 0)
            {
                Buttons.Insert(i, new DeviceButtonViewModel(d, _tracker.IsActive(d.Id), _select));
                continue;
            }
            if (at != i) Buttons.Move(at, i);
            Buttons[i].Apply(d);
            Buttons[i].IsActive = _tracker.IsActive(d.Id);
        }
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < Buttons.Count; i++)
            if (string.Equals(Buttons[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    public void Dispose()
    {
        _tracker.Changed -= OnTrackerChanged;
        Buttons.CollectionChanged -= OnButtonsChanged;
    }
}
