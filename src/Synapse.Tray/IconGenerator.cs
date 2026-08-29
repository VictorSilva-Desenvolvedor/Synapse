using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Synapse.Tray.UI;

namespace Synapse.Tray;

/// <summary>
/// Gera ícones de bandeja em Pixel Art autêntico (16x16 / 32x32), desenhados pixel a pixel
/// com bordas sólidas e cores saturadas para cada estado do Synapse.
/// </summary>
public static class IconGenerator
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateStatusIcon(Color color, Color pulseColor) =>
        CreatePixelArtIcon(color, pulseColor);

    public static Icon CreatePixelArtIcon(Color mainColor, Color? highlightColor = null)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.Clear(Color.Transparent);

        // Matriz Pixel Art 16x16 escalada para 32x32 (cada bloco = 2x2 px)
        // Desenho: Cérebro / Nodo Neural Synapse Retrô
        var pattern = new string[]
        {
            "....XXXXXX....",
            "..XX......XX..",
            ".X..######..X.",
            "X..########..X",
            "X.##########.X",
            "X.##..##..##.X",
            "X.##########.X",
            ".X.########.X.",
            ".X..######..X.",
            "..XX.####.XX..",
            "...X..##..X...",
            "....X....X....",
            ".....XXXX.....",
            ".............."
        };

        var borderCol = Color.FromArgb(13, 17, 23);
        var fillCol = mainColor;
        var highCol = highlightColor ?? Color.FromArgb(240, 246, 252);

        var startX = 2;
        var startY = 2;
        const int scale = 2;

        for (var row = 0; row < pattern.Length; row++)
        {
            var line = pattern[row];
            for (var col = 0; col < line.Length; col++)
            {
                var ch = line[col];
                if (ch == '.') continue;

                var pixelColor = ch switch
                {
                    'X' => borderCol,
                    '#' => fillCol,
                    _ => borderCol
                };

                using var brush = new SolidBrush(pixelColor);
                g.FillRectangle(brush, startX + (col * scale), startY + (row * scale), scale, scale);
            }
        }

        // Brilho pixelado no topo esquerdo (2x2 px)
        using (var hBrush = new SolidBrush(highCol))
        {
            g.FillRectangle(hBrush, startX + (4 * scale), startY + (3 * scale), scale, scale);
            g.FillRectangle(hBrush, startX + (5 * scale), startY + (3 * scale), scale, scale);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public static Icon GetIconForState(string estado, bool pausado)
    {
        if (pausado)
        {
            return CreatePixelArtIcon(SynapseTheme.Warning); // Âmbar / Pausado
        }

        return estado switch
        {
            "Sincronizado" => CreatePixelArtIcon(SynapseTheme.NeonGreen),
            "Sincronizando" => CreatePixelArtIcon(SynapseTheme.AccentPrimary, SynapseTheme.BorderHighlight),
            "Offline" => CreatePixelArtIcon(SynapseTheme.Warning),
            "AuthRequired" => CreatePixelArtIcon(SynapseTheme.Error, SynapseTheme.Warning),
            "Erro" => CreatePixelArtIcon(SynapseTheme.Error),
            _ => CreatePixelArtIcon(SynapseTheme.TextSecondary) // Desconectado
        };
    }
}
