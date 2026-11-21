using System.Runtime.InteropServices;
using MixerDeAudio.Audio;
using MixerDeAudio.Audio.CoreAudio;

namespace MixerDeAudio.Tests;

public class CoreAudioValuesTests
{
    [Fact]
    public void PropVariant_TemTamanhoDoWindows_NoX64()
        => Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<PropVariant>());

    [Fact]
    public void PropertyKey_TemVinteBytes()
        => Assert.Equal(20, Marshal.SizeOf<PropertyKey>());

    [Fact]
    public void PropVariant_Texto_LeLpwstr()
    {
        var ptr = Marshal.StringToCoTaskMemUni("Alto-falantes (Realtek)");
        try
        {
            var v = new PropVariant { Vt = PropVariant.VtLpwstr, Data = ptr };
            Assert.Equal("Alto-falantes (Realtek)", v.AsString());
            Assert.Null(v.AsInt32());
        }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    [Fact]
    public void PropVariant_Vazio_NaoTemTextoNemNumero()
    {
        var v = new PropVariant { Vt = PropVariant.VtEmpty };
        Assert.Null(v.AsString());
        Assert.Null(v.AsInt32());
    }

    [Fact]
    public void PropVariant_TextoComPonteiroNulo_DaNulo()
        => Assert.Null(new PropVariant { Vt = PropVariant.VtLpwstr, Data = IntPtr.Zero }.AsString());

    [Fact]
    public void PropVariant_Inteiro_LeUi4()
    {
        var v = new PropVariant { Vt = PropVariant.VtUi4, Data = (IntPtr)5 };
        Assert.Equal(5, v.AsInt32());
        Assert.Null(v.AsString());
    }

    [Fact]
    public void PropVariant_Inteiro_IgnoraLixoNosBytesAltos()
    {
        var v = new PropVariant { Vt = PropVariant.VtUi4, Data = unchecked((IntPtr)0x7FFF_0000_0000_0003L) };
        Assert.Equal(3, v.AsInt32());
    }

    [Theory]
    [InlineData(0, SessionState.Inactive)]
    [InlineData(1, SessionState.Active)]
    [InlineData(2, SessionState.Expired)]
    [InlineData(7, SessionState.Inactive)]
    public void MapState_ConvertePeloNumeroDoWindows(int estado, SessionState esperado)
        => Assert.Equal(esperado, CoreAudioValues.MapState(estado));

    [Fact]
    public void ReadVolumeNotification_LeMudoEVolumeNasPosicoesDoWindows()
    {
        var ptr = Marshal.AllocHGlobal(32);
        try
        {
            for (var i = 0; i < 32; i++) Marshal.WriteByte(ptr, i, 0);
            Marshal.WriteInt32(ptr, 16, 1);
            Marshal.WriteInt32(ptr, 20, BitConverter.SingleToInt32Bits(0.42f));
            var (volume, muted) = CoreAudioValues.ReadVolumeNotification(ptr);
            Assert.Equal(0.42f, volume);
            Assert.True(muted);

            Marshal.WriteInt32(ptr, 16, 0);
            Assert.False(CoreAudioValues.ReadVolumeNotification(ptr).Muted);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
