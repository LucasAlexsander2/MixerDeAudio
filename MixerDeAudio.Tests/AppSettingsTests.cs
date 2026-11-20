using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SemArquivo_EhPadrao(string? json)
    {
        var s = AppSettings.Parse(json, out var invalido);
        Assert.False(invalido);
        Assert.Empty(s.Favoritos);
        Assert.Equal(AppTheme.Auto, s.Tema);
        Assert.Equal(1, s.Versao);
    }

    [Theory]
    [InlineData("{ isto nao e json")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("{\"Favoritos\": 5}")]
    public void JsonInvalido_VoltaAoPadrao(string json)
    {
        var s = AppSettings.Parse(json, out var invalido);
        Assert.True(invalido);
        Assert.Empty(s.Favoritos);
        Assert.Equal(AppTheme.Auto, s.Tema);
    }

    [Fact]
    public void OutraVersao_VoltaAoPadrao()
    {
        var s = AppSettings.Parse("{\"Versao\":2,\"Favoritos\":[\"a\"],\"Tema\":\"escuro\"}", out var invalido);
        Assert.True(invalido);
        Assert.Empty(s.Favoritos);
        Assert.Equal(AppTheme.Auto, s.Tema);
    }

    [Theory]
    [InlineData("roxo")]
    [InlineData("")]
    [InlineData(null)]
    public void TemaDesconhecido_ViraAutomatico(string? tema)
    {
        var json = tema is null ? "{\"Versao\":1,\"Tema\":null}" : "{\"Versao\":1,\"Tema\":\"" + tema + "\"}";
        Assert.Equal(AppTheme.Auto, AppSettings.Parse(json).Tema);
    }

    [Theory]
    [InlineData("claro")]
    [InlineData("escuro")]
    [InlineData("automatico")]
    public void TemaValido_Permanece(string tema) =>
        Assert.Equal(tema, AppSettings.Parse("{\"Versao\":1,\"Tema\":\"" + tema + "\"}").Tema);

    [Fact]
    public void MaisDeTresFavoritos_CortaEmTres()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"Favoritos\":[\"a\",\"b\",\"c\",\"d\",\"e\"]}");
        Assert.Equal(new[] { "a", "b", "c" }, s.Favoritos);
    }

    [Fact]
    public void RepetidosEVazios_SaemDaLista()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"Favoritos\":[\"a\",\"\",\"a\",\"  \",\"b\",null]}");
        Assert.Equal(new[] { "a", "b" }, s.Favoritos);
    }

    [Fact]
    public void FavoritosNulo_ViraListaVazia() =>
        Assert.Empty(AppSettings.Parse("{\"Versao\":1,\"Favoritos\":null}").Favoritos);

    [Fact]
    public void NomesDeQuemNaoEhFavorito_SaemDoArquivo()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"Favoritos\":[\"a\"],\"NomesFavoritos\":{\"a\":\"Fone\",\"x\":\"Outra\"}}");
        Assert.Equal("Fone", s.NomesFavoritos["a"]);
        Assert.False(s.NomesFavoritos.ContainsKey("x"));
    }

    [Fact]
    public void IdaEVolta_PreservaTudo()
    {
        var original = new AppSettings
        {
            Favoritos = new List<string> { "{0.0.0}.{abc}", "{0.0.0}.{def}" },
            NomesFavoritos = new Dictionary<string, string> { ["{0.0.0}.{abc}"] = "Headset", ["{0.0.0}.{def}"] = "Soundbar" },
            Tema = AppTheme.Dark,
        };
        var lido = AppSettings.Parse(original.Serialize(), out var invalido);
        Assert.False(invalido);
        Assert.Equal(original.Favoritos, lido.Favoritos);
        Assert.Equal(original.NomesFavoritos, lido.NomesFavoritos);
        Assert.Equal(AppTheme.Dark, lido.Tema);
        Assert.Equal(1, lido.Versao);
    }

    [Fact]
    public void Serialize_TemAsChavesEsperadas()
    {
        var json = AppSettings.Default.Serialize();
        Assert.Contains("\"Versao\": 1", json);
        Assert.Contains("\"Favoritos\"", json);
        Assert.Contains("\"Tema\": \"automatico\"", json);
    }
}
