using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Synapse.Tray.UI;

public enum SynapseButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

/// <summary>
/// Botão em estilo Pixel Art / Arcade Retrô com relevo 3D táctil e deslocamento ao clicar.
/// </summary>
public sealed class SynapseButton : Button
{
    private bool _hovering;
    private bool _pressed;

    public SynapseButtonVariant Variant { get; set; } = SynapseButtonVariant.Secondary;
    public int Radius { get; set; } = 0; // Mantido para compatibilidade

    /// <summary>Cor de preenchimento customizada, sobrepõe a cor da <see cref="Variant"/>.</summary>
    public Color? FillOverride { get; set; }

    public SynapseButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = SynapseTheme.FontBodyBold(8.5f);
        Height = 32;

        MouseEnter += (_, _) => { _hovering = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovering = false; _pressed = false; Invalidate(); };
        MouseDown += (_, _) => { _pressed = true; Invalidate(); };
        MouseUp += (_, _) => { _pressed = false; Invalidate(); };
    }

    private (Color fill, Color lightEdge, Color darkEdge, Color text) GetColors()
    {
        if (!Enabled)
        {
            return (SynapseTheme.Surface, SynapseTheme.Border, Color.Black, SynapseTheme.TextDisabled);
        }

        if (FillOverride is { } custom)
        {
            var fill = _pressed ? ControlPaint.Dark(custom, 0.2f) : _hovering ? ControlPaint.Light(custom, 0.15f) : custom;
            return (fill, ControlPaint.Light(fill, 0.3f), ControlPaint.Dark(fill, 0.4f), Color.White);
        }

        return Variant switch
        {
            SynapseButtonVariant.Primary => (
                _pressed ? SynapseTheme.AccentPrimaryPressed : _hovering ? SynapseTheme.AccentPrimaryHover : SynapseTheme.AccentPrimary,
                Color.FromArgb(180, 255, 255),
                Color.FromArgb(0, 120, 150),
                Color.FromArgb(13, 17, 23)),

            SynapseButtonVariant.Danger => (
                _pressed ? Color.FromArgb(180, 20, 30) : _hovering ? Color.FromArgb(255, 80, 95) : SynapseTheme.Error,
                Color.FromArgb(255, 160, 170),
                Color.FromArgb(120, 10, 20),
                Color.White),

            SynapseButtonVariant.Ghost => (
                _hovering ? SynapseTheme.SurfaceAlt : Color.Transparent,
                _hovering ? SynapseTheme.BorderLight : SynapseTheme.Border,
                _hovering ? Color.Black : SynapseTheme.Border,
                SynapseTheme.TextSecondary),

            _ => (
                _pressed ? SynapseTheme.Surface : _hovering ? SynapseTheme.SurfaceAlt : SynapseTheme.Surface,
                _hovering ? SynapseTheme.BorderHighlight : SynapseTheme.BorderLight,
                Color.Black,
                SynapseTheme.TextPrimary)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        var (fill, lightEdge, darkEdge, textColor) = GetColors();
        var rect = new Rectangle(0, 0, Width, Height);

        // Se pressionado, inverte a luz e a sombra para dar efeito mecânico de clique
        var topColor = _pressed ? darkEdge : lightEdge;
        var bottomColor = _pressed ? lightEdge : darkEdge;
        var offset = _pressed ? 2 : 0;

        // Fundo
        using (var brush = new SolidBrush(fill))
        {
            e.Graphics.FillRectangle(brush, rect);
        }

        // Moldura Bevel 8-bit (2px)
        using (var penTop = new Pen(topColor, 2))
        {
            e.Graphics.DrawLine(penTop, 0, 0, Width - 1, 0);
            e.Graphics.DrawLine(penTop, 0, 0, 0, Height - 1);
        }

        using (var penBottom = new Pen(bottomColor, 2))
        {
            e.Graphics.DrawLine(penBottom, Width - 1, 0, Width - 1, Height - 1);
            e.Graphics.DrawLine(penBottom, 0, Height - 1, Width - 1, Height - 1);
        }

        // Borda externa preta 1px de acabamento pixel art
        using (var penOuter = new Pen(Color.FromArgb(13, 17, 23), 1))
        {
            e.Graphics.DrawRectangle(penOuter, 0, 0, Width - 1, Height - 1);
        }

        // Texto com deslocamento de clique
        var textRect = new Rectangle(offset, offset, Width, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
