using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

public class CommandLineTests
{
    [Fact]
    public void Vazio_EhPadrao() => Assert.Equal(new CommandLineResult(AppCommand.Default, false), CommandLine.Parse([]));

    [Theory]
    [InlineData("--abrir", AppCommand.Open)]
    [InlineData("--abrir-sem-foco", AppCommand.OpenNoFocus)]
    [InlineData("--configuracoes", AppCommand.Settings)]
    [InlineData("--configuracoes-sem-foco", AppCommand.SettingsNoFocus)]
    [InlineData("--fechar", AppCommand.Close)]
    [InlineData("--sair", AppCommand.Exit)]
    public void CadaArgumento(string arg, AppCommand esperado) => Assert.Equal(esperado, CommandLine.Parse([arg]).Command);

    [Fact]
    public void SemAparar_SozinhoLigaAFlagSemComando()
    {
        var r = CommandLine.Parse(["--sem-aparar"]);
        Assert.True(r.NoTrim);
        Assert.Equal(AppCommand.Default, r.Command);
    }

    [Theory]
    [InlineData(@"C:\app\MixerDeAudio.exe --abrir")]
    [InlineData("\"C:\\Program Files\\app\\MixerDeAudio.exe\" --abrir")]
    public void CaminhoDoExe_ComESemAspas_EhIgnorado(string linha) => Assert.Equal(AppCommand.Open, CommandLine.ParseLine(linha).Command);

    [Fact]
    public void CaminhoDoExe_SoEhVazio() => Assert.Equal(AppCommand.Default, CommandLine.ParseLine("\"C:\\a b\\MixerDeAudio.exe\"").Command);

    [Theory]
    [InlineData("--ABRIR", AppCommand.Open)]
    [InlineData("--Fechar", AppCommand.Close)]
    [InlineData("--SAIR", AppCommand.Exit)]
    public void Maiusculas(string arg, AppCommand esperado) => Assert.Equal(esperado, CommandLine.Parse([arg]).Command);

    [Fact]
    public void Desconhecido_NaoFazNada() => Assert.Equal(AppCommand.None, CommandLine.Parse(["--xyz"]).Command);

    [Fact]
    public void SemAparar_JuntoComAbrirSemFoco()
    {
        var r = CommandLine.Parse(["--abrir-sem-foco", "--sem-aparar"]);
        Assert.Equal(AppCommand.OpenNoFocus, r.Command);
        Assert.True(r.NoTrim);
        Assert.Equal(r, CommandLine.ParseLine("--sem-aparar --abrir-sem-foco"));
    }
}
