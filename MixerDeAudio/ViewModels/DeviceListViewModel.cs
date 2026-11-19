using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using MixerDeAudio.Audio;

namespace MixerDeAudio.ViewModels;

/// <summary>
/// Botões das saídas na ordem do D7, alimentados pelos avisos do <see cref="DeviceTracker"/>: insere, remove e move
/// na posição certa, sem recriar a lista (D15).
/// </summary>
public sealed class DeviceListViewModel : ObservableObject, IDisposable
{
    private readonly DeviceTracker _tracker;
    private readonly Action<string> _select;

    public DeviceListViewModel(DeviceTracker tracker, Action<string> select)
    {
        _tracker = tracker;
        _select = select;
        foreach (var d in tracker.Devices) Buttons.Add(new DeviceButtonViewModel(d, tracker.IsActive(d.Id), select));
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

    private void OnTrackerChanged(DeviceChange change)
    {
        switch (change.Kind)
        {
            case DeviceChangeKind.Added when change.Device is not null:
                Buttons.Insert(Math.Min(change.Index, Buttons.Count),
                    new DeviceButtonViewModel(change.Device, _tracker.IsActive(change.Device.Id), _select));
                break;
            case DeviceChangeKind.Changed when change.Device is not null:
                if (change.OldIndex < 0 || change.OldIndex >= Buttons.Count) return;
                Buttons[change.OldIndex].Apply(change.Device);
                if (change.Index != change.OldIndex && change.Index >= 0 && change.Index < Buttons.Count)
                    Buttons.Move(change.OldIndex, change.Index);
                break;
            case DeviceChangeKind.Removed:
                if (change.OldIndex >= 0 && change.OldIndex < Buttons.Count) Buttons.RemoveAt(change.OldIndex);
                break;
            case DeviceChangeKind.DefaultChanged:
                foreach (var b in Buttons) b.IsActive = _tracker.IsActive(b.Id);
                break;
        }
    }

    public void Dispose()
    {
        _tracker.Changed -= OnTrackerChanged;
        Buttons.CollectionChanged -= OnButtonsChanged;
    }
}
