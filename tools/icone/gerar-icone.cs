#:package System.Drawing.Common@10.0.0
#pragma warning disable CA1416 // System.Drawing.Common só roda no Windows, e este script também.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Gera o ícone do Mixer de Áudio: quadrado de cantos arredondados azul (#2563EB) com um alto-falante branco.
// Uso, a partir da raiz do repositório:   dotnet run tools\icone\gerar-icone.cs
// Opcional: um argumento com a pasta de saída (padrão: MixerDeAudio\Assets).
// Saída: MixerDeAudio.ico (10 tamanhos; o de 256 em PNG, os outros em 32 bits com alfa) e MixerDeAudio-256.png (para conferência).
// Adaptado de tools\icone\gerar-icone.cs do MeuBloco.

var folder = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "MixerDeAudio", "Assets");
Directory.CreateDirectory(folder);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var images = new List<(int Size, byte[] Data, bool Png)>();
foreach (var size in sizes)
{
    using var bitmap = Render(size);
    if (size == 256)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        images.Add((size, stream.ToArray(), true));
        bitmap.Save(Path.Combine(folder, "MixerDeAudio-256.png"), ImageFormat.Png);
    }
    else
    {
        images.Add((size, Dib(bitmap), false));
    }
}

using (var file = File.Create(Path.Combine(folder, "MixerDeAudio.ico")))
using (var writer = new BinaryWriter(file))
{
    writer.Write((ushort)0);              // reservado
    writer.Write((ushort)1);              // tipo: ícone
    writer.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    foreach (var (size, data, _) in images)
    {
        writer.Write((byte)(size >= 256 ? 0 : size)); // 0 = 256
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);            // sem paleta
        writer.Write((byte)0);
        writer.Write((ushort)1);          // planos
        writer.Write((ushort)32);         // bits por pixel
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data, _) in images) writer.Write(data);
}

Console.WriteLine($"Gerado em {folder}: MixerDeAudio.ico ({images.Count} tamanhos: {string.Join(", ", sizes)}) e MixerDeAudio-256.png");

// ---------- Desenho (grade de 16) ----------

static Bitmap Render(int size)
{
    var s = size / 16f;
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    // fundo: x 1..15, y 1..15, cantos de raio 3,5
    using (var brush = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB)))
    using (var path = RoundedRect(new RectangleF(1 * s, 1 * s, 14 * s, 14 * s), 3.5f * s))
        g.FillPath(brush, path);

    // alto-falante: caixa + cone
    using var white = new SolidBrush(Color.White);
    PointF[] speaker =
    [
        new(3.2f * s, 6.2f * s), new(5.4f * s, 6.2f * s), new(8.0f * s, 3.9f * s),
        new(8.0f * s, 12.1f * s), new(5.4f * s, 9.8f * s), new(3.2f * s, 9.8f * s),
    ];
    g.FillPolygon(white, speaker);

    // ondas sonoras
    using var pen = new Pen(Color.White, MathF.Max(1f, 1.1f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    foreach (var r in new[] { 2.4f, 4.2f })
        g.DrawArc(pen, (8.4f - r) * s, (8f - r) * s, 2 * r * s, 2 * r * s, -45, 90);

    return bitmap;
}

static GraphicsPath RoundedRect(RectangleF r, float radius)
{
    var d = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(r.Left, r.Top, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

// Imagem de ícone em formato DIB: cabeçalho BITMAPINFOHEADER (altura dobrada), pixels BGRA de baixo para cima e máscara AND zerada.
static byte[] Dib(Bitmap bitmap)
{
    var size = bitmap.Width;
    var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var pixels = new byte[size * size * 4];
    for (var y = 0; y < size; y++)
        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, (size - 1 - y) * size * 4, size * 4);
    bitmap.UnlockBits(data);

    var maskRow = (size + 31) / 32 * 4;
    using var stream = new MemoryStream();
    using var w = new BinaryWriter(stream);
    w.Write(40);              // tamanho do cabeçalho
    w.Write(size);
    w.Write(size * 2);        // XOR + AND
    w.Write((ushort)1);
    w.Write((ushort)32);
    w.Write(0);               // sem compressão
    w.Write(pixels.Length + maskRow * size);
    w.Write(0); w.Write(0); w.Write(0); w.Write(0);
    w.Write(pixels);
    w.Write(new byte[maskRow * size]);
    return stream.ToArray();
}
