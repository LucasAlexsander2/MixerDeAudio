using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class DeviceTrackerTests
{
    private static DeviceInfo D(string id, string nome) => new(id, nome, nome + " (completo)", 0);

    private static List<string> Ids(DeviceTracker t) => t.Devices.Select(d => d.Id).ToList();

    [Fact]
    public void OrdemAlfabeticaSemDiferenciarMaiusculas()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "soundbar"));
        t.Upsert(D("2", "Headset"));
        t.Upsert(D("3", "Alto-falantes"));
        Assert.Equal(new[] { "3", "2", "1" }, Ids(t));
    }

    [Fact]
    public void MesmoNomeDesempataPeloId()
    {
        var t = new DeviceTracker();
        t.Upsert(D("b", "Fone"));
        t.Upsert(D("a", "fone"));
        Assert.Equal(new[] { "a", "b" }, Ids(t));
    }

    [Fact]
    public void SaidaQueEntraFicaNaPosicaoCerta()
    {
        var t = new DeviceTracker();
        var avisos = new List<DeviceChange>();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("3", "Gama"));
        t.Changed += avisos.Add;
        t.Upsert(D("2", "Beta"));
        Assert.Equal(new[] { "1", "2", "3" }, Ids(t));
        Assert.Single(avisos);
        Assert.Equal(DeviceChangeKind.Added, avisos[0].Kind);
        Assert.Equal(1, avisos[0].Index);
    }

    [Fact]
    public void SaidaQueSaiSome()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("2", "Beta"));
        var avisos = new List<DeviceChange>();
        t.Changed += avisos.Add;
        t.Remove("1");
        Assert.Equal(new[] { "2" }, Ids(t));
        Assert.Equal(DeviceChangeKind.Removed, avisos[0].Kind);
        Assert.Equal(0, avisos[0].OldIndex);
    }

    [Fact]
    public void RemoverInexistenteNaoAvisa()
    {
        var t = new DeviceTracker();
        var avisos = new List<DeviceChange>();
        t.Changed += avisos.Add;
        t.Remove("x");
        Assert.Empty(avisos);
    }

    [Fact]
    public void SaidaPadraoQueSaiDeixaNadaAtivo()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("2", "Beta"));
        t.SetDefault("1");
        Assert.Equal("1", t.ActiveId);
        var avisos = new List<DeviceChange>();
        t.Changed += avisos.Add;
        t.Remove("1");
        Assert.Null(t.ActiveId);
        Assert.False(t.IsActive("2"));
        Assert.Contains(avisos, a => a.Kind == DeviceChangeKind.DefaultChanged);
    }

    [Fact]
    public void TrocarAPadraoDeixaSoUmaAtiva()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("2", "Beta"));
        t.SetDefault("1");
        t.SetDefault("2");
        Assert.False(t.IsActive("1"));
        Assert.True(t.IsActive("2"));
        Assert.Equal("2", t.ActiveId);
    }

    [Fact]
    public void PadraoDeIdDesconhecidoFicaAtivaQuandoEle_Entra()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.SetDefault("9");
        Assert.Null(t.ActiveId);
        Assert.Equal("9", t.DefaultId);
        t.Upsert(D("9", "Zeta"));
        Assert.Equal("9", t.ActiveId);
        Assert.False(t.IsActive("1"));
    }

    [Fact]
    public void SemPadraoNadaFicaAtivo()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        Assert.Null(t.ActiveId);
        t.SetDefault("1");
        t.SetDefault(null);
        Assert.Null(t.ActiveId);
        Assert.False(t.IsActive("1"));
    }

    [Fact]
    public void IdRepetidoNaoDuplica()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("1", "Alfa"));
        Assert.Single(t.Devices);
    }

    [Fact]
    public void MudancaDeNomeReposiciona()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("2", "Beta"));
        t.Upsert(D("3", "Gama"));
        var avisos = new List<DeviceChange>();
        t.Changed += avisos.Add;
        t.Upsert(D("1", "Zulu"));
        Assert.Equal(new[] { "2", "3", "1" }, Ids(t));
        Assert.Equal(DeviceChangeKind.Changed, avisos[0].Kind);
        Assert.Equal(0, avisos[0].OldIndex);
        Assert.Equal(2, avisos[0].Index);
    }

    [Fact]
    public void SyncCompara_PeloId()
    {
        var t = new DeviceTracker();
        t.Upsert(D("1", "Alfa"));
        t.Upsert(D("2", "Beta"));
        t.SetDefault("2");
        t.Sync(new[] { D("2", "Beta"), D("3", "Gama") });
        Assert.Equal(new[] { "2", "3" }, Ids(t));
        Assert.Equal("2", t.ActiveId);
    }

    [Fact]
    public void NomeCurtoUsaDescricaoDepoisNomeCompletoDepoisDesconhecido()
    {
        Assert.Equal("Headset", DeviceInfo.ShortNameOf("Headset", "Headset (USB Audio)"));
        Assert.Equal("Headset (USB Audio)", DeviceInfo.ShortNameOf(null, "Headset (USB Audio)"));
        Assert.Equal("Headset (USB Audio)", DeviceInfo.ShortNameOf("  ", "Headset (USB Audio)"));
        Assert.Equal("Saída desconhecida", DeviceInfo.ShortNameOf(null, null));
        Assert.Equal("Saída desconhecida", DeviceInfo.ShortNameOf("", " "));
    }

    [Fact]
    public void CriarAplicaONomeCurto()
    {
        var d = DeviceInfo.Create("x", "Soundbar", "Soundbar (HDMI)", 9);
        Assert.Equal("Soundbar", d.ShortName);
        Assert.Equal("Soundbar (HDMI)", d.FullName);
        var s = DeviceInfo.Create("y", null, null, 0);
        Assert.Equal("Saída desconhecida", s.ShortName);
        Assert.Equal("Saída desconhecida", s.FullName);
    }
}
