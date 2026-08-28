namespace Synapse.Tray.UI;

/// <summary>Paleta escura para o ContextMenuStrip da bandeja, alinhada ao design system do Synapse.</summary>
public sealed class SynapseMenuColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => SynapseTheme.Surface;
    public override Color MenuItemSelectedGradientBegin => SynapseTheme.Surface;
    public override Color MenuItemSelectedGradientEnd => SynapseTheme.Surface;
    public override Color MenuItemBorder => SynapseTheme.AccentPrimary;
    public override Color MenuBorder => SynapseTheme.Border;
    public override Color ToolStripDropDownBackground => SynapseTheme.SurfaceAlt;
    public override Color ImageMarginGradientBegin => SynapseTheme.SurfaceAlt;
    public override Color ImageMarginGradientMiddle => SynapseTheme.SurfaceAlt;
    public override Color ImageMarginGradientEnd => SynapseTheme.SurfaceAlt;
    public override Color SeparatorDark => SynapseTheme.Border;
    public override Color SeparatorLight => SynapseTheme.Border;
    public override Color MenuItemPressedGradientBegin => SynapseTheme.Surface;
    public override Color MenuItemPressedGradientEnd => SynapseTheme.Surface;
}

/// <summary>Renderer escuro para o menu de contexto da bandeja do Synapse.</summary>
public sealed class SynapseMenuRenderer : ToolStripProfessionalRenderer
{
    public SynapseMenuRenderer() : base(new SynapseMenuColorTable())
    {
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var hasCustomColor = e.Item.ForeColor != System.Drawing.SystemColors.ControlText;
        e.TextColor = hasCustomColor ? e.Item.ForeColor : e.Item.Enabled ? SynapseTheme.TextPrimary : SynapseTheme.TextDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(SynapseTheme.Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }
}
