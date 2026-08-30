using System.Windows;
using System.Windows.Media;

namespace Synapse.Tray.UI;

/// <summary>
/// Desenha um icone pixel art a partir de uma matriz de caracteres, um quadrado por
/// pixel logico. Mesmo idioma do <see cref="IconGenerator"/> (que faz o icone da
/// bandeja em GDI+), mas em WPF.
///
/// Retangulos preenchidos em coordenadas inteiras, com escala inteira e EdgeMode
/// Aliased: nao existe caminho para o WPF interpolar nada aqui. Um Path vetorial ou
/// um PNG escalado violariam a Lei Zero na primeira mudanca de tamanho.
/// </summary>
public sealed class PixelIcon : FrameworkElement
{
    /// <summary>Linhas separadas por '|'. '.' e vazio; qualquer outro caractere pinta.</summary>
    public static readonly DependencyProperty PatternProperty =
        DependencyProperty.Register(nameof(Pattern), typeof(string), typeof(PixelIcon),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Pattern
    {
        get => (string)GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(PixelIcon),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Escala em pixels por celula. Inteiro por contrato — 1, 2, 3, nunca 1,5.</summary>
    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(int), typeof(PixelIcon),
            new FrameworkPropertyMetadata(2,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public int Scale
    {
        get => (int)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public PixelIcon()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    private string[] Rows =>
        string.IsNullOrEmpty(Pattern)
            ? []
            : Pattern.Split('|', StringSplitOptions.RemoveEmptyEntries);

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Rows;
        if (rows.Length == 0)
        {
            return new Size(0, 0);
        }

        var scale = Math.Max(1, Scale);
        return new Size(rows.Max(r => r.Length) * scale, rows.Length * scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rows = Rows;
        if (rows.Length == 0 || Fill is null)
        {
            return;
        }

        var scale = Math.Max(1, Scale);

        for (var y = 0; y < rows.Length; y++)
        {
            var row = rows[y];

            // Junta celulas pintadas vizinhas num retangulo so: menos primitivas e
            // sem costura visivel entre quadrados adjacentes.
            var x = 0;
            while (x < row.Length)
            {
                if (row[x] == '.')
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < row.Length && row[x] != '.')
                {
                    x++;
                }

                dc.DrawRectangle(
                    Fill,
                    null,
                    new Rect(start * scale, y * scale, (x - start) * scale, scale));
            }
        }
    }
}
