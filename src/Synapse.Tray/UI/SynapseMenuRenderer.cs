using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Synapse.Tray.UI;

/// <summary>Paleta de cores Pixel Art para o ContextMenuStrip da bandeja.</summary>
public sealed class SynapseMenuColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => SynapseTheme.SurfaceAlt;
    public override Color MenuItemSelectedGradientBegin => SynapseTheme.SurfaceAlt;
    public override Color MenuItemSelectedGradientEnd => SynapseTheme.SurfaceAlt;
    public override Color MenuItemBorder => SynapseTheme.AccentPrimary;
    public override Color MenuBorder => SynapseTheme.BorderLight;
    public override Color ToolStripDropDownBackground => SynapseTheme.Surface;
    public override Color ImageMarginGradientBegin => SynapseTheme.Surface;
    public override Color ImageMarginGradientMiddle => SynapseTheme.Surface;
    public override Color ImageMarginGradientEnd => SynapseTheme.Surface;
    public override Color SeparatorDark => SynapseTheme.Border;
    public override Color SeparatorLight => SynapseTheme.Border;
    public override Color MenuItemPressedGradientBegin => SynapseTheme.SurfaceAlt;
    public override Color MenuItemPressedGradientEnd => SynapseTheme.SurfaceAlt;
}

/// <summary>Renderer Pixel Art 8-bit para o menu de contexto da bandeja do Synapse.</summary>
public sealed class SynapseMenuRenderer : ToolStripProfessionalRenderer
{
    public SynapseMenuRenderer() : base(new SynapseMenuColorTable())
    {
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);

        // Borda dupla 8-bit com realce superior
        using var penOuter = new Pen(SynapseTheme.BorderLight, 2);
        e.Graphics.DrawRectangle(penOuter, r);

        using var penInner = new Pen(SynapseTheme.Border, 1);
        e.Graphics.DrawRectangle(penInner, 2, 2, e.ToolStrip.Width - 5, e.ToolStrip.Height - 5);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        if (e.Item.Selected && e.Item.Enabled)
        {
            var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);

            // Fundo de seleção com moldura neon
            using var bg = new SolidBrush(SynapseTheme.SurfaceAlt);
            e.Graphics.FillRectangle(bg, rect);

            using var pen = new Pen(SynapseTheme.AccentPrimary, 1);
            e.Graphics.DrawRectangle(pen, rect);

            // Indicador pixelado à esquerda
            using var markerBrush = new SolidBrush(SynapseTheme.AccentPrimary);
            e.Graphics.FillRectangle(markerBrush, 4, (e.Item.Height / 2) - 3, 4, 6);
        }
        else
        {
            base.OnRenderMenuItemBackground(e);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.Graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        var hasCustomColor = e.Item.ForeColor != SystemColors.ControlText;
        e.TextColor = hasCustomColor ? e.Item.ForeColor : e.Item.Enabled ? (e.Item.Selected ? SynapseTheme.AccentPrimary : SynapseTheme.TextPrimary) : SynapseTheme.TextDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(SynapseTheme.Border, 1);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 6, y, e.Item.Width - 6, y);
    }
}
