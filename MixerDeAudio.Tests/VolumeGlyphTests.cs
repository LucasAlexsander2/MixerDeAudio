using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class VolumeGlyphTests
{
    [Fact]
    public void MudoMostraOAltoFalanteCortado() => Assert.Equal("", VolumeGlyph.For(80, true));

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    [InlineData(33, "")]
    [InlineData(34, "")]
    [InlineData(66, "")]
    [InlineData(67, "")]
    [InlineData(100, "")]
    public void GlifoPeloVolume(int percent, string esperado) => Assert.Equal(esperado, VolumeGlyph.For(percent, false));
}
