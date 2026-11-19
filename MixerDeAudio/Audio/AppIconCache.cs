using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MixerDeAudio.Audio;

/// <summary>
/// Ícones dos apps (D18). Os bytes do PNG (32 px) ficam num cache estático por caminho, que sobrevive ao fechar a
/// janela, com no máximo 64 itens (o mais antigo sai). Nunca guarda objetos XAML: cada abertura cria um
/// <see cref="BitmapImage"/> novo a partir dos bytes. A extração roda fora da thread da tela.
/// </summary>
internal static class AppIconCache
{
    public const int MaxItems = 64;
    private const int IconSize = 32;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();

    /// <summary>Bytes do PNG do ícone do .exe (do cache ou extraídos fora da thread da tela). Vazio se não houver ícone.</summary>
    public static async Task<byte[]> GetPngAsync(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return Array.Empty<byte>();
        byte[]? png;
        lock (Gate) Cache.TryGetValue(exePath, out png);
        if (png is null)
        {
            png = await Task.Run(() => Extract(exePath)).ConfigureAwait(false);
            Store(exePath, png);
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

    /// <summary>PNG de 32 px do ícone associado ao arquivo; vazio se falhar (guardado também, para não repetir o erro).</summary>
    private static byte[] Extract(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return Array.Empty<byte>();
            using var source = icon.ToBitmap();
            using var bitmap = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, IconSize, IconSize);
            }
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static void Store(string path, byte[] png)
    {
        lock (Gate)
        {
            if (Cache.ContainsKey(path)) return;
            while (Cache.Count >= MaxItems && Order.Count > 0) Cache.Remove(Order.Dequeue());
            Cache[path] = png;
            Order.Enqueue(path);
        }
    }
}
