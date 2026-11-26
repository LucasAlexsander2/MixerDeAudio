using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

/// <summary>Fase 16b: nomes de arquivo (D12), energia "agora" (D13) e o código do celular protegido (D14).</summary>
public class Fase16bTests
{
    // ---- D12: caracteres Cf e extensões executáveis ----

    [Fact]
    public void Nome_SemCaracteresCf()
    {
        // U+202E (RLO), U+200E (LRM), U+2066 (LRI), U+FEFF (BOM) e U+E0001 (tag, fora do plano básico)
        var raw = "foto‮gpj.exe‎⁦﻿" + char.ConvertFromUtf32(0xE0001);
        var limpo = FileNameRules.Clean(raw);
        Assert.Equal("fotogpj.exe", limpo);
        Assert.DoesNotContain(limpo, c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
    }

    [Fact]
    public void Nome_ComEmojiEAcento_ContinuaIgual() => Assert.Equal("café 😀.png", FileNameRules.Clean("café 😀.png"));

    [Theory]
    [InlineData(".exe")]
    [InlineData(".msi")]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    [InlineData(".ps1")]
    [InlineData(".vbs")]
    [InlineData(".js")]
    [InlineData(".lnk")]
    [InlineData(".url")]
    [InlineData(".scr")]
    [InlineData(".dll")]
    [InlineData(".com")]
    [InlineData(".pif")]
    [InlineData(".hta")]
    [InlineData(".jar")]
    [InlineData(".reg")]
    public void Executavel_GanhaTxt(string ext)
    {
        Assert.Equal("setup" + ext + ".txt", FileNameRules.ForReceive("setup" + ext, out var renomeado));
        Assert.True(renomeado);
    }

    [Fact]
    public void Executavel_MaiusculaTambem()
    {
        Assert.Equal("SETUP.EXE.txt", FileNameRules.ForReceive("SETUP.EXE", out var renomeado));
        Assert.True(renomeado);
    }

    [Theory]
    [InlineData("foto.jpg")]
    [InlineData("nota.txt")]
    [InlineData("relatorio.pdf")]
    [InlineData("sem-extensao")]
    [InlineData("exe")]
    public void NaoExecutavel_FicaIgual(string nome)
    {
        Assert.Equal(nome, FileNameRules.ForReceive(nome, out var renomeado));
        Assert.False(renomeado);
    }

    [Fact]
    public void RloComExe_ViraExeTxtSemOCaractere()
    {
        // o caso do roteiro 16b.1
        Assert.Equal("fotogpj.exe.txt", FileNameRules.ForReceive("foto‮gpj.exe", out var renomeado));
        Assert.True(renomeado);
    }

    // ---- D13: energia "agora" ----

    [Fact]
    public void Agora_Vira10Segundos()
    {
        Assert.Equal(10, PowerRules.NowSeconds);
        Assert.Equal(10, PowerRules.EffectiveSeconds(0));
        Assert.Equal(5, PowerRules.EffectiveSeconds(5));
        Assert.Equal(3600, PowerRules.EffectiveSeconds(3600));
    }

    [Fact]
    public void Agora_ExecutaDepoisDe10Segundos_ECancelaAntes()
    {
        var agora = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var t = new PowerTimer(() => agora);
        t.Schedule(PowerAction.Shutdown, PowerRules.EffectiveSeconds(0));
        Assert.Null(t.Plan!.WarnAt); // sem o aviso de 1 min: a janelinha já avisa
        agora = agora.AddSeconds(9);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind);
        Assert.True(t.Cancel()); // o Cancelar da janelinha, do celular ou da bandeja
        agora = agora.AddSeconds(5);
        Assert.Equal(PowerEventKind.None, t.Poll().Kind); // nada executa depois de cancelar

        t.Schedule(PowerAction.Restart, PowerRules.EffectiveSeconds(0));
        agora = agora.AddSeconds(10);
        var ev = t.Poll();
        Assert.Equal(PowerEventKind.Execute, ev.Kind);
        Assert.Equal(PowerAction.Restart, ev.Action);
    }

    [Theory]
    [InlineData(PowerAction.Shutdown, 10, "Desligando em 10 s")]
    [InlineData(PowerAction.Restart, 7, "Reiniciando em 7 s")]
    [InlineData(PowerAction.Suspend, 3, "Suspendendo em 3 s")]
    [InlineData(PowerAction.Lock, 1, "Bloqueando em 1 s")]
    public void TextoDaJanelinha(PowerAction acao, int seg, string esperado) =>
        Assert.Equal(esperado, PowerRules.NowPromptText(acao, seg));

    // ---- D14: código protegido com a DPAPI ----

    [Fact]
    public void Dpapi_IdaEVolta_ESemTextoClaro()
    {
        var protegido = CodeProtector.Protect("123456");
        Assert.NotNull(protegido);
        Assert.DoesNotContain("123456", protegido);
        Assert.Equal("123456", CodeProtector.Unprotect(protegido));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("isto nao e base64!")]
    [InlineData("AAAA")]
    public void Dpapi_ValorInvalido_DevolveNulo(string? valor) => Assert.Null(CodeProtector.Unprotect(valor));

    [Fact]
    public void Serializar_GravaSoOProtegido()
    {
        var json = new AppSettings { CelularLigado = true, CodigoCelular = "654321" }.Serialize();
        using var doc = JsonDocument.Parse(json);
        Assert.DoesNotContain("654321", json);
        Assert.False(doc.RootElement.TryGetProperty("CodigoCelular", out _));
        Assert.True(doc.RootElement.TryGetProperty("CodigoCelularProtegido", out var p));
        Assert.Equal("654321", CodeProtector.Unprotect(p.GetString()));
    }

    [Fact]
    public void Ler_ProtegidoVoltaEmTextoClaroNaMemoria()
    {
        var lido = AppSettings.Parse(new AppSettings { CodigoCelular = "000042" }.Serialize(), out var invalido, out var claro);
        Assert.False(invalido);
        Assert.False(claro); // nada a migrar
        Assert.Equal("000042", lido.CodigoCelular);
        Assert.Null(lido.CodigoCelularProtegido);
    }

    [Fact]
    public void Migracao_TextoClaroEhLido_ESaiProtegidoNaProximaGravacao()
    {
        // o que os roteiros gravam
        var lido = AppSettings.Parse("{\"Versao\":1,\"CelularLigado\":true,\"CodigoCelular\":\"123456\"}", out var invalido, out var claro);
        Assert.False(invalido);
        Assert.True(claro);
        Assert.Equal("123456", lido.CodigoCelular);

        var regravado = lido.Serialize();
        Assert.DoesNotContain("123456", regravado);
        var denovo = AppSettings.Parse(regravado, out _, out var aindaClaro);
        Assert.False(aindaClaro);
        Assert.Equal("123456", denovo.CodigoCelular);
    }

    [Fact]
    public void Protegido_NaoAbre_CodigoFicaNulo()
    {
        var lido = AppSettings.Parse("{\"Versao\":1,\"CodigoCelularProtegido\":\"AAAA\"}");
        Assert.Null(lido.CodigoCelular);
    }

    [Fact]
    public void SemCodigo_NaoGravaNenhumCampoDeCodigo()
    {
        var json = new AppSettings().Serialize();
        Assert.DoesNotContain("CodigoCelular", json);
    }
}
