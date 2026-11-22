using MixerDeAudio.Pc;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, D9: modo de projeção e flag do SetDisplayConfig; contagem de telas a partir de caminhos falsos.</summary>
public class DisplayTopologyTests
{
    [Theory]
    [InlineData("principal", ProjectionMode.Primary, 0x1u)]
    [InlineData("duplicar", ProjectionMode.Duplicate, 0x2u)]
    [InlineData("estender", ProjectionMode.Extend, 0x4u)]
    [InlineData("segunda", ProjectionMode.Second, 0x8u)]
    public void ModoEFlag_IdaEVolta(string id, ProjectionMode modo, uint flag)
    {
        Assert.True(DisplayTopology.TryParseMode(id, out var lido));
        Assert.Equal(modo, lido);
        Assert.Equal(id, DisplayTopology.Id(modo));
        Assert.Equal(flag, DisplayTopology.ToFlag(modo));
        Assert.Equal(modo, DisplayTopology.FromFlag(flag));
    }

    [Fact]
    public void ModoDesconhecido()
    {
        Assert.False(DisplayTopology.TryParseMode("girar", out _));
        Assert.False(DisplayTopology.TryParseMode(null, out _));
        Assert.Null(DisplayTopology.FromFlag(0));
        Assert.Null(DisplayTopology.FromFlag(3));
    }

    [Fact]
    public void ContagemDeTelas_SoAlvosDisponiveisEDistintos()
    {
        var alvos = new[]
        {
            new DisplayTarget(1, 10, true),
            new DisplayTarget(1, 10, true),  // mesmo alvo por outro caminho
            new DisplayTarget(1, 11, true),
            new DisplayTarget(1, 12, false), // porta sem monitor
            new DisplayTarget(2, 10, true),  // outro adaptador
        };
        Assert.Equal(3, DisplayTopology.CountScreens(alvos));
    }

    [Fact]
    public void ContagemDeTelas_Vazia() => Assert.Equal(0, DisplayTopology.CountScreens(Array.Empty<DisplayTarget>()));

    [Fact]
    public void ContagemDeTelas_SemNenhumDisponivel() =>
        Assert.Equal(0, DisplayTopology.CountScreens(new[] { new DisplayTarget(1, 1, false) }));

    private static void Caminho(byte[] buf, int i, long luid, uint id, bool disponivel)
    {
        var at = i * DisplayTopology.PathInfoSize;
        BitConverter.GetBytes(luid).CopyTo(buf, at + 20);
        BitConverter.GetBytes(id).CopyTo(buf, at + 28);
        BitConverter.GetBytes(disponivel ? 1 : 0).CopyTo(buf, at + 60);
    }

    [Fact]
    public void LeituraDosCaminhos_PelosDeslocamentos()
    {
        var buf = new byte[DisplayTopology.PathInfoSize * 3];
        Caminho(buf, 0, 0x0000_0001_0000_00AB, 7, true);
        Caminho(buf, 1, 0x0000_0001_0000_00AB, 8, false);
        Caminho(buf, 2, 0x0000_0001_0000_00AB, 7, true);
        var alvos = DisplayTopology.ParsePaths(buf, 3);
        Assert.Equal(3, alvos.Count);
        Assert.Equal(new DisplayTarget(0x0000_0001_0000_00AB, 7, true), alvos[0]);
        Assert.False(alvos[1].Available);
        Assert.Equal(1, DisplayTopology.CountScreens(alvos));
    }

    [Fact]
    public void LeituraDosCaminhos_BufferCurto_NaoEstoura()
    {
        var buf = new byte[DisplayTopology.PathInfoSize];
        Caminho(buf, 0, 5, 1, true);
        Assert.Single(DisplayTopology.ParsePaths(buf, 4));
        Assert.Empty(DisplayTopology.ParsePaths(Array.Empty<byte>(), 2));
    }
}
