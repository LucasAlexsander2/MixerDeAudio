using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MixerDeAudio.Interop;
using MixerDeAudio.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MixerDeAudio.Pc;

/// <summary>
/// Ver a tela (Fase 9b, D17 e D18): captura por GDI (<c>StretchBlt</c> com <c>HALFTONE</c> direto para um DIB do tamanho final,
/// cursor desenhado por cima) e JPEG pelo <c>BitmapEncoder</c>. Uma captura por vez e pelo menos 250 ms entre quadros. Buffers e
/// objetos GDI são criados e soltos a cada quadro: nada fica na memória quando ninguém olha. A leitura da tela é só leitura,
/// por isso também roda com <c>--teste-pc</c>. O objeto só existe depois do primeiro pedido; o único timer é o de "10 s sem
/// pedido" (só para o log), criado no primeiro quadro e solto ao encerrar.
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    private const float JpegQuality = 0.6f;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _shareLock = new();
    private long _nextStart;
    private Timer? _idle;
    private bool _sharing;

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
    /// Um quadro JPEG da tela <paramref name="monitor"/> com a largura pedida (cortada por <see cref="ScreenRules.FrameSize"/>).
    /// Espera a vez e o intervalo mínimo. Nulo se a tela não existe ou a captura falhou (tela bloqueada, por exemplo).
    /// </summary>
    public async Task<byte[]?> CaptureJpegAsync(int monitor, int width, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _nextStart - Stopwatch.GetTimestamp();
            if (wait > 0) await Task.Delay(TimeSpan.FromSeconds((double)wait / Stopwatch.Frequency), ct).ConfigureAwait(false);
            _nextStart = Stopwatch.GetTimestamp() + (long)(ScreenRules.MinFrameInterval.TotalSeconds * Stopwatch.Frequency);

            var screens = Monitors();
            if (monitor < 0 || monitor >= screens.Count) return null;
            var s = screens[monitor];
            var (w, h) = ScreenRules.FrameSize(s.Width, s.Height, width);
            if (w <= 0 || h <= 0) return null;
            var pixels = Grab(s, w, h);
            if (pixels is null) return null;
            var jpeg = await EncodeAsync(pixels, w, h).ConfigureAwait(false);
            Touch();
            return jpeg;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static unsafe byte[]? Grab(ScreenInfo s, int w, int h)
    {
        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;
            var bmi = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(NativeMethods.BITMAPINFOHEADER),
                biWidth = w,
                biHeight = -h, // de cima para baixo
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = NativeMethods.CreateDIBSection(screenDc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;
            old = NativeMethods.SelectObject(memDc, dib);
            NativeMethods.SetStretchBltMode(memDc, NativeMethods.HALFTONE);
            NativeMethods.SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
            if (!NativeMethods.StretchBlt(memDc, 0, 0, w, h, screenDc, s.Left, s.Top, s.Width, s.Height,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                return null;
            DrawCursor(memDc, s, w);
            NativeMethods.GdiFlush();
            var bytes = new byte[w * h * 4];
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (old != IntPtr.Zero) NativeMethods.SelectObject(memDc, old);
            if (dib != IntPtr.Zero) NativeMethods.DeleteObject(dib);
            if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Desenha o cursor (se estiver visível) na posição certa do quadro reduzido.</summary>
    private static unsafe void DrawCursor(IntPtr memDc, ScreenInfo s, int frameWidth)
    {
        var ci = new NativeMethods.CURSORINFO { cbSize = (uint)sizeof(NativeMethods.CURSORINFO) };
        if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;
        if (!NativeMethods.GetIconInfo(ci.hCursor, out var ii)) return;
        try
        {
            var scale = (double)frameWidth / s.Width;
            var size = Math.Max(12, (int)Math.Round(NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR) * scale));
            var x = (int)Math.Round((ci.ptScreenPos.X - s.Left - (int)ii.xHotspot) * scale);
            var y = (int)Math.Round((ci.ptScreenPos.Y - s.Top - (int)ii.yHotspot) * scale);
            NativeMethods.DrawIconEx(memDc, x, y, ci.hCursor, size, size, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
        }
        finally
        {
            if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
        }
    }

    private static async Task<byte[]> EncodeAsync(byte[] bgra, int w, int h)
    {
        using var stream = new InMemoryRandomAccessStream();
        var props = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue(JpegQuality, PropertyType.Single) } };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, props);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)w, (uint)h, 96, 96, bgra);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        reader.DetachStream();
        return bytes;
    }

    // ---- Log da transmissão (nunca por quadro) ----

    private void Touch()
    {
        lock (_shareLock)
        {
            if (!_sharing)
            {
                _sharing = true;
                Log.Write("celular: tela compartilhada");
            }
            _idle ??= new Timer(OnIdle, null, Timeout.Infinite, Timeout.Infinite);
            _idle.Change(ScreenRules.IdleEnd, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnIdle(object? state)
    {
        lock (_shareLock)
        {
            if (!_sharing) return;
            _sharing = false;
            _idle?.Dispose();
            _idle = null;
            Log.Write("celular: tela encerrada");
        }
        // Os quadros (1 MB cada) vão para o monte de objetos grandes: ao encerrar, compacta e devolve a memória (D17: nada fica preso)
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        MemoryTrimmer.Trim();
    }

    public void Dispose()
    {
        lock (_shareLock)
        {
            _idle?.Dispose();
            _idle = null;
            _sharing = false;
        }
        _gate.Dispose();
    }
}
