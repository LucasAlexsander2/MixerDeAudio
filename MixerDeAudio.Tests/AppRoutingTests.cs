using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class AppRoutingTests
{
    private const string Mm = "{0.0.0.00000000}.{aaaa1111-2222-3333-4444-555566667777}";
    private const string Policy = @"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{aaaa1111-2222-3333-4444-555566667777}#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    // ---- Ids (D2) ----

    [Fact]
    public void ToPolicyIdMontaOCaminho() => Assert.Equal(Policy, AppRouting.ToPolicyId(Mm));

    [Fact]
    public void IdentificadorIdaEVolta() => Assert.Equal(Mm, AppRouting.FromPolicyId(AppRouting.ToPolicyId(Mm)));

    [Fact]
    public void FromPolicyIdTolerarMaiusculasEMinusculas() =>
        Assert.Equal(Mm, AppRouting.FromPolicyId(Policy.ToUpperInvariant().Replace(Mm.ToUpperInvariant(), Mm)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("texto qualquer")]
    [InlineData(@"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{x}")]
    [InlineData(@"\\?\SWD#MMDEVAPI##{e6327cad-dcec-4949-ae8a-991e976a79d2}")]
    [InlineData(@"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{x}#{2eef81be-33fa-4800-9670-1cd474972c3f}")]
    public void FromPolicyIdForaDoFormatoDevolveNulo(string? texto) => Assert.Null(AppRouting.FromPolicyId(texto));

    // ---- Effective (D7) ----

    [Fact]
    public void SemEscolhaVaiParaAPadrao() => Assert.Equal("P", AppRouting.Effective(null, "P", new[] { "P", "X" }));

    [Fact]
    public void EscolhaAtivaEhAEfetiva() => Assert.Equal("X", AppRouting.Effective("X", "P", new[] { "P", "X" }));

    [Fact]
    public void EscolhaDesligadaCaiNaPadrao() => Assert.Equal("P", AppRouting.Effective("Z", "P", new[] { "P", "X" }));

    [Fact]
    public void SemPadraoENemEscolhaAtivaDevolveNulo() => Assert.Null(AppRouting.Effective("Z", null, new[] { "X" }));

    [Fact]
    public void EscolhaSemPadraoAindaValeSeEstiverAtiva() => Assert.Equal("X", AppRouting.Effective("X", null, new[] { "X" }));

    // ---- OnClick (D7, P1) ----

    [Fact]
    public void CliqueNaEfetivaNaoFazNada()
    {
        Assert.Equal(RouteActionKind.None, AppRouting.OnClick("P", null, "P").Kind);
        Assert.Equal(RouteActionKind.None, AppRouting.OnClick("X", "X", "P").Kind);
    }

    [Fact]
    public void CliqueNaPadraoApagaAEscolha()
    {
        var a = AppRouting.OnClick("P", "X", "P");
        Assert.Equal(RouteActionKind.Clear, a.Kind);
    }

    [Fact]
    public void CliqueEmOutraSaidaGrava()
    {
        var a = AppRouting.OnClick("X", null, "P");
        Assert.Equal(RouteActionKind.Set, a.Kind);
        Assert.Equal("X", a.DeviceId);
        var b = AppRouting.OnClick("Y", "X", "P");
        Assert.Equal(RouteActionKind.Set, b.Kind);
        Assert.Equal("Y", b.DeviceId);
    }

    // ---- ShowButtons (D7) ----

    [Fact]
    public void BotoesSoComDuasSaidasOuMais()
    {
        Assert.True(AppRouting.ShowButtons(false, true, 2, true));
        Assert.True(AppRouting.ShowButtons(false, true, 3, true));
        Assert.False(AppRouting.ShowButtons(false, true, 1, true));
        Assert.False(AppRouting.ShowButtons(false, true, 0, true));
    }

    [Fact]
    public void SonsDoSistemaSemPidOuSemServicoNaoTemBotoes()
    {
        Assert.False(AppRouting.ShowButtons(true, true, 2, true));
        Assert.False(AppRouting.ShowButtons(false, false, 2, true));
        Assert.False(AppRouting.ShowButtons(false, true, 2, false));
    }
}
