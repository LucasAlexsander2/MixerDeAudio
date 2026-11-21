using System;
using System.Runtime.InteropServices;

namespace MixerDeAudio.Audio.CoreAudio;

/// <summary>
/// <c>PROPVARIANT</c> próprio (24 bytes no x64) e conversões puras dos valores do Core Audio (Fase 8, D8). Sem COM aqui,
/// para os testes unitários poderem usar este arquivo.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public const ushort VtEmpty = 0;
    public const ushort VtI4 = 3;
    public const ushort VtUi4 = 19;
    public const ushort VtLpwstr = 31;

    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Data;
    public IntPtr Data2;

    /// <summary>O texto, se o tipo for <c>VT_LPWSTR</c>; senão nulo.</summary>
    public readonly string? AsString() => Vt == VtLpwstr && Data != IntPtr.Zero ? Marshal.PtrToStringUni(Data) : null;

    /// <summary>O inteiro, se o tipo for <c>VT_UI4</c> ou <c>VT_I4</c>; senão nulo.</summary>
    public readonly int? AsInt32() => Vt is VtUi4 or VtI4 ? unchecked((int)(Data.ToInt64() & 0xFFFFFFFFL)) : null;
}

/// <summary><c>PROPERTYKEY</c>: o GUID do grupo e o número da propriedade.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    private static readonly Guid DeviceGroup = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    public static readonly PropertyKey DeviceDesc = new(DeviceGroup, 2);
    public static readonly PropertyKey FriendlyName = new(DeviceGroup, 14);
    public static readonly PropertyKey FormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
}

internal static class CoreAudioValues
{
    /// <summary><c>AudioSessionState</c> do Windows (0 inativa, 1 ativa, 2 expirada) para o estado do app.</summary>
    public static SessionState MapState(int state) => state switch
    {
        1 => SessionState.Active,
        2 => SessionState.Expired,
        _ => SessionState.Inactive,
    };

    /// <summary>
    /// Lê o volume (0 a 1) e o mudo de um <c>AUDIO_VOLUME_NOTIFICATION_DATA</c>: o GUID de contexto (16 bytes), o
    /// <c>BOOL</c> do mudo no byte 16 e o <c>float</c> do volume no byte 20.
    /// </summary>
    public static (float Volume, bool Muted) ReadVolumeNotification(IntPtr data)
    {
        var muted = Marshal.ReadInt32(data, 16) != 0;
        var volume = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
        return (volume, muted);
    }
}
