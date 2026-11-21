using System;
using MixerDeAudio.Services;

namespace MixerDeAudio.Tests;

public class VersionTextTests
{
    [Fact]
    public void VersaoDoAssembly_MostraTresPartes() => Assert.Equal("Versão 1.0.0", VersionText.Format(new Version(1, 0, 0, 0)));

    [Fact]
    public void SemBuild_VaiComZero() => Assert.Equal("Versão 2.3.0", VersionText.Format(new Version(2, 3)));

    [Fact]
    public void Nula_Desconhecida() => Assert.Equal("Versão desconhecida", VersionText.Format(null));
}
