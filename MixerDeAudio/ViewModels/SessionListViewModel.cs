using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using MixerDeAudio.Audio;

namespace MixerDeAudio.ViewModels;

/// <summary>
/// Lista de linhas na ordem do D12, alimentada pelos avisos do <see cref="SessionTracker"/>. Insere, remove e move
/// na posição certa, sem recriar a lista inteira (D19).
/// </summary>
public sealed class SessionListViewModel : ObservableObject, IDisposable
{
    private readonly SessionTracker _tracker;
    private readonly Action<string, float> _setVolume;
    private readonly Action<string, bool> _setMute;

    public SessionListViewModel(SessionTracker tracker, Action<string, float> setVolume, Action<string, bool> setMute)
    {
        _tracker = tracker;
        _setVolume = setVolume;
        _setMute = setMute;
        Rows.CollectionChanged += OnRowsChanged;
        _tracker.Changed += OnTrackerChanged;
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
                break;
            case GroupChangeKind.Changed:
                if (change.OldIndex < 0 || change.OldIndex >= Rows.Count) return;
                Rows[change.OldIndex].Apply(change.Group);
                if (change.Index != change.OldIndex && change.Index >= 0 && change.Index < Rows.Count)
                    Rows.Move(change.OldIndex, change.Index);
                break;
            case GroupChangeKind.Removed:
                if (change.OldIndex >= 0 && change.OldIndex < Rows.Count) Rows.RemoveAt(change.OldIndex);
                break;
        }
    }

    public void Dispose()
    {
        _tracker.Changed -= OnTrackerChanged;
        Rows.CollectionChanged -= OnRowsChanged;
    }
}
