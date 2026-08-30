using System.Drawing;

namespace Synapse.Tray.UI;

/// <summary>
/// Paleta Verde-Musgo em cores do GDI+.
///
/// Existe porque o icone da bandeja e desenhado pixel a pixel com System.Drawing
/// (a API de bandeja do Windows exige um System.Drawing.Icon), e System.Drawing.Color
/// e System.Windows.Media.Color sao tipos distintos que nao se convertem sozinhos.
///
/// ESTES VALORES SAO UM ESPELHO de PixelTheme.xaml. Ao mudar um token la, mude aqui -
/// PixelPaletteTests trava os dois em sincronia e falha se divergirem.
/// </summary>
public static class PixelPalette
{
    public static readonly Color Void = Color.FromArgb(0x1A, 0x17, 0x12);
    public static readonly Color Surface = Color.FromArgb(0x26, 0x20, 0x19);
    public static readonly Color SurfaceAlt = Color.FromArgb(0x34, 0x2C, 0x22);
    public static readonly Color SurfaceInput = Color.FromArgb(0x14, 0x11, 0x0D);

    public static readonly Color Edge = Color.FromArgb(0x4A, 0x3E, 0x2E);
    public static readonly Color EdgeStrong = Color.FromArgb(0x6B, 0x5B, 0x43);
    public static readonly Color EdgeLight = Color.FromArgb(0xA0, 0x8A, 0x63);
    public static readonly Color EdgeHighlight = Color.FromArgb(0xEF, 0xE0, 0xC2);

    public static readonly Color TextPrimary = Color.FromArgb(0xEF, 0xE0, 0xC2);
    public static readonly Color TextSecondary = Color.FromArgb(0xA8, 0x94, 0x78);
    public static readonly Color TextDisabled = Color.FromArgb(0x6B, 0x5B, 0x43);

    public static readonly Color AccentPrimary = Color.FromArgb(0x7F, 0xB0, 0x69);
    public static readonly Color AccentSecondary = Color.FromArgb(0xA5, 0x85, 0xD6);
    public static readonly Color Success = Color.FromArgb(0xB8, 0xCF, 0x4C);
    public static readonly Color Warning = Color.FromArgb(0xE0, 0xA0, 0x30);
    public static readonly Color Error = Color.FromArgb(0xC4, 0x55, 0x3D);
}
