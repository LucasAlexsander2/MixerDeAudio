using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class DeviceGlyphTests
{
    [Theory]
    [InlineData(3, "")]  // fone
    [InlineData(5, "")]  // headset
    [InlineData(6, "")]  // handset
    [InlineData(1, "")]  // alto-falantes
    [InlineData(2, "")]  // saída de linha
    [InlineData(8, "")]  // SPDIF
    [InlineData(9, "")]  // HDMI / DisplayPort
    [InlineData(0, "")]
    [InlineData(42, "")] // desconhecido
    public void GlifoPeloFormato(int formato, string esperado) => Assert.Equal(esperado, DeviceGlyph.For(formato));
}
