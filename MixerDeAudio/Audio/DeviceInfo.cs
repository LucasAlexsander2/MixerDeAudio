namespace MixerDeAudio.Audio;

/// <summary>Dados de uma saída de áudio, já copiados do Windows (sem objetos COM). Lógica pura (D5).</summary>
/// <param name="Id">Identificador do dispositivo (<c>MMDevice.ID</c>).</param>
/// <param name="ShortName">Nome curto, o rótulo do botão (D6).</param>
/// <param name="FullName">Nome completo, para a dica e a acessibilidade.</param>
/// <param name="FormFactor">Formato físico (<c>PKEY_AudioEndpoint_FormFactor</c>); 0 se desconhecido.</param>
public sealed record DeviceInfo(string Id, string ShortName, string FullName, int FormFactor)
{
    public const string UnknownName = "Saída desconhecida";

    /// <summary>Nome curto (D6): a descrição do dispositivo; senão o nome completo; senão "Saída desconhecida".</summary>
    public static string ShortNameOf(string? description, string? fullName)
    {
        if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        if (!string.IsNullOrWhiteSpace(fullName)) return fullName.Trim();
        return UnknownName;
    }

    /// <summary>Cria a saída aplicando o D6 ao nome curto e ao completo.</summary>
    public static DeviceInfo Create(string id, string? description, string? fullName, int formFactor)
    {
        var shortName = ShortNameOf(description, fullName);
        var full = string.IsNullOrWhiteSpace(fullName) ? shortName : fullName.Trim();
        return new DeviceInfo(id, shortName, full, formFactor);
    }
}
