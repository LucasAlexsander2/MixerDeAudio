using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MixerDeAudio.Audio;

namespace MixerDeAudio.ViewModels;

/// <summary>Linha do volume geral: volume de 0 a 100, mudo, glifo, texto do valor e se está habilitada (D15).</summary>
public sealed class MasterVolumeViewModel : ObservableObject
{
    private readonly Action<float> _setVolume;
    private readonly Action<bool> _setMute;
    private bool _updatingFromSystem; // D12: atualizar pelo sistema não pode disparar nova gravação
    private double _volume;
    private bool _muted;
    private bool _enabled;

    public MasterVolumeViewModel(Action<float> setVolume, Action<bool> setMute)
    {
        _setVolume = setVolume;
        _setMute = setMute;
    }

    /// <summary>Habilitada só quando há saída padrão (D13).</summary>
    public bool Enabled { get => _enabled; private set => SetProperty(ref _enabled, value); }

    /// <summary>Volume de 0 a 100. Mudar pelo slider grava no Windows; mudar pelo sistema não.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, value)) return;
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(MuteGlyph));
            if (!_updatingFromSystem) _setVolume((float)(value / 100.0));
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
            if (!_updatingFromSystem) _setMute(value);
        }
    }

    public string ValueText => ((int)Math.Round(_volume)).ToString();
    public string MuteGlyph => VolumeGlyph.For((int)Math.Round(_volume), _muted);
    public string MuteName => _muted ? "Ativar som" : "Silenciar tudo";

    /// <summary>Roda do mouse sobre o slider (Fase 8, D3): ±2 por dente, pelo mesmo caminho do arrastar.</summary>
    public void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!_enabled) return;
        var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
        Volume = VolumeStep.Apply(_volume, delta);
        e.Handled = true; // a lista não rola junto
    }

    /// <summary>Aplica o estado vindo do Windows sem gravar de volta (D12).</summary>
    public void Apply(bool available, float volume, bool muted)
    {
        _updatingFromSystem = true;
        try
        {
            Enabled = available;
            Volume = Math.Round(Math.Clamp(volume, 0f, 1f) * 100.0);
            Muted = muted;
        }
        finally
        {
            _updatingFromSystem = false;
        }
    }
}
