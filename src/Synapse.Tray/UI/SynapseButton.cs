using System.Drawing.Drawing2D;

namespace Synapse.Tray.UI;

public enum SynapseButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

/// <summary>Botão com cantos arredondados e estados de hover/pressed, seguindo o design system do Synapse.</summary>
public sealed class SynapseButton : Button
{
    private bool _hovering;
    private bool _pressed;

    public SynapseButtonVariant Variant { get; set; } = SynapseButtonVariant.Secondary;
    public int Radius { get; set; } = SynapseTheme.RadiusSmall;

    /// <summary>Cor de preenchimento customizada, sobrepõe a cor da <see cref="Variant"/> (ex.: botões de avaliação semânticos).</summary>
    public Color? FillOverride { get; set; }

    public SynapseButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = SynapseTheme.FontBodyBold(9.5f);
        Height = 32;
        MouseEnter += (_, _) => { _hovering = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovering = false; _pressed = false; Invalidate(); };
        MouseDown += (_, _) => { _pressed = true; Invalidate(); };
        MouseUp += (_, _) => { _pressed = false; Invalidate(); };
    }

    private (Color fill, Color border, Color text) GetColors()
    {
        if (!Enabled)
        {
            return (SynapseTheme.Surface, SynapseTheme.Border, SynapseTheme.TextDisabled);
        }

        if (FillOverride is { } custom)
        {
            var fill = _pressed ? ControlPaint.Dark(custom, 0.15f) : _hovering ? ControlPaint.Light(custom, 0.1f) : custom;
            return (fill, Color.Transparent, Color.White);
        }

        return Variant switch
        {
            SynapseButtonVariant.Primary => (
                _pressed ? SynapseTheme.AccentPrimaryPressed : _hovering ? SynapseTheme.AccentPrimaryHover : SynapseTheme.AccentPrimary,
                Color.Transparent,
                Color.White),
            SynapseButtonVariant.Danger => (
                _pressed ? Color.FromArgb(185, 28, 28) : _hovering ? Color.FromArgb(220, 38, 38) : SynapseTheme.Error,
                Color.Transparent,
                Color.White),
            SynapseButtonVariant.Ghost => (
                _hovering ? SynapseTheme.Surface : Color.Transparent,
                Color.Transparent,
                SynapseTheme.TextSecondary),
            _ => (
                _hovering ? SynapseTheme.Surface : Color.Transparent,
                SynapseTheme.BorderStrong,
                SynapseTheme.TextPrimary)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var (fill, border, text) = GetColors();

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPanel.RoundedRect(rect, Radius);

        using (var bg = new SolidBrush(fill))
        {
            e.Graphics.FillPath(bg, path);
        }

        if (border != Color.Transparent)
        {
            using var pen = new Pen(border, 1);
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
