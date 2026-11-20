using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

public class StartupRulesTests
{
    private const string Exe = @"C:\Projetos\Mixer de Audio\publicado\MixerDeAudio.exe";

    [Fact]
    public void CaminhoIgual_ComESemAspas()
    {
        Assert.True(StartupRules.PathMatches(Exe, Exe));
        Assert.True(StartupRules.PathMatches("\"" + Exe + "\"", Exe));
        Assert.True(StartupRules.PathMatches(Exe, "\"" + Exe + "\""));
    }

    [Fact]
    public void MaiusculasDiferentes_Casam() =>
        Assert.True(StartupRules.PathMatches("\"" + Exe.ToUpperInvariant() + "\"", Exe.ToLowerInvariant()));

    [Fact]
    public void OutroCaminho_NaoCasa() =>
        Assert.False(StartupRules.PathMatches("\"C:\\Outro\\MixerDeAudio.exe\"", Exe));

    [Fact]
    public void ArgumentoDepoisDoCaminhoEntreAspas_EhIgnorado() =>
        Assert.True(StartupRules.PathMatches("\"" + Exe + "\" --abrir", Exe));

    [Theory]
    [InlineData(null, Exe)]
    [InlineData("", Exe)]
    [InlineData("   ", Exe)]
    [InlineData(Exe, null)]
    [InlineData(Exe, "")]
    public void VazioOuNulo_NaoCasa(string? valor, string? exe) => Assert.False(StartupRules.PathMatches(valor, exe));

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x01 }, true)]
    [InlineData(new byte[] { 0x04 }, false)]
    public void BytesDoStartupApproved(byte[] bytes, bool desligado) => Assert.Equal(desligado, StartupRules.IsDisabled(bytes));

    [Fact]
    public void Vazio_NaoEstaDesligado() => Assert.False(StartupRules.IsDisabled([]));

    [Fact]
    public void Nulo_NaoEstaDesligado() => Assert.False(StartupRules.IsDisabled(null));
}
