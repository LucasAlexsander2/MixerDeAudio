using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class AppNameResolverTests
{
    [Fact]
    public void SistemaDaSonsDoSistema() =>
        Assert.Equal("Sons do sistema", AppNameResolver.Resolve(true, "Qualquer", "Desc", "proc"));

    [Fact]
    public void DisplayNamePreenchidoVence() =>
        Assert.Equal("Meu App", AppNameResolver.Resolve(false, "Meu App", "Descricao", "proc"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DisplayNameVazioOuEmBrancoCaiParaADescricao(string? display) =>
        Assert.Equal("Descricao", AppNameResolver.Resolve(false, display, "Descricao", "proc"));

    [Fact]
    public void TextoArrobaResolvidoEUsado() =>
        Assert.Equal("Nome do recurso", AppNameResolver.Resolve(false, "Nome do recurso", "Descricao", "proc"));

    [Fact]
    public void ArrobaQueNaoResolveuCaiParaOProximo() =>
        // O chamador passa nulo quando o SHLoadIndirectString falha.
        Assert.Equal("Descricao", AppNameResolver.Resolve(false, null, "Descricao", "proc"));

    [Fact]
    public void SemDescricaoUsaONomeDoProcesso() =>
        Assert.Equal("proc", AppNameResolver.Resolve(false, null, " ", "proc"));

    [Fact]
    public void SemNadaDaAppDesconhecido() =>
        Assert.Equal("App desconhecido", AppNameResolver.Resolve(false, null, null, null));
}
