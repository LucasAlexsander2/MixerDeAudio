using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

/// <summary>
/// Só a regra pura, com caminhos e estados falsos: nada aqui toca o disco, o registro nem o desinstalador real.
/// </summary>
public class InnoMigrationTests
{
    private const string Local = @"C:\Falso\AppData\Local";
    private const string Folder = @"C:\Falso\AppData\Local\Programs\MixerDeAudio";
    private const string NewExe = @"C:\Falso\AppData\Local\MixerDeAudioApp\current\MixerDeAudio.exe";

    [Fact]
    public void PastaAntiga_EmProgramsMixerDeAudio() =>
        Assert.Equal(Folder, InnoMigration.LegacyFolder(Local));

    [Fact]
    public void SemDesinstalador_NaoMigra()
    {
        var plan = InnoMigration.Plan(Local, _ => false, runWasEnabled: true);
        Assert.False(plan.Migrate);
        Assert.False(plan.RestoreRun);
    }

    [Fact]
    public void ComDesinstalador_Migra_ComOsCaminhosCertos()
    {
        var consultados = new List<string>();
        var plan = InnoMigration.Plan(Local, p => { consultados.Add(p); return true; }, runWasEnabled: false);
        Assert.True(plan.Migrate);
        Assert.Equal(Folder + @"\unins000.exe", plan.Uninstaller);
        Assert.Equal(Folder + @"\MixerDeAudio.exe", plan.OldExe);
        Assert.Equal([Folder + @"\unins000.exe"], consultados);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RefazIniciarComOWindows_SoSeEstavaLigado(bool estavaLigado, bool esperado)
    {
        var plan = InnoMigration.Plan(Local, _ => true, estavaLigado);
        Assert.Equal(esperado, plan.RestoreRun);
    }

    [Fact]
    public void SemInstalacaoAntiga_NuncaRefazOIniciar() =>
        Assert.False(InnoMigration.Plan(Local, _ => false, true).RestoreRun);

    [Fact]
    public void Desinstalador_ArgumentosSilenciosos() =>
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", InnoMigration.UninstallerArguments);

    // ---- Estado do "Iniciar com o Windows" lido antes ----

    [Fact]
    public void EstavaLigado_ValorExisteENaoFoiDesligado()
    {
        Assert.True(StartupRules.WasEnabled("\"" + Folder + "\\MixerDeAudio.exe\"", null));
        Assert.True(StartupRules.WasEnabled("x", [0x02, 0, 0, 0]));
        Assert.False(StartupRules.WasEnabled("x", [0x03, 0, 0, 0]));
        Assert.False(StartupRules.WasEnabled(null, null));
        Assert.False(StartupRules.WasEnabled("  ", null));
    }

    // ---- Caminho novo do Run ----

    [Fact]
    public void ValorNovoDoRun_CaminhoEntreAspas() =>
        Assert.Equal("\"" + NewExe + "\"", StartupRules.BuildRunValue(NewExe));

    [Fact]
    public void Corrigir_RunApontandoParaOAntigo_VaiParaOCaminhoNovo() =>
        Assert.Equal("\"" + NewExe + "\"", StartupRules.RepointedValue("\"" + Folder + "\\MixerDeAudio.exe\"", NewExe));

    [Fact]
    public void Corrigir_JaApontaParaOAtual_NaoMexe() =>
        Assert.Null(StartupRules.RepointedValue("\"" + NewExe + "\"", NewExe));

    [Fact]
    public void Corrigir_SemValor_NaoLigaSozinho()
    {
        Assert.Null(StartupRules.RepointedValue(null, NewExe));
        Assert.Null(StartupRules.RepointedValue("", NewExe));
        Assert.Null(StartupRules.RepointedValue("\"" + Folder + "\\MixerDeAudio.exe\"", null));
    }
}
