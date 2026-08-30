using System.Drawing;
using System.Windows.Forms;
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

    /// <summary>
    /// Icon.FromHandle(bitmap.GetHicon()) nao assume dono do handle nativo: Icon.Dispose()
    /// nao chama DestroyIcon nesse caso, entao cada icone gerado por este arquivo vaza um
    /// handle de USER/GDI se nao for destruido explicitamente. Como os icones aqui sao
    /// recriados a cada poll da bandeja (SynapseTrayApp.UpdateUI), sem isso o processo
    /// esgota a cota de handles em algumas horas e morre sem excecao gerenciada nem
    /// relatorio de falha (o handle acaba antes do proprio WER conseguir alocar o dele).
    /// </summary>
    public static void ReleaseIcon(Icon? icon)
    {
        if (icon is null) return;
        DestroyIcon(icon.Handle);
        icon.Dispose();
    }

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
            return CreatePixelArtIcon(PixelPalette.Warning); // Âmbar / Pausado
        }

        return estado switch
        {
            "Sincronizado" => CreatePixelArtIcon(PixelPalette.Success),
            "Sincronizando" => CreatePixelArtIcon(PixelPalette.AccentPrimary, PixelPalette.EdgeHighlight),
            "Offline" => CreatePixelArtIcon(PixelPalette.Warning),
            "AuthRequired" => CreatePixelArtIcon(PixelPalette.Error, PixelPalette.Warning),
            "Erro" => CreatePixelArtIcon(PixelPalette.Error),
            _ => CreatePixelArtIcon(PixelPalette.TextSecondary) // Desconectado
        };
    }
}
