using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class SessionTrackerTests
{
    private static SessionInfo S(string id, string name, string? exe, float vol = 0.5f, bool muted = false,
        SessionState state = SessionState.Active, bool system = false) =>
        new(id, system ? 0 : 100, exe, name, vol, muted, state, system);

    private static List<string> Names(SessionTracker t) => t.Groups.Select(g => g.Name).ToList();

    [Fact]
    public void SessaoNovaCriaUmGrupo()
    {
        var t = new SessionTracker();
        var avisos = new List<GroupChange>();
        t.Changed += avisos.Add;
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        Assert.Single(t.Groups);
        Assert.Single(avisos);
        Assert.Equal(GroupChangeKind.Added, avisos[0].Kind);
        Assert.Equal(0, avisos[0].Index);
    }

    [Fact]
    public void DuasSessoesDoMesmoExeFicamNumGrupoSo()
    {
        var t = new SessionTracker();
        t.Add(S("a1", "Alfa", @"C:\Apps\Alfa.exe"));
        t.Add(S("a2", "Alfa", @"c:\apps\alfa.EXE"));
        Assert.Single(t.Groups);
        Assert.Equal(2, t.Groups[0].SessionCount);
        Assert.Equal(2, t.SessionIdsOf(t.Groups[0].Key).Count);
    }

    [Fact]
    public void ExesDiferentesFicamEmGruposSeparados()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        t.Add(S("b", "Beta", @"C:\b.exe"));
        Assert.Equal(2, t.Groups.Count);
    }

    [Fact]
    public void SemCaminhoAChaveEOIdDaSessao()
    {
        var t = new SessionTracker();
        t.Add(S("sessao-1", "Alfa", null));
        t.Add(S("sessao-2", "Alfa", ""));
        Assert.Equal(2, t.Groups.Count);
        Assert.Equal("sessao-1", t.KeyOf("sessao-1"));
    }

    [Fact]
    public void SonsDoSistemaFicamSemprePorUltimo()
    {
        var t = new SessionTracker();
        t.Add(S("sys", "Sons do sistema", null, system: true));
        t.Add(S("z", "Zebra", @"C:\z.exe"));
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        Assert.Equal(new[] { "Alfa", "Zebra", "Sons do sistema" }, Names(t));
    }

    [Fact]
    public void OrdemAlfabeticaSemDiferenciarMaiusculas()
    {
        var t = new SessionTracker();
        t.Add(S("c", "charlie", @"C:\c.exe"));
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        t.Add(S("b", "BRAVO", @"C:\b.exe"));
        Assert.Equal(new[] { "Alfa", "BRAVO", "charlie" }, Names(t));
    }

    [Fact]
    public void VolumeDoGrupoVemDaSessaoMaisAntiga()
    {
        var t = new SessionTracker();
        t.Add(S("a1", "Alfa", @"C:\a.exe", vol: 0.3f));
        t.Add(S("a2", "Alfa", @"C:\a.exe", vol: 0.9f));
        Assert.Equal(0.3f, t.Groups[0].Volume);
        t.UpdateVolume("a2", 0.1f, false);
        Assert.Equal(0.3f, t.Groups[0].Volume);
        t.UpdateVolume("a1", 0.7f, false);
        Assert.Equal(0.7f, t.Groups[0].Volume);
    }

    [Fact]
    public void MudoDoGrupoSoComTodasMudas()
    {
        var t = new SessionTracker();
        t.Add(S("a1", "Alfa", @"C:\a.exe", muted: true));
        Assert.True(t.Groups[0].Muted);
        t.Add(S("a2", "Alfa", @"C:\a.exe", muted: false));
        Assert.False(t.Groups[0].Muted);
        t.UpdateVolume("a2", 0.5f, true);
        Assert.True(t.Groups[0].Muted);
    }

    [Fact]
    public void SessaoInativaContinua()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        t.UpdateState("a", SessionState.Inactive);
        Assert.Single(t.Groups);
    }

    [Fact]
    public void SessaoInativaJaNaChegadaEntra()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe", state: SessionState.Inactive));
        Assert.Single(t.Groups);
    }

    [Fact]
    public void ExpiradaESaem()
    {
        var t = new SessionTracker();
        t.Add(S("a1", "Alfa", @"C:\a.exe"));
        t.Add(S("a2", "Alfa", @"C:\a.exe"));
        t.UpdateState("a1", SessionState.Expired);
        Assert.Equal(1, t.Groups[0].SessionCount);
        t.Remove("a2"); // desconectada
        Assert.Empty(t.Groups);
    }

    [Fact]
    public void GrupoSomeQuandoSaiAUltimaSessao()
    {
        var t = new SessionTracker();
        var avisos = new List<GroupChange>();
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        t.Changed += avisos.Add;
        t.Remove("a");
        Assert.Empty(t.Groups);
        Assert.Equal(GroupChangeKind.Removed, Assert.Single(avisos).Kind);
        Assert.Null(t.KeyOf("a"));
    }

    [Fact]
    public void MudarONomeReposiciona()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        t.Add(S("b", "Beta", @"C:\b.exe"));
        var avisos = new List<GroupChange>();
        t.Changed += avisos.Add;
        t.UpdateName("a", "Zulu");
        Assert.Equal(new[] { "Beta", "Zulu" }, Names(t));
        var c = Assert.Single(avisos);
        Assert.Equal(0, c.OldIndex);
        Assert.Equal(1, c.Index);
    }

    [Fact]
    public void MudarVolumeNaoMudaALinhaDeLugar()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe"));
        t.Add(S("b", "Alfa", @"C:\b.exe")); // mesmo nome, empate
        t.UpdateVolume("a", 0.2f, false);
        Assert.Equal(new[] { @"c:\a.exe", @"c:\b.exe" }, t.Groups.Select(g => g.Key).ToArray());
    }

    [Fact]
    public void IdRepetidoEmAddNaoDuplica()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe", vol: 0.2f));
        t.Add(S("a", "Alfa", @"C:\a.exe", vol: 0.8f));
        Assert.Equal(1, t.SessionCount);
        Assert.Equal(1, t.Groups[0].SessionCount);
        Assert.Equal(0.8f, t.Groups[0].Volume);
    }

    [Fact]
    public void EventoDeSessaoDesconhecidaEIgnorado()
    {
        var t = new SessionTracker();
        var avisos = new List<GroupChange>();
        t.Changed += avisos.Add;
        t.UpdateVolume("x", 0.5f, false);
        t.UpdateState("x", SessionState.Inactive);
        t.UpdateName("x", "Nome");
        t.Remove("x");
        Assert.Empty(avisos);
        Assert.Empty(t.Groups);
    }

    [Fact]
    public void AvisoSoQuandoAlgoMuda()
    {
        var t = new SessionTracker();
        t.Add(S("a", "Alfa", @"C:\a.exe", vol: 0.5f));
        var avisos = new List<GroupChange>();
        t.Changed += avisos.Add;
        t.UpdateVolume("a", 0.5f, false);
        t.UpdateState("a", SessionState.Inactive); // o estado não aparece na foto do grupo
        Assert.Empty(avisos);
    }

    private static SessionInfo P(string id, int pid, string exe) =>
        new(id, pid, exe, "App", 0.5f, false, SessionState.Active, false);

    [Fact]
    public void ProcessIdsNaoRepeteNemTrazPidZero()
    {
        var t = new SessionTracker();
        t.Add(P("a1", 10, @"C:\Apps\a.exe"));
        t.Add(P("a2", 10, @"C:\Apps\a.exe"));
        t.Add(P("a3", 20, @"C:\Apps\a.exe"));
        t.Add(P("a4", 0, @"C:\Apps\a.exe"));
        var key = t.Groups[0].Key;
        Assert.Equal(new[] { 10, 20 }, t.ProcessIds(key));
    }

    [Fact]
    public void ProcessIdsDeGrupoInexistenteEhVazio()
    {
        var t = new SessionTracker();
        Assert.Empty(t.ProcessIds("nao-existe"));
    }

    [Fact]
    public void ProcessIdsDeSonsDoSistemaEhVazio()
    {
        var t = new SessionTracker();
        t.Add(S("sys", "Sons do sistema", null, system: true));
        Assert.Empty(t.ProcessIds(SessionTracker.SystemKey));
    }
}
