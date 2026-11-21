using MixerDeAudio.ViewModels;

namespace MixerDeAudio.Tests;

public class VolumeStepTests
{
    [Fact]
    public void UmDenteParaCima_SobeDois()
        => Assert.Equal(52, VolumeStep.Apply(50, 120));

    [Fact]
    public void UmDenteParaBaixo_DesceDois()
        => Assert.Equal(48, VolumeStep.Apply(50, -120));

    [Fact]
    public void VariosDentes_Somam()
    {
        Assert.Equal(56, VolumeStep.Apply(50, 360));
        Assert.Equal(44, VolumeStep.Apply(50, -360));
    }

    [Fact]
    public void LimiteSuperior_Em100()
    {
        Assert.Equal(100, VolumeStep.Apply(99, 120));
        Assert.Equal(100, VolumeStep.Apply(100, 120));
    }

    [Fact]
    public void LimiteInferior_Em0()
    {
        Assert.Equal(0, VolumeStep.Apply(1, -120));
        Assert.Equal(0, VolumeStep.Apply(0, -120));
    }

    [Fact]
    public void DeltaParcial_ValeProporcional()
    {
        Assert.Equal(51, VolumeStep.Apply(50, 60));
        Assert.Equal(49, VolumeStep.Apply(50, -60));
    }

    [Fact]
    public void DeltaPequenoDemais_ArredondaSimetrico()
    {
        Assert.Equal(51, VolumeStep.Apply(50, 30));
        Assert.Equal(49, VolumeStep.Apply(50, -30));
        Assert.Equal(50, VolumeStep.Apply(50, 10));
    }

    [Fact]
    public void DeltaZero_NaoMuda()
        => Assert.Equal(40, VolumeStep.Apply(40, 0));
}
