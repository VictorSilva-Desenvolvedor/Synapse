using System.Drawing.Drawing2D;

namespace Synapse.Tray.UI;

/// <summary>
/// Painel em estilo Pixel Art com bordas chanfradas e relevo 8-bit / 16-bit.
/// Substitui o antigo RoundedPanel para estética de janela retrô.
/// </summary>
public sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 0; // Mantido para compatibilidade
    public Color BorderColor { get; set; } = SynapseTheme.BorderLight;
    public Color ShadowColor { get; set; } = SynapseTheme.Border;
    public int BorderThickness { get; set; } = 2;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = SynapseTheme.SurfaceAlt;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        var rect = new Rectangle(0, 0, Width, Height);

        // Preenche o fundo do painel
        using (var bg = new SolidBrush(BackColor))
        {
            e.Graphics.FillRectangle(bg, rect);
        }

        // Desenha relevo chanfrado 8-bit (Luz superior/esquerda, Sombra inferior/direita)
        var t = BorderThickness;
        using (var penLight = new Pen(BorderColor, t))
        {
            e.Graphics.DrawLine(penLight, 0, 0, Width - 1, 0);
            e.Graphics.DrawLine(penLight, 0, 0, 0, Height - 1);
        }

        using (var penShadow = new Pen(ShadowColor, t))
        {
            e.Graphics.DrawLine(penShadow, Width - 1, 0, Width - 1, Height - 1);
            e.Graphics.DrawLine(penShadow, 0, Height - 1, Width - 1, Height - 1);
        }
    }

    internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        path.AddRectangle(bounds);
        return path;
    }
}
