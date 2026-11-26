#:package System.Drawing.Common@10.0.0
#pragma warning disable CA1416 // System.Drawing.Common só roda no Windows, e este script também.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Gera o ícone do PWA da Controladora PC (Fase 14, D16 e P2): fundo #0f1218 e uma grade 2 x 2 de blocos arredondados
// com traço #8fb4ff, o de cima à esquerda preenchido, com margem de 20% para o Android poder recortar.
// Uso, a partir da raiz do repositório:   dotnet run tools\icone\icone.cs
// Opcional: um argumento com a pasta de saída (padrão: MixerDeAudio\Remote\Web\app).
// Saída: icone-180.png, icone-192.png e icone-512.png. Só roda de novo se o ícone mudar.

var folder = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "MixerDeAudio", "Remote", "Web", "app");
Directory.CreateDirectory(folder);

foreach (var size in new[] { 180, 192, 512 })
{
    using var bitmap = Render(size);
    bitmap.Save(Path.Combine(folder, $"icone-{size}.png"), ImageFormat.Png);
}
Console.WriteLine("ícones gerados em " + folder);

static Bitmap Render(int size)
{
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(ColorTranslator.FromHtml("#0f1218"));

    var accent = ColorTranslator.FromHtml("#8fb4ff");
    float margin = size * 0.20f;
    float content = size - 2 * margin;
    float gap = content * 0.10f;
    float block = (content - gap) / 2f;
    float stroke = Math.Max(2f, content * 0.055f);
    float radius = block * 0.26f;

    using var pen = new Pen(accent, stroke) { LineJoin = LineJoin.Round };
    using var brush = new SolidBrush(accent);
    for (int row = 0; row < 2; row++)
    {
        for (int col = 0; col < 2; col++)
        {
            float x = margin + col * (block + gap);
            float y = margin + row * (block + gap);
            // o traço fica por dentro do bloco: recua meia espessura
            var rect = new RectangleF(x + stroke / 2, y + stroke / 2, block - stroke, block - stroke);
            using var path = RoundedRect(rect, radius);
            if (row == 0 && col == 0) g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
    }
    return bitmap;
}

static GraphicsPath RoundedRect(RectangleF r, float radius)
{
    float d = radius * 2;
    var path = new GraphicsPath();
    path.AddArc(r.Left, r.Top, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
