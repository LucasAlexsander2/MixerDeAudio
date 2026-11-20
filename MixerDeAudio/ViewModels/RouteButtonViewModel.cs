using System;

namespace MixerDeAudio.ViewModels;

/// <summary>Botão de uma saída na linha de um app (D10): só o ícone; a dica e a acessibilidade dizem para onde envia.</summary>
public sealed class RouteButtonViewModel : ObservableObject
{
    private readonly Action<string> _select;
    private bool _isActive;

    public RouteButtonViewModel(string deviceId, string glyph, string appName, string deviceShortName, bool isActive, Action<string> select)
    {
        DeviceId = deviceId;
        Glyph = glyph;
        Name = $"Enviar {appName} para {deviceShortName}";
        _isActive = isActive;
        _select = select;
    }

    public string DeviceId { get; }
    public string Glyph { get; }
    public string Name { get; }

    /// <summary>A saída efetiva do app; só muda pela releitura (D6 e D8), nunca pelo clique direto.</summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    /// <summary>Clique: pede a troca e reenvia <see cref="IsActive"/> para desfazer o toggle automático do botão. No destaque, nada.</summary>
    public void OnClick()
    {
        if (!_isActive) _select(DeviceId);
        OnPropertyChanged(nameof(IsActive));
    }
}
