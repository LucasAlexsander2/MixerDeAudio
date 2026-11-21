using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using MixerDeAudio.Interop;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MixerDeAudio.Audio;

/// <summary>Tamanho em pixels, passado por valor ao shell (<c>SIZE</c>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShellSize
{
    public int Width;
    public int Height;
}

/// <summary><c>IShellItemImageFactory</c> (Fase 8, D5): o shell devolve o ícone do item já como bitmap com alfa.</summary>
[GeneratedComInterface]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
internal partial interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(ShellSize size, int flags, out IntPtr bitmap);
}

/// <summary>
/// Ícones dos apps (D18 da Fase 1; Fase 8, D5). Os bytes do PNG (32 px) ficam num cache estático por chave, que
/// sobrevive ao esconder a janela, com no máximo 64 itens (o mais antigo sai). A chave é o caminho do .exe ou
/// <c>aumid:&lt;id&gt;</c> (apps da Loja, que o shell só resolve pela pasta de apps). Nunca guarda objetos XAML: cada
/// abertura cria um <see cref="BitmapImage"/> novo a partir dos bytes. A extração roda fora da thread da tela, numa
/// thread STA (a pasta de apps do shell pede STA).
/// </summary>
internal static class AppIconCache
{
    public const int MaxItems = 64;
    public const string AumidPrefix = "aumid:";
    private const int IconSize = 32;
    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    private static readonly Guid ImageFactoryId = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private static readonly Guid AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();

    private readonly record struct RawImage(int Width, int Height, byte[] Bgra);

    /// <summary>Bytes do PNG do ícone (do cache ou extraídos fora da thread da tela). Vazio se não houver ícone.</summary>
    /// <param name="key">Caminho do .exe, ou <c>aumid:&lt;id&gt;</c>.</param>
    public static async Task<byte[]> GetPngAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Array.Empty<byte>();
        byte[]? png;
        lock (Gate) Cache.TryGetValue(key, out png);
        if (png is null)
        {
            png = Array.Empty<byte>();
            try
            {
                var raw = await ExtractOnStaAsync(key).ConfigureAwait(false);
                if (raw is { } r) png = await EncodePngAsync(r).ConfigureAwait(false);
            }
            catch
            {
                // sem ícone: guarda o vazio, para não repetir o erro
            }
            Store(key, png);
        }
        return png;
    }

    /// <summary>Cria um <see cref="BitmapImage"/> novo a partir dos bytes. Só na thread da tela.</summary>
    public static async Task<BitmapImage?> CreateImageAsync(byte[] png)
    {
        if (png.Length == 0) return null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static Task<RawImage?> ExtractOnStaAsync(string key)
    {
        var source = new TaskCompletionSource<RawImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { source.SetResult(Extract(key)); }
            catch { source.SetResult(null); }
        }) { IsBackground = true, Name = "icone" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return source.Task;
    }

    /// <summary>Pede o ícone ao shell (32 px, só o ícone) e lê o HBITMAP como BGRA. Nulo se falhar.</summary>
    private static unsafe RawImage? Extract(string key)
    {
        var itemPtr = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            var hr = key.StartsWith(AumidPrefix, StringComparison.Ordinal)
                ? NativeMethods.SHCreateItemInKnownFolder(AppsFolder, 0, key[AumidPrefix.Length..], ImageFactoryId, out itemPtr)
                : NativeMethods.SHCreateItemFromParsingName(key, IntPtr.Zero, ImageFactoryId, out itemPtr);
            if (hr != 0 || itemPtr == IntPtr.Zero) return null;

            factory = (IShellItemImageFactory)AumidResolver.Wrappers.GetOrCreateObjectForComInstance(itemPtr, CreateObjectFlags.None);
            if (factory.GetImage(new ShellSize { Width = IconSize, Height = IconSize }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out bitmap) != 0
                || bitmap == IntPtr.Zero)
                return null;

            if (NativeMethods.GetObjectW(bitmap, sizeof(NativeMethods.BITMAP), out var info) == 0) return null;
            var width = info.bmWidth;
            var height = Math.Abs(info.bmHeight);
            if (width <= 0 || height <= 0) return null;

            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(NativeMethods.BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // de cima para baixo
                biPlanes = 1,
                biBitCount = 32,
            };
            var pixels = new byte[width * height * 4];
            var hdc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                int lines;
                fixed (byte* p = pixels) lines = NativeMethods.GetDIBits(hdc, bitmap, 0, (uint)height, p, ref header, 0);
                if (lines == 0) return null;
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            }

            // Bitmap sem canal alfa (todos os alfas em 0): torna opaco.
            var anyAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha)
                for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new RawImage(width, height, pixels);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if ((object?)factory is ComObject com) com.FinalRelease(); // soltura determinística na própria thread STA
            if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);
        }
    }

    /// <summary>Codifica o BGRA como PNG de 32 px (reduz se o shell devolveu maior).</summary>
    private static async Task<byte[]> EncodePngAsync(RawImage raw)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raw.Width, (uint)raw.Height, 96, 96, raw.Bgra);
        if (raw.Width != IconSize || raw.Height != IconSize)
        {
            encoder.BitmapTransform.ScaledWidth = IconSize;
            encoder.BitmapTransform.ScaledHeight = IconSize;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        reader.DetachStream();
        return bytes;
    }

    private static void Store(string key, byte[] png)
    {
        lock (Gate)
        {
            if (Cache.ContainsKey(key)) return;
            while (Cache.Count >= MaxItems && Order.Count > 0) Cache.Remove(Order.Dequeue());
            Cache[key] = png;
            Order.Enqueue(key);
        }
    }
}
