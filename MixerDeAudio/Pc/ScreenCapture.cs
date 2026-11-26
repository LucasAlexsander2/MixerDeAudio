using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using MixerDeAudio.Interop;
using MixerDeAudio.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MixerDeAudio.Pc;

/// <summary>
/// Ver a tela (Fase 9b, D17 e D18; Fase 10, D4 e D5): captura por GDI (<c>StretchBlt</c> com <c>HALFTONE</c> direto para um DIB do
/// tamanho final, cursor desenhado por cima) e JPEG pelo <c>BitmapEncoder</c>. Um objeto por canal aberto: o DIB, o contexto GDI
/// e a cópia do quadro anterior ficam do tamanho atual enquanto o canal está aberto e são refeitos se mudar a tela ou a
/// largura; ao fechar (<see cref="Dispose"/>) tudo é solto, compacta e devolve a memória. Quadro igual ao anterior não é
/// codificado. A leitura da tela é só leitura, por isso também roda com <c>--teste-pc</c>. Uma captura por vez (o laço do
/// canal espera cada uma).
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    private IntPtr _memDc, _dib, _old, _bits;
    private int _mon = -1, _w, _h;
    private byte[]? _prev;
    private IBuffer? _prevBuffer;
    private bool _hasPrev;
    private bool _disposed;
    private readonly Stopwatch _clock = new();

    /// <summary>Posição do ponteiro (0 a 1 no quadro) lida na última captura; nula se o Windows não informou (Fase 15c, D16).</summary>
    public (double X, double Y)? LastPointer { get; private set; }

    /// <summary>Quadros enviados (codificados).</summary>
    public int Sent { get; private set; }
    /// <summary>Quadros iguais ao anterior, pulados.</summary>
    public int Skipped { get; private set; }
    /// <summary>Soma do tempo de captura e codificação dos quadros enviados, em ms.</summary>
    public double TotalMs { get; private set; }

    // ---- Telas ----

    /// <summary>As telas ligadas ao PC: a principal primeiro (índice 0), as outras da esquerda para a direita.</summary>
    public static unsafe List<ScreenInfo> Monitors()
    {
        var raw = new List<ScreenInfo>();
        var handle = GCHandle.Alloc(raw);
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &EnumProc, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return ScreenRules.Order(raw);
    }

    [UnmanagedCallersOnly]
    private static unsafe int EnumProc(IntPtr monitor, IntPtr hdc, NativeMethods.RECT* rect, IntPtr data)
    {
        try
        {
            var list = (List<ScreenInfo>)GCHandle.FromIntPtr(data).Target!;
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)sizeof(NativeMethods.MONITORINFO) };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var r = info.rcMonitor;
                list.Add(new ScreenInfo(0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (info.dwFlags & 1) != 0));
            }
        }
        catch
        {
            // um callback nativo não pode lançar: essa tela fica de fora
        }
        return 1;
    }

    // ---- Quadro ----

    /// <summary>
    /// Captura a tela <paramref name="monitor"/> na largura pedida (cortada por <see cref="ScreenRules.FrameSize"/> e pela
    /// qualidade). <c>Jpeg</c> nulo com <c>Equal</c> verdadeiro: o quadro é igual ao anterior (nada codificado); <c>Failed</c>:
    /// a tela não existe ou a captura falhou (tela bloqueada, por exemplo). <c>Width</c> e <c>Height</c> são as dimensões do quadro.
    /// </summary>
    public async Task<(byte[]? Jpeg, bool Equal, bool Failed, int Width, int Height)> CaptureAsync(int monitor, int width, ScreenQuality quality, double cursorScale = 1.0)
    {
        if (_disposed) return (null, false, true, 0, 0);
        _clock.Restart();
        var screens = Monitors();
        if (monitor < 0 || monitor >= screens.Count) return (null, false, true, 0, 0);
        var s = screens[monitor];
        var (w, h) = ScreenRules.FrameSize(s.Width, s.Height, width, quality.MaxWidth);
        if (w <= 0 || h <= 0) return (null, false, true, 0, 0);
        var rebuilt = ScreenStreamRules.NeedsRebuild(_mon, _w, _h, monitor, w, h);
        if (rebuilt && !Rebuild(monitor, w, h)) return (null, false, true, 0, 0);
        if (!Grab(s, w, h, cursorScale)) return (null, false, true, w, h);

        var len = w * h * 4;
        var identical = !rebuilt && _hasPrev && SameAsPrevious(len);
        if (ScreenStreamRules.Decide(rebuilt, identical) == FrameAction.Skip)
        {
            Skipped++;
            return (null, true, false, w, h);
        }
        CopyToPrevious(len);
        var jpeg = await EncodeAsync(_prevBuffer!, w, h, quality.Jpeg).ConfigureAwait(false);
        Sent++;
        TotalMs += _clock.Elapsed.TotalMilliseconds;
        return (jpeg, false, false, w, h);
    }

    private unsafe bool SameAsPrevious(int len) =>
        _prev is not null && ScreenStreamRules.SameFrame(_prev, new ReadOnlySpan<byte>((void*)_bits, len));

    private unsafe void CopyToPrevious(int len)
    {
        new ReadOnlySpan<byte>((void*)_bits, len).CopyTo(_prev);
        _hasPrev = true;
    }

    /// <summary>Refaz o DIB e a cópia anterior para o tamanho novo (D5).</summary>
    private unsafe bool Rebuild(int monitor, int w, int h)
    {
        FreeGdi();
        FreeBuffers();
        _hasPrev = false;
        _mon = -1; _w = 0; _h = 0;
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        try
        {
            _memDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (_memDc == IntPtr.Zero) return false;
            var bmi = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(NativeMethods.BITMAPINFOHEADER),
                biWidth = w,
                biHeight = -h, // de cima para baixo
                biPlanes = 1,
                biBitCount = 32,
            };
            _dib = NativeMethods.CreateDIBSection(screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero || _bits == IntPtr.Zero) { FreeGdi(); return false; }
            _old = NativeMethods.SelectObject(_memDc, _dib);
            NativeMethods.SetStretchBltMode(_memDc, NativeMethods.HALFTONE);
            NativeMethods.SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
        _prev = new byte[w * h * 4];
        _prevBuffer = _prev.AsBuffer();
        _mon = monitor; _w = w; _h = h;
        return true;
    }

    private void FreeBuffers()
    {
        _prevBuffer = null;
        _prev = null;
    }

    private void FreeGdi()
    {
        if (_old != IntPtr.Zero && _memDc != IntPtr.Zero) NativeMethods.SelectObject(_memDc, _old);
        if (_dib != IntPtr.Zero) NativeMethods.DeleteObject(_dib);
        if (_memDc != IntPtr.Zero) NativeMethods.DeleteDC(_memDc);
        _old = _dib = _memDc = _bits = IntPtr.Zero;
    }

    private bool Grab(ScreenInfo s, int w, int h, double cursorScale)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        try
        {
            if (!NativeMethods.StretchBlt(_memDc, 0, 0, w, h, screenDc, s.Left, s.Top, s.Width, s.Height,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                return false;
            LastPointer = DrawCursor(_memDc, s, w, cursorScale);
            NativeMethods.GdiFlush();
            return true;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Desenha o cursor (se estiver visível) na posição certa do quadro reduzido, <paramref name="cursorScale"/> vezes maior que o do
    /// sistema (só na imagem; o ponteiro do Windows não muda), e devolve a posição dele no quadro (0 a 1; nulo se o Windows não disse).
    /// Com <paramref name="cursorScale"/> 1 o desenho é o de sempre.
    /// </summary>
    private static unsafe (double X, double Y)? DrawCursor(IntPtr memDc, ScreenInfo s, int frameWidth, double cursorScale)
    {
        var ci = new NativeMethods.CURSORINFO { cbSize = (uint)sizeof(NativeMethods.CURSORINFO) };
        if (!NativeMethods.GetCursorInfo(ref ci)) return null;
        var pointer = ScreenStreamRules.PointerFraction(ci.ptScreenPos.X, ci.ptScreenPos.Y, s.Left, s.Top, s.Width, s.Height);
        if ((ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return pointer;
        if (!NativeMethods.GetIconInfo(ci.hCursor, out var ii)) return pointer;
        try
        {
            var scale = (double)frameWidth / s.Width;
            var size = Math.Max(12, (int)Math.Round(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR) * scale * cursorScale));
            var x = (int)Math.Round((ci.ptScreenPos.X - s.Left - (int)ii.xHotspot * cursorScale) * scale);
            var y = (int)Math.Round((ci.ptScreenPos.Y - s.Top - (int)ii.yHotspot * cursorScale) * scale);
            NativeMethods.DrawIconEx(memDc, x, y, ci.hCursor, size, size, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
        }
        finally
        {
            if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
        }
        return pointer;
    }

    /// <summary>
    /// JPEG do quadro. O pixel vai por um <c>SoftwareBitmap</c> solto (<c>Dispose</c>) a cada quadro: com <c>SetPixelData</c> o
    /// codificador do Windows guardava uma cópia nativa de 8 MB por quadro até o coletor rodar (centenas de MB em Alta, medido).
    /// </summary>
    private static async Task<byte[]> EncodeAsync(IBuffer pixels, int w, int h, float jpegQuality)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels, BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
        using var stream = new InMemoryRandomAccessStream();
        var props = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue(jpegQuality, PropertyType.Single) } };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, props);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        reader.DetachStream();
        return bytes;
    }

    /// <summary>Fecha o canal: solta o DIB e a cópia anterior, compacta o monte de objetos grandes e devolve a memória (D5).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FreeGdi();
        FreeBuffers();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        MemoryTrimmer.Trim();
    }
}
