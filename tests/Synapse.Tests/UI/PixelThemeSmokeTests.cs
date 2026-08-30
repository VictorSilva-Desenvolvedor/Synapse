using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Synapse.Tray.UI;
using Xunit;

namespace Synapse.Tests.UI;

/// <summary>
/// Valida a fundacao pixel art antes de qualquer tela ser portada: o tema carrega,
/// as fontes resolvem pelo pack URI, o chrome renderiza e o PNG sai em 1:1.
/// Se este teste falhar, nao adianta olhar as telas.
/// </summary>
[Collection(WpfCaptureCollection.Name)]
public sealed class PixelThemeSmokeTests
{
    private readonly WpfAppFixture _fixture;

    public PixelThemeSmokeTests(WpfAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void Tema_Carrega_E_Resolve_As_Fontes_Pixel()
    {
        _fixture.Invoke(() =>
        {
            PixelWindow.EnsureTheme();

            var display = (FontFamily)Application.Current.FindResource("FontDisplay");
            var body = (FontFamily)Application.Current.FindResource("FontBody");

            Assert.Contains("Press Start 2P", display.Source);
            Assert.Contains("Silkscreen", body.Source);

            // Checar FamilyNames nao vale nada: quando o pack URI esta errado o WPF cai
            // silenciosamente na fonte de fallback e FamilyNames continua preenchido.
            // Fonts.GetFontFamilies enumera o que existe DE FATO naquele local, entao
            // uma lista sem os nomes esperados reprova - que e o comportamento correto,
            // porque fallback aparecendo no lugar de fonte pixel e reprovacao automatica.
            var encontradas = Fonts
                .GetFontFamilies(new Uri("pack://application:,,,/Synapse.Tray;component/Resources/Fonts/"))
                .SelectMany(f => f.FamilyNames.Values)
                .ToList();

            Assert.Contains("Press Start 2P", encontradas);
            Assert.Contains("Silkscreen", encontradas);
        });
    }

    [Fact]
    public void Captura_Produz_Png_No_Tamanho_Exato_Da_Janela()
    {
        const int largura = 640;
        const int altura = 400;

        var path = WpfScreenshot.Capture(
            _fixture,
            () => new PixelWindow
            {
                Title = "SMOKE TEST",
                Subtitle = "Fundacao pixel art: chrome, bevel, fontes e captura 1:1.",
                Width = largura,
                Height = altura,
                Content = BuildProbeContent()
            },
            "00_PixelThemeSmoke.png");

        Assert.True(File.Exists(path), $"PNG nao foi gravado em {path}");

        var info = new FileInfo(path);
        Assert.True(info.Length > 2000, $"PNG suspeito de estar vazio: {info.Length} bytes");

        // O PNG precisa sair exatamente no tamanho da janela em DIP. Se sair diferente,
        // houve reamostragem em algum ponto da cadeia e a Lei Zero foi violada.
        using var stream = File.OpenRead(path);
        var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
            stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        Assert.Equal(largura, frame.PixelWidth);
        Assert.Equal(altura, frame.PixelHeight);

        // O PNG grava resolucao no chunk pHYs em pixels por METRO, como inteiro:
        // 96 DPI = 3779,527 px/m, que arredonda para 3779 e volta como 95,9866.
        // A perda e do formato, nao reamostragem - por isso a tolerancia de 0,1.
        Assert.InRange(frame.DpiX, 95.9, 96.1);
        Assert.InRange(frame.DpiY, 95.9, 96.1);
    }

    /// <summary>Uma amostra de cada primitiva do design system, para inspecao visual.</summary>
    private static UIElement BuildProbeContent()
    {
        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = "Corpo em Silkscreen 16px.",
            Style = (Style)Application.Current.FindResource("PixelTextBase"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = "Legenda secundaria.",
            Style = (Style)Application.Current.FindResource("PixelCaption"),
            Margin = new Thickness(0, 0, 0, 16)
        });

        var linhaBotoes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (rotulo, estilo) in new[]
                 {
                     ("PRIMARIO", "PixelButtonPrimary"),
                     ("SECUNDARIO", "PixelButtonSecondary"),
                     ("PERIGO", "PixelButtonDanger"),
                     ("GHOST", "PixelButtonGhost")
                 })
        {
            linhaBotoes.Children.Add(new Button
            {
                Content = rotulo,
                Style = (Style)Application.Current.FindResource(estilo),
                Margin = new Thickness(0, 0, 8, 0)
            });
        }

        root.Children.Add(linhaBotoes);

        root.Children.Add(new TextBox
        {
            Text = "Campo de entrada com moldura afundada",
            Margin = new Thickness(0, 16, 0, 0)
        });

        var card = new Border
        {
            Style = (Style)Application.Current.FindResource("PixelCard"),
            Margin = new Thickness(0, 16, 0, 0),
            Child = new Border
            {
                Style = (Style)Application.Current.FindResource("PixelCardInner"),
                Child = new TextBlock
                {
                    Text = "Card com bevel 8-bit: luz em cima/esquerda, sombra embaixo/direita.",
                    Margin = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        root.Children.Add(card);
        return root;
    }
}
