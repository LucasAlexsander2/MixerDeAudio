using MixerDeAudio.Audio;

namespace MixerDeAudio.Tests;

public class FavoriteFilterTests
{
    private static DeviceInfo D(string id, string nome) => new(id, nome, nome, 0);

    private static readonly DeviceInfo Fone = D("1", "Headset");
    private static readonly DeviceInfo Monitor = D("2", "Monitor");
    private static readonly DeviceInfo Barra = D("3", "Soundbar");
    private static readonly DeviceInfo[] Todas = [Fone, Monitor, Barra];

    private static string[] Ids(IEnumerable<DeviceInfo> l) => l.Select(d => d.Id).ToArray();

    [Fact]
    public void SemFavoritos_MostraTodas()
    {
        Assert.Equal(new[] { "1", "2", "3" }, Ids(FavoriteFilter.Visible(Todas, [], "2")));
        Assert.Equal(new[] { "1", "2", "3" }, Ids(FavoriteFilter.Visible(Todas, null, null)));
    }

    [Fact]
    public void FavoritosAtivos_MostraSoElas()
    {
        Assert.Equal(new[] { "1", "3" }, Ids(FavoriteFilter.Visible(Todas, ["1", "3"], "1")));
    }

    [Fact]
    public void FavoritaDesligada_NaoAparece()
    {
        Assert.Equal(new[] { "1" }, Ids(FavoriteFilter.Visible(Todas, ["1", "99"], "1")));
    }

    [Fact]
    public void AtivaForaDosFavoritos_VaiParaOFim()
    {
        // A padrão é o Monitor; os favoritos são Headset e Soundbar
        Assert.Equal(new[] { "1", "3", "2" }, Ids(FavoriteFilter.Visible(Todas, ["1", "3"], "2")));
    }

    [Fact]
    public void AtivaNula_NaoAcrescentaNada()
    {
        Assert.Equal(new[] { "1", "3" }, Ids(FavoriteFilter.Visible(Todas, ["1", "3"], null)));
    }

    [Fact]
    public void AtivaQueNaoEstaNaLista_NaoAcrescentaNada()
    {
        Assert.Equal(new[] { "1" }, Ids(FavoriteFilter.Visible(Todas, ["1"], "99")));
    }

    [Fact]
    public void OrdemAlfabetica_IndependenteDaOrdemDosFavoritos()
    {
        DeviceInfo[] desordenadas = [Barra, Fone, Monitor];
        Assert.Equal(new[] { "1", "3" }, Ids(FavoriteFilter.Visible(desordenadas, ["3", "1"], "1")));
    }

    [Fact]
    public void IdsComMaiusculasDiferentes_Casam()
    {
        var a = D("ABC", "Alto-falante");
        Assert.Equal(new[] { "ABC" }, Ids(FavoriteFilter.Visible([a], ["abc"], "abc")));
    }

    [Fact]
    public void UmaFavoritaMaisAAtiva_SaoDuasSaidas()
    {
        Assert.Equal(2, FavoriteFilter.Visible(Todas, ["1"], "2").Count);
    }
}
