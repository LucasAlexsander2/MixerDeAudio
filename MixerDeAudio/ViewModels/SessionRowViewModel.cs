using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MixerDeAudio.Audio;

namespace MixerDeAudio.ViewModels;

/// <summary>Uma linha da lista (um app): nome, ícone, volume de 0 a 100, mudo, glifo e texto do valor (D19, D20).</summary>
public sealed class SessionRowViewModel : ObservableObject
{
    private const string GenericGlyph = "";
    private const string SystemGlyph = "";

    private readonly Action<string, float> _setVolume;
    private readonly Action<string, bool> _setMute;
    private bool _updatingFromSystem; // D20: atualizar pelo sistema não pode disparar nova gravação
    private string _name;
    private double _volume;
    private bool _muted;
    private ImageSource? _icon;

    public SessionRowViewModel(GroupSnapshot snapshot, Action<string, float> setVolume, Action<string, bool> setMute)
    {
        _setVolume = setVolume;
        _setMute = setMute;
        Key = snapshot.Key;
        ExePath = snapshot.ExePath;
        IsSystem = snapshot.IsSystem;
        FallbackGlyph = snapshot.IsSystem ? SystemGlyph : GenericGlyph;
        _name = snapshot.Name;
        _volume = ToPercent(snapshot.Volume);
        _muted = snapshot.Muted;
    }

    public string Key { get; }
    public string? ExePath { get; }
    public bool IsSystem { get; }

    /// <summary>Botões de saída do app (Fase 4, D10).</summary>
    public ObservableCollection<RouteButtonViewModel> Routes { get; } = new();

    /// <summary>Id do <c>MMDevice</c> que o app escolheu; nulo se segue a saída padrão (D6).</summary>
    public string? PreferredId { get; set; }

    /// <summary>Se a escolha já foi lida do Windows.</summary>
    public bool RoutesLoaded { get; set; }

    private Visibility _routeVisibility = Visibility.Collapsed;
    public Visibility RouteVisibility { get => _routeVisibility; set => SetProperty(ref _routeVisibility, value); }
    public string FallbackGlyph { get; }

    public string Name
    {
        get => _name;
        private set
        {
            if (!SetProperty(ref _name, value)) return;
            OnPropertyChanged(nameof(MuteName));
            OnPropertyChanged(nameof(VolumeName));
        }
    }

    /// <summary>Volume de 0 a 100. Mudar pelo slider grava no Windows; mudar pelo sistema não.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, value)) return;
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(MuteGlyph));
            if (!_updatingFromSystem) _setVolume(Key, (float)(value / 100.0));
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (!SetProperty(ref _muted, value)) return;
            OnPropertyChanged(nameof(MuteGlyph));
            OnPropertyChanged(nameof(MuteName));
            if (!_updatingFromSystem) _setMute(Key, value);
        }
    }

    public string ValueText => ((int)Math.Round(_volume)).ToString();
    public string MuteGlyph => VolumeGlyph.For((int)Math.Round(_volume), _muted);
    public string MuteName => _muted ? $"Ativar som de {_name}" : $"Silenciar {_name}";
    public string VolumeName => $"Volume de {_name}";

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (!SetProperty(ref _icon, value)) return;
            OnPropertyChanged(nameof(IconVisibility));
            OnPropertyChanged(nameof(FallbackVisibility));
        }
    }

    public Visibility IconVisibility => _icon is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FallbackVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Aplica uma foto vinda do Windows sem gravar de volta (D20).</summary>
    public void Apply(GroupSnapshot snapshot)
    {
        _updatingFromSystem = true;
        try
        {
            Name = snapshot.Name;
            Volume = ToPercent(snapshot.Volume);
            Muted = snapshot.Muted;
        }
        finally
        {
            _updatingFromSystem = false;
        }
    }

    /// <summary>Busca o ícone fora da thread da tela e troca o glifo genérico quando fica pronto (D18).</summary>
    public async void LoadIcon()
    {
        try
        {
            var png = await AppIconCache.GetPngAsync(ExePath).ConfigureAwait(false);
            if (png.Length == 0) return;
            var queue = App.UiQueue;
            queue?.TryEnqueue(async () =>
            {
                try
                {
                    var image = await AppIconCache.CreateImageAsync(png);
                    if (image is not null) Icon = image;
                }
                catch
                {
                    // fica o ícone genérico
                }
            });
        }
        catch
        {
            // fica o ícone genérico
        }
    }

    private static double ToPercent(float volume) => Math.Round(Math.Clamp(volume, 0f, 1f) * 100.0);
}
