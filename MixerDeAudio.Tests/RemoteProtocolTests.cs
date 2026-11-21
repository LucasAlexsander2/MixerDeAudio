using System.Text.Json;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

public class RemoteProtocolTests
{
    private static RemoteState Fixo() => new(
        new[]
        {
            new RemoteOutput("{0.0.0}.{aaa}", "Alto-falantes", "\uE7F5", true),
            new RemoteOutput("{0.0.0}.{bbb}", "Fone \"BT\"", "\uE7F6", false),
        },
        true, 42, false,
        new[]
        {
            new RemoteApp("0123456789abcdef", @"c:\apps\musica.exe", "Música", 60, true, false,
                new[] { new RemoteRoute("{0.0.0}.{aaa}", "\uE7F5", "Alto-falantes", true), new RemoteRoute("{0.0.0}.{bbb}", "\uE7F6", "Fone \"BT\"", false) }),
            new RemoteApp("fedcba9876543210", "::sons-do-sistema", "Sons do sistema", 100, false, true, Array.Empty<RemoteRoute>()),
        });

    [Fact]
    public void Estado_MontaTudo()
    {
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(Fixo()));
        var r = doc.RootElement;
        Assert.Equal("estado", r.GetProperty("t").GetString());

        var saidas = r.GetProperty("saidas");
        Assert.Equal(2, saidas.GetArrayLength());
        Assert.Equal("{0.0.0}.{aaa}", saidas[0].GetProperty("id").GetString());
        Assert.Equal("Alto-falantes", saidas[0].GetProperty("nome").GetString());
        Assert.Equal("\uE7F5", saidas[0].GetProperty("glifo").GetString());
        Assert.True(saidas[0].GetProperty("ativa").GetBoolean());
        Assert.Equal("Fone \"BT\"", saidas[1].GetProperty("nome").GetString());
        Assert.False(saidas[1].GetProperty("ativa").GetBoolean());

        var geral = r.GetProperty("geral");
        Assert.True(geral.GetProperty("disponivel").GetBoolean());
        Assert.Equal(42, geral.GetProperty("volume").GetInt32());
        Assert.False(geral.GetProperty("mudo").GetBoolean());

        var apps = r.GetProperty("apps");
        Assert.Equal(2, apps.GetArrayLength());
        Assert.Equal("0123456789abcdef", apps[0].GetProperty("icone").GetString());
        Assert.Equal(@"c:\apps\musica.exe", apps[0].GetProperty("chave").GetString());
        Assert.Equal("Música", apps[0].GetProperty("nome").GetString());
        Assert.Equal(60, apps[0].GetProperty("volume").GetInt32());
        Assert.True(apps[0].GetProperty("mudo").GetBoolean());
        Assert.False(apps[0].GetProperty("sistema").GetBoolean());
        Assert.Equal(2, apps[0].GetProperty("rotas").GetArrayLength());
        Assert.True(apps[0].GetProperty("rotas")[0].GetProperty("ativa").GetBoolean());
        Assert.True(apps[1].GetProperty("sistema").GetBoolean());
        Assert.Equal(0, apps[1].GetProperty("rotas").GetArrayLength());
    }

    [Fact]
    public void Estado_Vazio()
    {
        var vazio = new RemoteState(Array.Empty<RemoteOutput>(), false, 0, false, Array.Empty<RemoteApp>());
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(vazio));
        Assert.Equal(0, doc.RootElement.GetProperty("saidas").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("geral").GetProperty("disponivel").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("apps").GetArrayLength());
    }

    [Fact]
    public void Estado_CortaVolumeForaDaFaixa()
    {
        var s = new RemoteState(Array.Empty<RemoteOutput>(), true, 250, false,
            new[] { new RemoteApp("0123456789abcdef", "k", "n", -5, false, false, Array.Empty<RemoteRoute>()) });
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildState(s));
        Assert.Equal(100, doc.RootElement.GetProperty("geral").GetProperty("volume").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("apps")[0].GetProperty("volume").GetInt32());
    }

    [Fact]
    public void IdDoIcone_Tem16HexadecimaisEnaoExpoeOCaminho()
    {
        var id = RemoteProtocol.IconId(@"c:\apps\musica.exe");
        Assert.True(RemoteProtocol.IsIconId(id));
        Assert.DoesNotContain("musica", id);
        Assert.Equal(id, RemoteProtocol.IconId(@"c:\apps\musica.exe"));
        Assert.NotEqual(id, RemoteProtocol.IconId(@"c:\apps\outro.exe"));
    }

    [Theory]
    [InlineData("0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF", false)]
    [InlineData("0123456789abcde", false)]
    [InlineData("0123456789abcdeg", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData(null, false)]
    public void IsIconId(string? id, bool esperado) => Assert.Equal(esperado, RemoteProtocol.IsIconId(id));

    [Fact]
    public void Volume_EhLido()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"volume\",\"app\":\"c:\\\\a.exe\",\"v\":30}", out var c));
        Assert.Equal(new VolumeCommand(@"c:\a.exe", 30), c);
    }

    [Fact]
    public void Mudo_EhLido()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"mudo\",\"app\":\"k\",\"m\":true}", out var c));
        Assert.Equal(new MuteCommand("k", true), c);
    }

    [Fact]
    public void Geral_EhLido()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"geral\",\"v\":55}", out var c));
        Assert.Equal(new MasterVolumeCommand(55), c);
    }

    [Fact]
    public void GeralMudo_EhLido()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"geralMudo\",\"m\":false}", out var c));
        Assert.Equal(new MasterMuteCommand(false), c);
    }

    [Fact]
    public void Padrao_EhLido()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"padrao\",\"id\":\"{0.0.0}.{aaa}\"}", out var c));
        Assert.Equal(new DefaultDeviceCommand("{0.0.0}.{aaa}"), c);
    }

    [Fact]
    public void Rota_EhLido()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"rota\",\"app\":\"k\",\"id\":\"d1\"}", out var c));
        Assert.Equal(new RouteCommand("k", "d1"), c);
    }

    [Theory]
    [InlineData("{\"t\":\"volume\",\"app\":\"k\",\"v\":150}", 100)]
    [InlineData("{\"t\":\"volume\",\"app\":\"k\",\"v\":-20}", 0)]
    [InlineData("{\"t\":\"volume\",\"app\":\"k\",\"v\":33.6}", 34)]
    [InlineData("{\"t\":\"volume\",\"app\":\"k\",\"v\":1e9}", 100)]
    public void ValorForaDaFaixa_EhCortado(string json, int esperado)
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Equal(esperado, ((VolumeCommand)c!).Volume);
    }

    [Fact]
    public void GeralForaDaFaixa_EhCortado()
    {
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand("{\"t\":\"geral\",\"v\":999}", out var c));
        Assert.Equal(100, ((MasterVolumeCommand)c!).Volume);
    }

    [Theory]
    [InlineData("{\"t\":\"explodir\"}")]
    [InlineData("{\"t\":5}")]
    [InlineData("{\"app\":\"k\"}")]
    public void TipoDesconhecido(string json)
    {
        Assert.Equal(ParseStatus.UnknownType, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Null(c);
    }

    [Theory]
    [InlineData("{\"t\":\"volume\",\"app\":\"k\"}")]
    [InlineData("{\"t\":\"volume\",\"v\":10}")]
    [InlineData("{\"t\":\"volume\",\"app\":\"k\",\"v\":\"10\"}")]
    [InlineData("{\"t\":\"volume\",\"app\":\"\",\"v\":10}")]
    [InlineData("{\"t\":\"mudo\",\"app\":\"k\",\"m\":\"sim\"}")]
    [InlineData("{\"t\":\"rota\",\"app\":\"k\"}")]
    [InlineData("{\"t\":\"padrao\"}")]
    [InlineData("{\"t\":\"geralMudo\"}")]
    public void CamposFaltandoOuComTipoErrado(string json)
    {
        Assert.Equal(ParseStatus.InvalidFields, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Null(c);
    }

    [Theory]
    [InlineData("isto nao e json")]
    [InlineData("{\"t\":")]
    [InlineData("[1,2,3]")]
    [InlineData("\"texto\"")]
    [InlineData("")]
    [InlineData(null)]
    public void JsonInvalido(string? json)
    {
        Assert.Equal(ParseStatus.InvalidJson, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Null(c);
    }

    [Fact]
    public void MensagemGrande_FechaAConexao()
    {
        var json = "{\"t\":\"volume\",\"app\":\"" + new string('a', RemoteProtocol.MaxMessageBytes) + "\",\"v\":1}";
        Assert.Equal(ParseStatus.TooLarge, RemoteProtocol.ParseCommand(json, out var c));
        Assert.Null(c);
    }

    [Fact]
    public void MensagemNoLimite_AindaEhAceita()
    {
        var prefixo = "{\"t\":\"volume\",\"app\":\"";
        var sufixo = "\",\"v\":1}";
        var json = prefixo + new string('a', RemoteProtocol.MaxMessageBytes - prefixo.Length - sufixo.Length) + sufixo;
        Assert.Equal(RemoteProtocol.MaxMessageBytes, System.Text.Encoding.UTF8.GetByteCount(json));
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out _));
    }
}
