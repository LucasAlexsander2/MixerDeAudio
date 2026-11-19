using System;
using MixerDeAudio.Audio;

namespace MixerDeAudio.ViewModels;

/// <summary>Um botão da fileira de saídas: nome curto, nome completo, glifo e se é a saída ativa (D15).</summary>
public sealed class DeviceButtonViewModel : ObservableObject
{
    private readonly Action<string> _select;
    private string _shortName;
    private string _fullName;
    private string _glyph;
    private bool _isActive;

    public DeviceButtonViewModel(DeviceInfo device, bool isActive, Action<string> select)
    {
        _select = select;
        Id = device.Id;
        _shortName = device.ShortName;
        _fullName = device.FullName;
        _glyph = DeviceGlyph.For(device.FormFactor);
        _isActive = isActive;
    }

    public string Id { get; }
    public string ShortName { get => _shortName; private set => SetProperty(ref _shortName, value); }
    public string FullName { get => _fullName; private set => SetProperty(ref _fullName, value); }
    public string Glyph { get => _glyph; private set => SetProperty(ref _glyph, value); }

    /// <summary>Só muda pelo evento do Windows (D10); o clique do usuário nunca muda este valor.</summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public void Apply(DeviceInfo device)
    {
        ShortName = device.ShortName;
        FullName = device.FullName;
        Glyph = DeviceGlyph.For(device.FormFactor);
    }

    /// <summary>
    /// Clique no botão (D10): pede a troca e devolve o botão ao estado real. O ToggleButton muda sozinho ao ser
    /// clicado; o destaque certo vem do evento do Windows, e o aviso abaixo refaz a ligação com o valor verdadeiro.
    /// </summary>
    public void OnClick()
    {
        if (!_isActive) _select(Id);
        OnPropertyChanged(nameof(IsActive));
    }
}
