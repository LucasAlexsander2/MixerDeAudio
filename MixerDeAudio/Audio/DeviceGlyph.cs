namespace MixerDeAudio.Audio;

/// <summary>Escolhe o ícone (Segoe Fluent Icons) do botão da saída pelo formato físico dela (D8).</summary>
public static class DeviceGlyph
{
    public const string Headphones = "";
    public const string Speaker = "";
    public const string Monitor = "";

    /// <param name="formFactor">Valor de <c>PKEY_AudioEndpoint_FormFactor</c> (enumeração EndpointFormFactor).</param>
    public static string For(int formFactor) => formFactor switch
    {
        3 or 5 or 6 => Headphones,   // fone, headset, handset
        9 => Monitor,                // HDMI / DisplayPort
        _ => Speaker,                // 1, 2, 8 e qualquer outro valor
    };
}
