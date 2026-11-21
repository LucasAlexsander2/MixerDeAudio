using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class MediaMatcherTests
{
    private static MediaCandidate M(string aumid, bool playing = true, string? shellExe = null) => new(aumid, playing, shellExe);

    private static GroupCandidate G(string exe, params string[] aumids) => new(exe.ToLowerInvariant(), exe, aumids);

    [Fact]
    public void Regra1_AumidIgualAoDeUmProcessoDoGrupo()
    {
        var r = MediaMatcher.Match([M("SpotifyAB.SpotifyMusic_x!Spotify")], [G(@"C:\Apps\spotify.exe", "SpotifyAB.SpotifyMusic_x!Spotify")]);
        Assert.Equal(@"c:\apps\spotify.exe", r["SpotifyAB.SpotifyMusic_x!Spotify"]);
    }

    [Fact]
    public void Regra2_ExeDaPastaDeAppsIgualAoDoGrupo()
    {
        var r = MediaMatcher.Match([M("Opera.1", shellExe: @"C:\Opera GX\opera.exe")], [G(@"c:\opera gx\OPERA.exe")]);
        Assert.Equal(@"c:\opera gx\opera.exe", r["Opera.1"]);
    }

    [Fact]
    public void Regra3_MesmoNomeDeArquivoEmOutraPasta()
    {
        var r = MediaMatcher.Match([M("com.squirrel.Discord", shellExe: @"C:\Users\x\AppData\Local\Discord\Update.exe")],
            [G(@"C:\Users\x\AppData\Local\Discord\app-1.0\Update.exe")]);
        Assert.Single(r);
    }

    [Fact]
    public void Regra4_AumidIgualAoNomeDoExe()
    {
        var comExt = MediaMatcher.Match([M("fonte.exe")], [G(@"C:\t\fonte.exe")]);
        var semExt = MediaMatcher.Match([M("FONTE")], [G(@"C:\t\fonte.exe")]);
        Assert.Single(comExt);
        Assert.Single(semExt);
    }

    [Fact]
    public void SemRegraBatendo_NaoLiga()
    {
        var r = MediaMatcher.Match([M("Outro.App", shellExe: @"C:\x\outro.exe")], [G(@"C:\y\chrome.exe")]);
        Assert.Empty(r);
    }

    [Fact]
    public void ExeNuloNaoLigaPorNomeNemPorCaminho()
    {
        var r = MediaMatcher.Match([M("a", shellExe: null)], [new GroupCandidate("sessao-1", null, [])]);
        Assert.Empty(r);
    }

    [Fact]
    public void RegraMaisForteGanhaDaMaisFraca()
    {
        // A mídia B liga ao grupo por regra 2; a A só por regra 3 (mesmo nome de arquivo): o grupo fica com a B.
        var grupo = G(@"C:\Apps\player.exe");
        var r = MediaMatcher.Match(
            [M("A", playing: true, shellExe: @"D:\Outra\player.exe"), M("B", playing: false, shellExe: @"C:\Apps\player.exe")],
            [grupo]);
        Assert.Equal(grupo.Key, r["B"]);
        Assert.False(r.ContainsKey("A"));
    }

    [Fact]
    public void EmpateNaMesmaRegra_FicaAQueEstaTocando()
    {
        var grupo = G(@"C:\Apps\player.exe");
        var r = MediaMatcher.Match(
            [M("pausada", playing: false, shellExe: @"C:\Apps\player.exe"), M("tocando", playing: true, shellExe: @"C:\Apps\player.exe")],
            [grupo]);
        Assert.Single(r);
        Assert.Equal(grupo.Key, r["tocando"]);
    }

    [Fact]
    public void UmaMidiaLigaAUmaLinhaSo()
    {
        var r = MediaMatcher.Match([M("m", shellExe: @"C:\a\app.exe")], [G(@"C:\a\app.exe"), G(@"C:\b\app.exe")]);
        Assert.Single(r);
        Assert.Equal(@"c:\a\app.exe", r["m"]); // a regra 2 vence a 3
    }

    [Fact]
    public void DuasMidiasEmDuasLinhasLigamCadaUmaNaSua()
    {
        var r = MediaMatcher.Match(
            [M("chrome", shellExe: @"C:\c\chrome.exe"), M("msedge", shellExe: @"C:\e\msedge.exe")],
            [G(@"C:\e\msedge.exe"), G(@"C:\c\chrome.exe")]);
        Assert.Equal(@"c:\c\chrome.exe", r["chrome"]);
        Assert.Equal(@"c:\e\msedge.exe", r["msedge"]);
    }

    [Fact]
    public void SemLinha_NaoEntraNoResultado_EViraSoMidia()
    {
        var r = MediaMatcher.Match([M("SpotifyAB.SpotifyMusic_x!Spotify", playing: false)], []);
        Assert.Empty(r);
    }

    [Theory]
    [InlineData("Navegador Opera GX", "x", "Navegador Opera GX")]
    [InlineData(null, "tocar-b.exe", "tocar-b")]
    [InlineData(null, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "SpotifyAB.SpotifyMusic")]
    [InlineData("  ", "app.exe", "app")]
    public void NomeDaLinhaSoDeMidia(string? shellName, string aumid, string esperado) =>
        Assert.Equal(esperado, MediaMatcher.NameFor(aumid, shellName));
}

public class MediaInfoTests
{
    private static MediaInfo I(string titulo, string artista) =>
        new("a", titulo, artista, MediaStatus.Playing, true, true, true);

    [Fact]
    public void TextoTemTituloEArtista() => Assert.Equal("Faixa 1 · MX-teste", I("Faixa 1", "MX-teste").Text("App"));

    [Fact]
    public void SemArtistaSoOTitulo() => Assert.Equal("Faixa 1", I("Faixa 1", "").Text("App"));

    [Fact]
    public void TituloVazioViraONomeDoApp()
    {
        Assert.Equal("Opera GX", I("", "").Text("Opera GX"));
        Assert.Equal("Opera GX · Canal", I("  ", "Canal").Text("Opera GX"));
    }

    [Fact]
    public void ClearTiraEspacosENulo()
    {
        Assert.Equal("x", MediaInfo.Clean("  x \n"));
        Assert.Equal("", MediaInfo.Clean(null));
    }

    [Theory]
    // alternar, play, pausa, estado, esperado
    [InlineData(true, false, false, MediaStatus.Playing, true)]   // alternar basta
    [InlineData(true, false, false, MediaStatus.Paused, true)]
    [InlineData(false, true, false, MediaStatus.Paused, true)]    // pausado: vale o play
    [InlineData(false, false, true, MediaStatus.Paused, false)]   // pausado: a pausa não serve
    [InlineData(false, false, true, MediaStatus.Playing, true)]   // tocando: vale a pausa
    [InlineData(false, true, false, MediaStatus.Playing, false)]  // tocando: o play não serve
    [InlineData(false, true, false, MediaStatus.Other, true)]     // parada: vale o play
    [InlineData(false, false, false, MediaStatus.Playing, false)]
    public void PlayPausaDisponivel(bool alternar, bool play, bool pausa, MediaStatus estado, bool esperado) =>
        Assert.Equal(esperado, MediaInfo.PlayPauseAvailable(alternar, play, pausa, estado));

    [Fact]
    public void MidiaIgualPorValor() =>
        Assert.Equal(I("a", "b"), I("a", "b"));
}
