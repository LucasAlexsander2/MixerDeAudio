using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MixerDeAudio.Services;

/// <summary>
/// Protege o código do celular no <c>config.json</c> (Fase 16, D14) com a DPAPI do Windows no escopo do usuário
/// (<c>CryptProtectData</c> por P/Invoke, sem pacote). O texto protegido só abre na conta do mesmo usuário, no mesmo PC.
/// Falha de qualquer tipo devolve <c>null</c> (quem chama decide o que fazer).
/// </summary>
public static partial class CodeProtector
{
    // Dado extra misturado à chave (não é segredo): impede que outro uso da DPAPI do mesmo usuário abra este valor por engano
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MixerDeAudio.CodigoCelular");

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const int UiForbidden = 0x1;

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob dataIn, [MarshalAs(UnmanagedType.LPWStr)] string? description,
        ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr handle);

    /// <summary>O texto protegido em base64, ou <c>null</c> se a DPAPI falhar.</summary>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Run(Encoding.UTF8.GetBytes(plain), protect: true);
        return bytes is null ? null : Convert.ToBase64String(bytes);
    }

    /// <summary>O texto original, ou <c>null</c> se o valor não for base64, vier de outro usuário ou estiver corrompido.</summary>
    public static string? Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64)) return null;
        byte[] raw;
        try { raw = Convert.FromBase64String(protectedBase64); }
        catch (FormatException) { return null; }
        var bytes = Run(raw, protect: false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static byte[]? Run(byte[] input, bool protect)
    {
        var pInput = Marshal.AllocHGlobal(Math.Max(1, input.Length));
        var pEntropy = Marshal.AllocHGlobal(Entropy.Length);
        try
        {
            Marshal.Copy(input, 0, pInput, input.Length);
            Marshal.Copy(Entropy, 0, pEntropy, Entropy.Length);
            var dataIn = new DataBlob { cbData = input.Length, pbData = pInput };
            var entropy = new DataBlob { cbData = Entropy.Length, pbData = pEntropy };
            DataBlob dataOut;
            var ok = protect
                ? CryptProtectData(ref dataIn, null, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out dataOut)
                : CryptUnprotectData(ref dataIn, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out dataOut);
            if (!ok) return null;
            try
            {
                var result = new byte[dataOut.cbData];
                Marshal.Copy(dataOut.pbData, result, 0, dataOut.cbData);
                return result;
            }
            finally
            {
                LocalFree(dataOut.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pInput);
            Marshal.FreeHGlobal(pEntropy);
        }
    }
}
