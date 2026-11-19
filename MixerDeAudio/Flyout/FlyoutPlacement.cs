using System;

namespace MixerDeAudio.Flyout;

/// <summary>Ponto em pixels físicos (tipo próprio, sem WinUI, para entrar nos testes).</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>Retângulo em pixels físicos.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// Calcula onde a janelinha aparece (Fase 1, D19 e D20): acima do ícone da bandeja, longe da borda da área de trabalho
/// pela margem, em qualquer lado em que a barra de tarefas esteja. Lógica pura.
/// </summary>
public static class FlyoutPlacement
{
    /// <summary>Margem até a borda da área de trabalho, em DIP.</summary>
    public const double MarginDip = 12;

    /// <param name="cursor">Ponto do cursor no clique, em pixels.</param>
    /// <param name="monitor">Retângulo completo do monitor.</param>
    /// <param name="workArea">Área de trabalho do monitor (sem a barra de tarefas).</param>
    /// <param name="scale">Escala do monitor (DPI / 96).</param>
    /// <param name="widthDip">Largura desejada, em DIP.</param>
    /// <param name="heightDip">Altura desejada, em DIP.</param>
    public static PixelRect Compute(PixelPoint cursor, PixelRect monitor, PixelRect workArea, double scale, double widthDip, double heightDip)
    {
        var margin = (int)Math.Round(MarginDip * scale);
        var width = (int)Math.Round(widthDip * scale);
        var height = (int)Math.Round(heightDip * scale);

        // Se a janela for maior que a área útil, encolhe.
        height = Math.Min(height, Math.Max(1, workArea.Height - 2 * margin));
        width = Math.Min(width, Math.Max(1, workArea.Width - 2 * margin));

        int x, y;
        if (workArea.X > monitor.X && workArea.Right == monitor.Right && workArea.Y == monitor.Y && workArea.Bottom == monitor.Bottom)
        {
            // Barra à esquerda
            x = workArea.X + margin;
            y = Clamp(cursor.Y - height / 2, workArea.Y + margin, workArea.Bottom - height - margin);
        }
        else if (workArea.Right < monitor.Right && workArea.X == monitor.X && workArea.Y == monitor.Y && workArea.Bottom == monitor.Bottom)
        {
            // Barra à direita
            x = workArea.Right - width - margin;
            y = Clamp(cursor.Y - height / 2, workArea.Y + margin, workArea.Bottom - height - margin);
        }
        else if (workArea.Y > monitor.Y)
        {
            // Barra em cima
            x = Clamp(cursor.X - width / 2, workArea.X + margin, workArea.Right - width - margin);
            y = workArea.Y + margin;
        }
        else
        {
            // Barra embaixo (ou oculta: a área é igual ao monitor)
            x = Clamp(cursor.X - width / 2, workArea.X + margin, workArea.Right - width - margin);
            y = workArea.Bottom - height - margin;
        }

        return new PixelRect(x, y, width, height);
    }

    // Limita ao intervalo; se o intervalo for invertido, vale o mínimo.
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Min(Math.Max(value, min), max);
}
