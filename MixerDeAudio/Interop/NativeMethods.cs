using System;
using System.Runtime.InteropServices;

namespace MixerDeAudio.Interop;

/// <summary>Chamadas diretas ao Windows (P/Invoke gerado em tempo de compilação, sem custo extra na partida).</summary>
internal static partial class NativeMethods
{
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    public static partial IntPtr MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSizeEx(IntPtr process, nint minimum, nint maximum, uint flags);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] exeName, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    /// <summary>AUMID do processo (apps da Loja). Devolve 0 se achou; 15703 em processo comum.</summary>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetApplicationUserModelId(IntPtr process, ref uint length, [Out] char[] buffer);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemInKnownFolder(in Guid folderId, uint flags, string item, in Guid riid, out IntPtr result);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid riid, out IntPtr result);

    // ---- GDI (ícone do shell para PNG; Fase 8, D5) ----

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    public static partial int GetObjectW(IntPtr handle, int count, out BITMAP bitmap);

    [LibraryImport("gdi32.dll")]
    public static unsafe partial int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte* bits, ref BITMAPINFOHEADER info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, [Out] char[] outBuffer, uint outBufferSize, IntPtr reserved);

    // ---- Controle do PC pelo celular (Fase 9) ----

    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public const uint WM_SYSCOMMAND = 0x0112;
    public const nint SC_MONITORPOWER = 0xF170;
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyW(uint code, uint mapType);

    /// <summary>Manda os eventos de entrada ao Windows (qualquer thread). Devolve quantos foram aceitos.</summary>
    public static unsafe uint SendInputs(INPUT[] inputs)
    {
        if (inputs.Length == 0) return 0;
        fixed (INPUT* p = inputs) return SendInput((uint)inputs.Length, p, sizeof(INPUT));
    }

    public static INPUT MouseInput(uint flags, int dx = 0, int dy = 0, uint data = 0) =>
        new() { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };

    public static INPUT KeyInput(ushort vk, ushort scan, uint flags) =>
        new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };

    // ---- Manter a tela ligada e ver a tela (Fase 9b) ----

    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [LibraryImport("kernel32.dll")]
    public static partial uint SetThreadExecutionState(uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    public const uint SRCCOPY = 0x00CC0020;
    public const uint CAPTUREBLT = 0x40000000;
    public const int HALFTONE = 4;
    public const uint CURSOR_SHOWING = 1;
    public const uint DI_NORMAL = 3;
    public const int SM_CXCURSOR = 13;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, delegate* unmanaged<IntPtr, IntPtr, RECT*, IntPtr, int> proc, IntPtr data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorInfo(ref CURSORINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    public static partial int GdiFlush();

    [LibraryImport("gdi32.dll")]
    public static partial int SetStretchBltMode(IntPtr hdc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr old);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int sx, int sy, int swidth, int sheight, uint rop);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    // ---- Janelas (Fase 9c, D21 e D22) ----

    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const nint WS_EX_TOOLWINDOW = 0x00000080;
    public const int DWMWA_CLOAKED = 14;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;
    public const nint SC_MINIMIZE = 0xF020;
    public const ushort VK_MENU = 0x12;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> proc, IntPtr data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(IntPtr hwnd, [Out] char[] text, int max);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetWindow(IntPtr hwnd, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>Um toque na tecla Alt (libera o <c>SetForegroundWindow</c> de um app em segundo plano).</summary>
    public static void TapAlt() =>
        SendInputs(new[] { KeyInput(VK_MENU, 0, 0), KeyInput(VK_MENU, 0, KEYEVENTF_KEYUP) });

    /// <summary>Move o mouse de forma relativa (usado só para acordar as telas).</summary>
    public static unsafe void MoveMouseBy(int dx, int dy)
    {
        var input = MouseInput(MOUSEEVENTF_MOVE, dx, dy);
        SendInput(1, &input, sizeof(INPUT));
    }

    /// <summary>Escala do monitor (DPI efetivo / 96) no ponto dado; 1,0 se o Windows não responder.</summary>
    public static double GetScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }
}
