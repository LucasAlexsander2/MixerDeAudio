using MixerDeAudio.Remote;
using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

/// <summary>Fase 16, D11 e P3: validade de 30 dias, limite de 5 e "Esquecer", com o relógio passado por parâmetro.</summary>
public class PhoneListRulesTests
{
    private static readonly DateTime Agora = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static PairedPhone Phone(string hash, double diasAtras) =>
        new() { Hash = hash, Nome = "Android", UltimoUso = Agora.AddDays(-diasAtras) };

    [Theory]
    [InlineData(0, false)]
    [InlineData(29.9, false)]
    [InlineData(30, false)]
    [InlineData(30.01, true)]
    [InlineData(90, true)]
    public void Validade_30Dias(double diasAtras, bool expirou) =>
        Assert.Equal(expirou, PhoneListRules.IsExpired(Agora.AddDays(-diasAtras), Agora));

    [Fact]
    public void RemoveExpired_TiraSoOsVelhos_EContaOsRemovidos()
    {
        var lista = new[] { Phone("a", 1), Phone("b", 31), Phone("c", 10), Phone("d", 60) };
        var validos = PhoneListRules.RemoveExpired(lista, Agora, out var removidos);
        Assert.Equal(new[] { "a", "c" }, validos.Select(p => p.Hash));
        Assert.Equal(2, removidos);
    }

    [Fact]
    public void RemoveExpired_SemExpirados_NaoMuda()
    {
        var validos = PhoneListRules.RemoveExpired(new[] { Phone("a", 1) }, Agora, out var removidos);
        Assert.Single(validos);
        Assert.Equal(0, removidos);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public void Limite_5Celulares(int quantos, bool atingido)
    {
        var lista = Enumerable.Range(0, quantos).Select(i => Phone("h" + i, 1)).ToList();
        Assert.Equal(atingido, PhoneListRules.LimitReached(lista, Agora));
    }

    [Fact]
    public void Limite_NaoContaOsExpirados()
    {
        var lista = Enumerable.Range(0, 4).Select(i => Phone("h" + i, 1)).Concat(new[] { Phone("velho", 45) }).ToList();
        Assert.False(PhoneListRules.LimitReached(lista, Agora));
    }

    [Fact]
    public void Esquecer_TiraSoOCelularCerto()
    {
        var lista = new[] { Phone("a", 1), Phone("b", 2), Phone("c", 3) };
        Assert.Equal(new[] { "a", "c" }, PhoneListRules.Forget(lista, "b").Select(p => p.Hash));
        Assert.Equal(3, PhoneListRules.Forget(lista, "nao-existe").Count);
    }

    [Theory]
    [InlineData(0, "agora")]
    [InlineData(0.0003, "agora")]
    [InlineData(0.01, "há 14 min")]
    [InlineData(0.125, "há 3 h")]
    [InlineData(1.2, "ontem")]
    [InlineData(12.5, "há 12 dias")]
    public void UltimoUso_TextoCurto(double diasAtras, string esperado) =>
        Assert.Equal(esperado, PhoneListRules.LastUseText(Agora.AddDays(-diasAtras), Agora));
}
