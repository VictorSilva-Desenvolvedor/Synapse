using System.Windows;
using System.Windows.Media;

namespace Synapse.Tray.UI;

/// <summary>
/// Propriedades anexadas que parametrizam o bevel 8-bit e os estados dos controles pixel art.
/// Existem para que um unico ControlTemplate sirva a todas as variantes de botao/painel:
/// o template faz TemplateBinding nestas propriedades em vez de duplicar a arvore visual.
/// </summary>
public static class Pixel
{
    /// <summary>Cor da aresta iluminada (topo e esquerda) do relevo 8-bit.</summary>
    public static readonly DependencyProperty LightEdgeProperty =
        DependencyProperty.RegisterAttached(
            "LightEdge", typeof(Brush), typeof(Pixel),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static Brush GetLightEdge(DependencyObject o) => (Brush)o.GetValue(LightEdgeProperty);
    public static void SetLightEdge(DependencyObject o, Brush v) => o.SetValue(LightEdgeProperty, v);

    /// <summary>Cor da aresta sombreada (baixo e direita) do relevo 8-bit.</summary>
    public static readonly DependencyProperty DarkEdgeProperty =
        DependencyProperty.RegisterAttached(
            "DarkEdge", typeof(Brush), typeof(Pixel),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static Brush GetDarkEdge(DependencyObject o) => (Brush)o.GetValue(DarkEdgeProperty);
    public static void SetDarkEdge(DependencyObject o, Brush v) => o.SetValue(DarkEdgeProperty, v);

    /// <summary>Preenchimento no estado hover.</summary>
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "HoverBackground", typeof(Brush), typeof(Pixel),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static Brush GetHoverBackground(DependencyObject o) => (Brush)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush v) => o.SetValue(HoverBackgroundProperty, v);

    /// <summary>Preenchimento no estado pressionado.</summary>
    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.RegisterAttached(
            "PressedBackground", typeof(Brush), typeof(Pixel),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static Brush GetPressedBackground(DependencyObject o) => (Brush)o.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject o, Brush v) => o.SetValue(PressedBackgroundProperty, v);

    /// <summary>
    /// Texto de marca d'agua exibido num <see cref="System.Windows.Controls.TextBox"/>
    /// vazio. O template de PixelTextBox o revela quando <see cref="HasTextProperty"/>
    /// e falso.
    /// </summary>
    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.RegisterAttached(
            "Watermark", typeof(string), typeof(Pixel),
            new FrameworkPropertyMetadata(string.Empty, OnWatermarkChanged));

    public static string GetWatermark(DependencyObject o) => (string)o.GetValue(WatermarkProperty);
    public static void SetWatermark(DependencyObject o, string v) => o.SetValue(WatermarkProperty, v);

    /// <summary>
    /// Espelha "o campo tem texto" como propriedade, porque o TextBox nao expoe isso de
    /// um jeito que um Trigger de ControlTemplate consiga observar.
    /// </summary>
    public static readonly DependencyProperty HasTextProperty =
        DependencyProperty.RegisterAttached(
            "HasText", typeof(bool), typeof(Pixel),
            new FrameworkPropertyMetadata(false));

    public static bool GetHasText(DependencyObject o) => (bool)o.GetValue(HasTextProperty);
    public static void SetHasText(DependencyObject o, bool v) => o.SetValue(HasTextProperty, v);

    private static void OnWatermarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        // Assina uma unica vez: definir a marca d'agua e o gatilho para passar a
        // acompanhar o conteudo do campo.
        box.TextChanged -= SyncHasText;
        box.TextChanged += SyncHasText;
        SetHasText(box, box.Text.Length > 0);
    }

    private static void SyncHasText(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box)
        {
            SetHasText(box, box.Text.Length > 0);
        }
    }

    /// <summary>Subtitulo exibido na barra de topo de uma <see cref="PixelWindow"/>.</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.RegisterAttached(
            "Subtitle", typeof(string), typeof(Pixel),
            new FrameworkPropertyMetadata(string.Empty));

    public static string GetSubtitle(DependencyObject o) => (string)o.GetValue(SubtitleProperty);
    public static void SetSubtitle(DependencyObject o, string v) => o.SetValue(SubtitleProperty, v);

    /// <summary>
    /// Aplica o estado obrigatorio de renderizacao pixel art a um elemento e a toda a sua
    /// subarvore visual (as quatro propriedades sao herdaveis).
    /// Lei Zero: Nearest Neighbor em bitmap, Aliased em forma e em glifo.
    /// </summary>
    public static void ApplyPixelRendering(FrameworkElement element)
    {
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(element, EdgeMode.Aliased);
        TextOptions.SetTextRenderingMode(element, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(element, TextHintingMode.Fixed);
        element.UseLayoutRounding = true;
        element.SnapsToDevicePixels = true;
    }
}
