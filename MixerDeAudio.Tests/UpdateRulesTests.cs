using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

public class UpdateRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    // ---- Regra das 6 h ----

    [Fact]
    public void NuncaVerificou_Verifica() => Assert.True(UpdateRules.ShouldCheck(Now, null, true));

    [Fact]
    public void MenosDeSeisHoras_NaoVerifica() => Assert.False(UpdateRules.ShouldCheck(Now, Now.AddHours(-5).AddMinutes(-59), true));

    [Fact]
    public void SeisHorasExatas_Verifica() => Assert.True(UpdateRules.ShouldCheck(Now, Now.AddHours(-6), true));

    [Fact]
    public void MaisDeSeisHoras_Verifica() => Assert.True(UpdateRules.ShouldCheck(Now, Now.AddDays(-2), true));

    [Fact]
    public void OpcaoDesligada_NuncaVerifica()
    {
        Assert.False(UpdateRules.ShouldCheck(Now, null, false));
        Assert.False(UpdateRules.ShouldCheck(Now, Now.AddDays(-2), false));
    }

    // ---- Balão uma vez por versão ----

    [Theory]
    [InlineData(null, "1.1.1", true)]
    [InlineData("", "1.1.1", true)]
    [InlineData("1.1.0", "1.1.1", true)]
    [InlineData("1.1.1", "1.1.1", false)]
    [InlineData(" 1.1.1 ", "1.1.1", false)]
    [InlineData("1.1.1", null, false)]
    [InlineData("1.1.1", "", false)]
    public void Balao_UmaVezPorVersao(string? ultimo, string? nova, bool esperado) =>
        Assert.Equal(esperado, UpdateRules.ShouldNotify(ultimo, nova));

    // ---- Comparar versões ----

    [Fact]
    public void Versoes_Ordem()
    {
        Assert.True(UpdateRules.CompareVersions("1.1.0", "1.1.1") < 0);
        Assert.True(UpdateRules.CompareVersions("1.1.1", "1.10.0") < 0);
        Assert.True(UpdateRules.CompareVersions("1.10.0", "1.1.1") > 0);
        Assert.True(UpdateRules.CompareVersions("2.0.0", "1.99.99") > 0);
    }

    [Theory]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("1.1", "1.1.0")]
    [InlineData("v1.1.0", "1.1.0")]
    [InlineData("1.1.0+abc123", "1.1.0")]
    [InlineData("1.1.0-beta", "1.1.0")]
    public void Versoes_Iguais(string a, string b) => Assert.Equal(0, UpdateRules.CompareVersions(a, b));

    // ---- Mensagem de erro ----

    [Fact]
    public void Erro_UmaLinhaECurto()
    {
        Assert.Equal("a b", UpdateRules.Summarize("a\r\nb"));
        Assert.Equal("erro desconhecido", UpdateRules.Summarize(null));
        var longo = UpdateRules.Summarize(new string('x', 200));
        Assert.Equal(80, longo.Length);
        Assert.EndsWith("...", longo);
    }

    // ---- Novidades ----

    private const string Json = """
        [
          { "versao": "1.1.0", "itens": [ { "icone": "E895", "titulo": "A", "texto": "tA" }, { "icone": "E713", "titulo": "B", "texto": "tB" } ] },
          { "versao": "1.1.1", "itens": [ { "icone": "E789", "titulo": "C", "texto": "tC" } ] }
        ]
        """;

    [Fact]
    public void Novidades_LeOJson()
    {
        var notes = UpdateRules.ParseNotes(Json);
        Assert.Equal(2, notes.Count);
        Assert.Equal("1.1.0", notes[0].Versao);
        Assert.Equal(2, notes[0].Itens.Count);
        Assert.Equal("E895", notes[0].Itens[0].Icone);
        Assert.Equal("tB", notes[0].Itens[1].Texto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{}")]
    public void Novidades_InvalidoViraListaVazia(string? json) => Assert.Empty(UpdateRules.ParseNotes(json));

    [Fact]
    public void Novidades_EscolheAVersao()
    {
        var notes = UpdateRules.ParseNotes(Json);
        Assert.Equal("C", UpdateRules.NotesFor(notes, "1.1.1")!.Itens[0].Titulo);
        Assert.Equal("A", UpdateRules.NotesFor(notes, "1.1.0+hash")!.Itens[0].Titulo);
        Assert.Null(UpdateRules.NotesFor(notes, "9.9.9"));
        Assert.Null(UpdateRules.NotesFor(notes, null));
    }

    [Fact]
    public void Novidades_MostraUmaVez()
    {
        // Veio da 1.0.0 (config existia, nada visto): mostra.
        Assert.True(UpdateRules.ShouldShowNotes(null, "1.1.0", configExisted: true));
        // Depois de mostrar, a versão fica vista: não mostra de novo.
        Assert.False(UpdateRules.ShouldShowNotes("1.1.0", "1.1.0", configExisted: true));
        // Atualizou para a 1.1.1: mostra outra vez.
        Assert.True(UpdateRules.ShouldShowNotes("1.1.0", "1.1.1", configExisted: true));
        // Voltou a uma versão mais antiga: não mostra.
        Assert.False(UpdateRules.ShouldShowNotes("1.1.1", "1.1.0", configExisted: true));
    }

    [Fact]
    public void Novidades_InstalacaoNovaNaoMostra()
    {
        Assert.False(UpdateRules.ShouldShowNotes(null, "1.1.0", configExisted: false));
        Assert.False(UpdateRules.ShouldShowNotes("", "1.1.0", configExisted: false));
        Assert.False(UpdateRules.ShouldShowNotes(null, null, configExisted: true));
    }

    [Fact]
    public void NovidadesDoApp_TemAVersaoAtualComDoisACincoItens()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Novidades.json"));
        var entry = UpdateRules.NotesFor(UpdateRules.ParseNotes(json), "1.1.0");
        Assert.NotNull(entry);
        Assert.InRange(entry!.Itens.Count, 2, 5);
        Assert.All(entry.Itens, i => Assert.False(string.IsNullOrWhiteSpace(i.Titulo + i.Texto + i.Icone)));
        Assert.Contains("\"versao\": \"1.1.0\"", json); // o workflow da 11b confere com este texto
    }

    // ---- Textos ----

    [Fact]
    public void Texto_CopiaNaoInstalada()
    {
        var s = new UpdateStatus(UpdateState.Unavailable, null, 0, null, null);
        Assert.Contains("indisponível nesta cópia", UpdateRules.StatusText(s, false));
        Assert.False(UpdateRules.Banner(s).Visible);
    }

    [Fact]
    public void Texto_Estados()
    {
        Assert.Contains("mais recente", UpdateRules.StatusText(new(UpdateState.Idle, null, 0, null, null), true));
        Assert.Contains("1.1.1 disponível", UpdateRules.StatusText(new(UpdateState.NewVersion, "1.1.1", 0, null, null), true));
        Assert.Contains("42%", UpdateRules.StatusText(new(UpdateState.Downloading, "1.1.1", 42, 8, null), true));
        Assert.Contains("Reinicie", UpdateRules.StatusText(new(UpdateState.Ready, "1.1.1", 100, null, null), true));
        Assert.Contains("falhou", UpdateRules.StatusText(new(UpdateState.Error, null, 0, null, "falhou"), true));
    }

    [Fact]
    public void Faixa_SoComVersaoNovaBaixandoOuPronta()
    {
        Assert.False(UpdateRules.Banner(new(UpdateState.Idle, null, 0, null, null)).Visible);
        Assert.False(UpdateRules.Banner(new(UpdateState.Checking, null, 0, null, null)).Visible);
        Assert.False(UpdateRules.Banner(new(UpdateState.Error, null, 0, null, "x")).Visible);
        var nova = UpdateRules.Banner(new(UpdateState.NewVersion, "1.1.1", 0, null, null));
        Assert.True(nova.Visible);
        Assert.Equal("Atualizar", nova.Button);
        Assert.Contains("1.1.1", nova.Text);
        var baixando = UpdateRules.Banner(new(UpdateState.Downloading, "1.1.1", 30, null, null));
        Assert.True(baixando.Visible);
        Assert.Null(baixando.Button);
        Assert.Equal("Reiniciar", UpdateRules.Banner(new(UpdateState.Ready, "1.1.1", 100, null, null)).Button);
    }

    [Fact]
    public void Estimativa_SemDadosEDepois()
    {
        var e = new DownloadEstimate();
        Assert.Null(e.Register(2, TimeSpan.FromSeconds(5)));
        Assert.Null(e.Register(50, TimeSpan.FromMilliseconds(500)));
        Assert.Equal(10, e.Register(50, TimeSpan.FromSeconds(10)));
        Assert.Equal("menos de 1 s", DownloadEstimate.Format(0.4));
        Assert.Equal("cerca de 8 s", DownloadEstimate.Format(8));
        Assert.Equal("cerca de 1 min 05 s", DownloadEstimate.Format(65));
    }

    // ---- Configuração nova ----

    [Fact]
    public void Config_PadroesELeitura()
    {
        var d = AppSettings.Default;
        Assert.True(d.AtualizarAuto);
        Assert.Null(d.UltimaVersaoVista);
        Assert.Null(d.BalaoVersao);
        var s = AppSettings.Parse((d with { AtualizarAuto = false, UltimaVersaoVista = " 1.1.0 ", BalaoVersao = "1.1.1" }).Serialize());
        Assert.False(s.AtualizarAuto);
        Assert.Equal("1.1.0", s.UltimaVersaoVista);
        Assert.Equal("1.1.1", s.BalaoVersao);
        // Configuração de versões antigas (sem os campos novos) mantém a conferência ligada.
        Assert.True(AppSettings.Parse("{\"versao\":1}").AtualizarAuto);
    }
}
