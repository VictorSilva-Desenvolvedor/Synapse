using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Synapse.Tray.UI;

/// <summary>
/// Janela base de todas as telas do Synapse em estetica pixel art.
/// Substitui SynapseTheme.ApplyFormChrome + CreateHeaderBar do WinForms: o chrome
/// (moldura 8-bit, barra de topo com faixa de acento, titulo, subtitulo e botao fechar)
/// vive no ControlTemplate de PixelWindowStyle, nao em codigo de layout por tela.
/// </summary>
public class PixelWindow : Window
{
    /// <summary>Linha de apoio exibida abaixo do titulo na barra de topo.</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PixelWindow),
            new PropertyMetadata(string.Empty));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>
    /// Esconde a barra de topo de 64px. Telas de entrada rapida (a Captura) usam
    /// chrome proprio: a moldura 8-bit permanece, mas titulo e subtitulo saem para
    /// que a janela caiba em duas linhas de altura.
    /// </summary>
    public static readonly DependencyProperty ShowTitleBarProperty =
        DependencyProperty.Register(nameof(ShowTitleBar), typeof(bool), typeof(PixelWindow),
            new PropertyMetadata(true));

    public bool ShowTitleBar
    {
        get => (bool)GetValue(ShowTitleBarProperty);
        set => SetValue(ShowTitleBarProperty, value);
    }

    /// <summary>Cor da faixa de acento a esquerda do titulo. Ciano por padrao.</summary>
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(PixelWindow),
            new PropertyMetadata(null));

    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public PixelWindow()
    {
        EnsureTheme();

        // Chrome proprio: a barra de titulo do Windows tem cantos arredondados e
        // sombra difusa no Win11, o que reprovaria a tela na rubrica.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("VoidBrush");
        Foreground = (Brush)FindResource("TextPrimaryBrush");
        FontFamily = (FontFamily)FindResource("FontBody");
        FontSize = (double)FindResource("FontSizeBody");

        Pixel.ApplyPixelRendering(this);
        SetResourceReference(StyleProperty, "PixelWindowStyle");
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_TitleBar") is FrameworkElement titleBar)
        {
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            };
        }

        if (GetTemplateChild("PART_Close") is System.Windows.Controls.Button close)
        {
            close.Click += (_, _) => Close();
        }
    }

    /// <summary>
    /// Fecha com Esc. Dialogos pixel art nao tem botao de sistema, entao o teclado
    /// precisa de uma saida garantida.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ResizeMode == ResizeMode.NoResize)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private static bool _themeLoaded;

    /// <summary>
    /// Garante que PixelTheme.xaml esteja em Application.Current.Resources.
    /// Cria uma Application se nao houver: o harness de captura e os testes rodam
    /// sem App, e sem ela os pack URIs das fontes nao resolvem.
    /// </summary>
    public static void EnsureTheme()
    {
        if (_themeLoaded && Application.Current is not null)
        {
            return;
        }

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var alreadyMerged = app.Resources.MergedDictionaries
            .Any(d => d.Source?.OriginalString.Contains("PixelTheme.xaml", StringComparison.OrdinalIgnoreCase) == true);

        if (!alreadyMerged)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Synapse.Tray;component/UI/PixelTheme.xaml", UriKind.Absolute)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Synapse.Tray;component/UI/PixelWindowStyle.xaml", UriKind.Absolute)
            });
        }

        _themeLoaded = true;
    }
}
