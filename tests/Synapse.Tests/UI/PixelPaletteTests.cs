using System.Windows;
using System.Windows.Media;
using Synapse.Tray.UI;
using Xunit;
using GdiColor = System.Drawing.Color;

namespace Synapse.Tests.UI;

/// <summary>
/// Trava a paleta GDI+ (PixelPalette, usada no icone da bandeja) e a paleta XAML
/// (PixelTheme.xaml, usada em toda a UI) em sincronia.
///
/// Sao dois tipos Color diferentes que nao se convertem sozinhos, entao a duplicacao
/// e inevitavel - mas ela nao pode divergir em silencio: cor fora da paleta e
/// reprovacao automatica na rubrica pixel art.
/// </summary>
[Collection(WpfCaptureCollection.Name)]
public sealed class PixelPaletteTests
{
    private readonly WpfAppFixture _fixture;

    public PixelPaletteTests(WpfAppFixture fixture) => _fixture = fixture;

    public static TheoryData<string, string> TokenPairs => new()
    {
        // chave do recurso XAML     campo de PixelPalette
        { "VoidColor", nameof(PixelPalette.Void) },
        { "SurfaceColor", nameof(PixelPalette.Surface) },
        { "SurfaceAltColor", nameof(PixelPalette.SurfaceAlt) },
        { "SurfaceInputColor", nameof(PixelPalette.SurfaceInput) },
        { "EdgeColor", nameof(PixelPalette.Edge) },
        { "EdgeStrongColor", nameof(PixelPalette.EdgeStrong) },
        { "EdgeLightColor", nameof(PixelPalette.EdgeLight) },
        { "EdgeHighlightColor", nameof(PixelPalette.EdgeHighlight) },
        { "TextPrimaryColor", nameof(PixelPalette.TextPrimary) },
        { "TextSecondaryColor", nameof(PixelPalette.TextSecondary) },
        { "TextDisabledColor", nameof(PixelPalette.TextDisabled) },
        { "AccentPrimaryColor", nameof(PixelPalette.AccentPrimary) },
        { "AccentSecondaryColor", nameof(PixelPalette.AccentSecondary) },
        { "SuccessColor", nameof(PixelPalette.Success) },
        { "WarningColor", nameof(PixelPalette.Warning) },
        { "ErrorColor", nameof(PixelPalette.Error) }
    };

    [Theory]
    [MemberData(nameof(TokenPairs))]
    public void Token_Xaml_E_Gdi_Tem_A_Mesma_Cor(string xamlKey, string paletteField)
    {
        _fixture.Invoke(() =>
        {
            PixelWindow.EnsureTheme();

            var xamlColor = (Color)Application.Current.FindResource(xamlKey);

            var field = typeof(PixelPalette).GetField(paletteField)
                        ?? throw new InvalidOperationException($"PixelPalette.{paletteField} nao existe.");
            var gdiColor = (GdiColor)field.GetValue(null)!;

            Assert.Equal(gdiColor.R, xamlColor.R);
            Assert.Equal(gdiColor.G, xamlColor.G);
            Assert.Equal(gdiColor.B, xamlColor.B);
        });
    }
}
