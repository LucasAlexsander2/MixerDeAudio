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
    public void ArquivoAntigo_SemCamposDoCelular_AindaFunciona()
    {
        var s = AppSettings.Parse("{\"Versao\":1,\"Favoritos\":[\"a\"],\"Tema\":\"escuro\"}", out var invalido);
        Assert.False(invalido);
        Assert.False(s.CelularLigado);
        Assert.Null(s.CodigoCelular);
        Assert.Empty(s.ChavesCelular);
        Assert.Equal(new[] { "a" }, s.Favoritos);
    }

    [Fact]
    public void CamposDoCelular_IdaEVolta()
    {
        var uso = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
        var original = new AppSettings
        {
            CelularLigado = true,
            CodigoCelular = "012345",
            ChavesCelular = new List<PairedPhone> { new() { Hash = "h1", Nome = "Android", UltimoUso = uso } },
        };
        var lido = AppSettings.Parse(original.Serialize());
        Assert.True(lido.CelularLigado);
        Assert.Equal("012345", lido.CodigoCelular);
        var p = Assert.Single(lido.ChavesCelular);
        Assert.Equal("h1", p.Hash);
        Assert.Equal("Android", p.Nome);
        Assert.Equal(uso, p.UltimoUso.ToUniversalTime());
        Assert.Equal(1, lido.Versao);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("")]
    public void CodigoInvalido_Some(string codigo) =>
        Assert.Null(AppSettings.Parse("{\"Versao\":1,\"CodigoCelular\":\"" + codigo + "\"}").CodigoCelular);

    [Fact]
    public void ChavesCelular_NoMaximo8_SaemAsMaisAntigas()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var lista = new List<PairedPhone>();
        for (var i = 0; i < 10; i++) lista.Add(new PairedPhone { Hash = "h" + i, Nome = "n", UltimoUso = baseTime.AddDays(i) });
        var s = new AppSettings { ChavesCelular = lista }.Normalized();
        Assert.Equal(AppSettings.MaxPhones, s.ChavesCelular.Count);
        Assert.DoesNotContain(s.ChavesCelular, p => p.Hash is "h0" or "h1");
        Assert.Contains(s.ChavesCelular, p => p.Hash == "h9");
    }

    [Fact]
    public void ChavesCelular_SemHashVazioNemRepetido()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var s = new AppSettings
        {
            ChavesCelular = new List<PairedPhone>
            {
                new() { Hash = "", Nome = "x", UltimoUso = t },
                new() { Hash = "a", Nome = "velho", UltimoUso = t },
                new() { Hash = "a", Nome = "novo", UltimoUso = t.AddDays(1) },
            },
        }.Normalized();
        var p = Assert.Single(s.ChavesCelular);
        Assert.Equal("novo", p.Nome);
    }

    [Fact]
    public void ChavesCelularNulo_ViraListaVazia() =>
        Assert.Empty(AppSettings.Parse("{\"Versao\":1,\"ChavesCelular\":null}").ChavesCelular);

    [Fact]
    public void Serialize_TemAsChavesEsperadas()
    {
        var json = AppSettings.Default.Serialize();
        Assert.Contains("\"Versao\": 1", json);
        Assert.Contains("\"Favoritos\"", json);
        Assert.Contains("\"Tema\": \"automatico\"", json);
    }
}
