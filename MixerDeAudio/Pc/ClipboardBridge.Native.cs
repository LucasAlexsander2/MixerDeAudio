using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MixerDeAudio.Pc;

public static partial class ClipboardBridge
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int OpenTries = 5;
    private const int OpenWaitMs = 20;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalFree(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    private static bool Open()
    {
        for (var i = 0; i < OpenTries; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(OpenWaitMs);
        }
        return false;
    }

    /// <summary>O texto da área de transferência real (vazio se não há texto); nulo se não foi possível abri-la. Thread da tela.</summary>
    public static string? ReadText()
    {
        if (!Open()) return null;
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return "";
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return "";
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(ptr) ?? ""; }
            finally { GlobalUnlock(handle); }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Grava o texto na área de transferência real. Devolve falso se não foi possível. Thread da tela.</summary>
    public static bool WriteText(string text)
    {
        var bytes = (nuint)((text.Length + 1) * 2);
        var mem = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (mem == IntPtr.Zero) return false;
        var done = false;
        try
        {
            var ptr = GlobalLock(mem);
            if (ptr == IntPtr.Zero) return false;
            try
            {
                var chars = text.ToCharArray();
                Marshal.Copy(chars, 0, ptr, chars.Length);
                Marshal.WriteInt16(ptr, chars.Length * 2, 0);
            }
            finally { GlobalUnlock(mem); }
            if (!Open()) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                done = SetClipboardData(CF_UNICODETEXT, mem) != IntPtr.Zero; // com sucesso a memória passa a ser do sistema
                return done;
            }
            finally { CloseClipboard(); }
        }
        finally
        {
            if (!done) GlobalFree(mem);
        }
    }
}
