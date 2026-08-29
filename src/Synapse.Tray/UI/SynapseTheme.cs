using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Synapse.Tray.UI;

/// <summary>
/// Design System Pixel Art (8-bit / 16-bit) do Synapse.Tray.
/// Substitui o antigo tema dark por tipografia pixelada (Silkscreen / Press Start 2P),
/// paleta saturada de alto contraste, bordas retas com bevels 3D e renderização nítida.
/// </summary>
public static class SynapseTheme
{
    #region Cores Pixel Art (Paleta Cyber-Synapse 16-bit)

    // Fundos
    public static readonly Color Background = Color.FromArgb(13, 17, 23);       // #0D1117 (Void / Fundo Principal)
    public static readonly Color Surface = Color.FromArgb(22, 27, 34);          // #161B22 (Painel / Header)
    public static readonly Color SurfaceAlt = Color.FromArgb(33, 38, 45);       // #21262D (Card / Bevel Base)
    public static readonly Color SurfaceInput = Color.FromArgb(18, 22, 28);     // #12161C (Caixas de Texto)

    // Bordas e Bevels 8-bit
    public static readonly Color Border = Color.FromArgb(48, 54, 61);           // #30363D (Sombra escura)
    public static readonly Color BorderLight = Color.FromArgb(139, 148, 158);   // #8B949E (Realce médio)
    public static readonly Color BorderHighlight = Color.FromArgb(240, 246, 252);// #F0F6FC (Realce claro / Luz)
    public static readonly Color BorderStrong = Color.FromArgb(88, 96, 105);    // #586069

    // Texto
    public static readonly Color TextPrimary = Color.FromArgb(240, 246, 252);   // Branco Pixel
    public static readonly Color TextSecondary = Color.FromArgb(139, 148, 158); // Cinza Pixel Claro
    public static readonly Color TextDisabled = Color.FromArgb(72, 79, 88);     // Cinza Escuro

    // Acentos Retrô
    public static readonly Color AccentPrimary = Color.FromArgb(0, 229, 255);        // Ciano Elétrico
    public static readonly Color AccentPrimaryHover = Color.FromArgb(77, 240, 255);
    public static readonly Color AccentPrimaryPressed = Color.FromArgb(0, 180, 204);

    public static readonly Color AccentSecondary = Color.FromArgb(189, 0, 255);      // Roxo Arcade / IA Brain
    public static readonly Color AccentSecondaryHover = Color.FromArgb(209, 64, 255);
    public static readonly Color AccentSecondaryPressed = Color.FromArgb(150, 0, 204);

    public static readonly Color NeonGreen = Color.FromArgb(0, 255, 102);            // Verde Terminal / Sync
    public static readonly Color Success = Color.FromArgb(0, 255, 102);
    public static readonly Color Warning = Color.FromArgb(255, 204, 0);              // Âmbar CRT / Conflito
    public static readonly Color Error = Color.FromArgb(255, 51, 68);                // Vermelho Carmesim
    public static readonly Color Info = Color.FromArgb(0, 229, 255);

    #endregion

    #region Tipografia Pixel Art Embutida (Silkscreen & Press Start 2P)

    private static readonly PrivateFontCollection FontCollection = new();
    private static readonly FontFamily? SilkscreenFamily;
    private static readonly FontFamily? PressStartFamily;

    public const string FallbackFontFamily = "Consolas";
    public const string FallbackFontFamilyMono = "Consolas";

    static SynapseTheme()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var resourceName in asm.GetManifestResourceNames())
            {
                if (resourceName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = asm.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var data = new byte[stream.Length];
                        stream.ReadExactly(data, 0, data.Length);
                        var fontPtr = Marshal.AllocCoTaskMem(data.Length);
                        Marshal.Copy(data, 0, fontPtr, data.Length);
                        FontCollection.AddMemoryFont(fontPtr, data.Length);
                    }
                }
            }

            SilkscreenFamily = FontCollection.Families.FirstOrDefault(f => f.Name.Contains("Silkscreen", StringComparison.OrdinalIgnoreCase))
                               ?? FontCollection.Families.FirstOrDefault();
            PressStartFamily = FontCollection.Families.FirstOrDefault(f => f.Name.Contains("Press Start 2P", StringComparison.OrdinalIgnoreCase))
                               ?? SilkscreenFamily;
        }
        catch
        {
            // Fallback gracioso para fontes monospace nativas
        }
    }

    public static string FontFamily => SilkscreenFamily?.Name ?? FallbackFontFamily;
    public static string FontFamilyMono => FallbackFontFamilyMono;

    public static Font FontDisplay(float size = 10.5f) =>
        PressStartFamily != null
            ? new Font(PressStartFamily, size, FontStyle.Regular)
            : SilkscreenFamily != null
                ? new Font(SilkscreenFamily, size, FontStyle.Bold)
                : new Font(FallbackFontFamily, size, FontStyle.Bold);

    public static Font FontHeadline(float size = 8.5f) =>
        SilkscreenFamily != null
            ? new Font(SilkscreenFamily, size, FontStyle.Bold)
            : PressStartFamily != null
                ? new Font(PressStartFamily, size, FontStyle.Regular)
                : new Font(FallbackFontFamily, size, FontStyle.Bold);

    public static Font FontBody(float size = 8.5f) =>
        SilkscreenFamily != null
            ? new Font(SilkscreenFamily, size, FontStyle.Regular)
            : new Font(FallbackFontFamily, size, FontStyle.Regular);

    public static Font FontBodyBold(float size = 8.5f) =>
        SilkscreenFamily != null
            ? new Font(SilkscreenFamily, size, FontStyle.Bold)
            : new Font(FallbackFontFamily, size, FontStyle.Bold);

    public static Font FontCaption(float size = 8f) =>
        SilkscreenFamily != null
            ? new Font(SilkscreenFamily, size, FontStyle.Regular)
            : new Font(FallbackFontFamily, size, FontStyle.Regular);

    public static Font FontCaptionItalic(float size = 8f) =>
        SilkscreenFamily != null
            ? new Font(SilkscreenFamily, size, FontStyle.Regular)
            : new Font(FallbackFontFamily, size, FontStyle.Italic);

    public static Font FontMono(float size = 9f) =>
        new(FallbackFontFamilyMono, size, FontStyle.Regular);

    public static Font FontPixel(float size = 8.5f, FontStyle style = FontStyle.Regular) =>
        SilkscreenFamily != null
            ? new Font(SilkscreenFamily, size, style)
            : new Font(FallbackFontFamily, size, style);

    #endregion

    #region Helpers Visuais e Desenho 8-Bit

    /// <summary>Aplica o pano de fundo escuro pixel art e fonte global a um formulário.</summary>
    public static void ApplyFormChrome(Form form)
    {
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = Background;
        form.ForeColor = TextPrimary;
        form.Font = FontBody();
    }

    /// <summary>Desenha uma moldura com relevo 3D 8-bit (estilo janela de RPG/Arcade).</summary>
    public static void DrawPixelBevel(Graphics g, Rectangle rect, Color fill, Color topLight, Color bottomShadow, int thickness = 2)
    {
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;

        // Fundo
        using var brush = new SolidBrush(fill);
        g.FillRectangle(brush, rect);

        // Borda Superior e Esquerda (Luz)
        using var penLight = new Pen(topLight, thickness);
        g.DrawLine(penLight, rect.Left, rect.Top, rect.Right - 1, rect.Top);
        g.DrawLine(penLight, rect.Left, rect.Top, rect.Left, rect.Bottom - 1);

        // Borda Inferior e Direita (Sombra)
        using var penShadow = new Pen(bottomShadow, thickness);
        g.DrawLine(penShadow, rect.Right - 1, rect.Top, rect.Right - 1, rect.Bottom - 1);
        g.DrawLine(penShadow, rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
    }

    /// <summary>Cria a barra de topo arcade retrô (título pixelado + subtítulo).</summary>
    public static Panel CreateHeaderBar(string title, string subtitle, int height = 68)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = height,
            BackColor = Surface,
            Padding = new Padding(0)
        };

        panel.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var penDark = new Pen(Border, 2);
            e.Graphics.DrawLine(penDark, 0, panel.Height - 2, panel.Width, panel.Height - 2);
            using var penLight = new Pen(BorderLight, 1);
            e.Graphics.DrawLine(penLight, 0, panel.Height - 1, panel.Width, panel.Height - 1);
        };

        var accent = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(6, height),
            BackColor = AccentPrimary,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        var lblTitle = new Label
        {
            Text = title,
            Font = FontHeadline(8.5f),
            ForeColor = TextPrimary,
            Location = new Point(16, 10),
            Size = new Size(800, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        var lblSubtitle = new Label
        {
            Text = subtitle,
            Font = FontCaption(8f),
            ForeColor = TextSecondary,
            Location = new Point(16, 36),
            Size = new Size(800, 20),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        panel.Controls.Add(accent);
        panel.Controls.Add(lblTitle);
        panel.Controls.Add(lblSubtitle);
        return panel;
    }

    /// <summary>Cria um painel de cartão com bordas chanfradas 8-bit.</summary>
    public static RoundedPanel CreateCard()
    {
        return new RoundedPanel
        {
            BackColor = SurfaceAlt,
            BorderColor = BorderLight,
            Padding = new Padding(12)
        };
    }

    /// <summary>Estiliza caixas de texto com moldura pixelada e fundo de terminal escuro.</summary>
    public static void StyleInput(TextBox tb)
    {
        tb.BackColor = SurfaceInput;
        tb.ForeColor = TextPrimary;
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.Font = FontBody(9f);
    }

    public static void StyleInput(RichTextBox rtb)
    {
        rtb.BackColor = SurfaceInput;
        rtb.ForeColor = TextPrimary;
        rtb.BorderStyle = BorderStyle.FixedSingle;
        rtb.Font = FontBody(9f);
    }

    /// <summary>Estiliza TabControl com abas em estilo 8-bit com realce neon.</summary>
    public static void StyleTabControl(TabControl tc)
    {
        tc.DrawMode = TabDrawMode.OwnerDrawFixed;
        tc.ItemSize = new Size(220, 34);
        tc.SizeMode = TabSizeMode.Fixed;

        tc.DrawItem += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            var tabRect = tc.GetTabRect(e.Index);
            var isSelected = tc.SelectedIndex == e.Index;

            var bgCol = isSelected ? SurfaceAlt : Surface;
            using var brush = new SolidBrush(bgCol);
            g.FillRectangle(brush, tabRect);

            // Borda pixel 8-bit
            using var pen = new Pen(isSelected ? AccentPrimary : Border, 2);
            g.DrawRectangle(pen, tabRect.X + 1, tabRect.Y + 1, tabRect.Width - 2, tabRect.Height - 2);

            var text = tc.TabPages[e.Index].Text;
            var textCol = isSelected ? AccentPrimary : TextSecondary;
            var font = isSelected ? FontHeadline(7.5f) : FontBody(8f);

            TextRenderer.DrawText(g, text, font, tabRect, textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
    }

    /// <summary>Estiliza ListView para visual de tabela/inventário 8-bit.</summary>
    public static void StyleListView(ListView lv)
    {
        lv.BackColor = SurfaceInput;
        lv.ForeColor = TextPrimary;
        lv.BorderStyle = BorderStyle.FixedSingle;
        lv.Font = FontBody(8.5f);
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
    }

    /// <summary>Cria uma etiqueta de estado / badge em estilo pixel art.</summary>
    public static Label CreateStatusBadge(string text, Color accent)
    {
        var lbl = new Label
        {
            Text = text,
            Font = FontCaption(8f),
            ForeColor = TextPrimary,
            BackColor = Surface,
            AutoSize = true,
            Padding = new Padding(8, 4, 8, 4),
            TextAlign = ContentAlignment.MiddleCenter
        };

        lbl.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            var r = new Rectangle(0, 0, lbl.Width - 1, lbl.Height - 1);
            using var penAccent = new Pen(accent, 2);
            e.Graphics.DrawRectangle(penAccent, r);
        };

        return lbl;
    }

    /// <summary>Cria uma mensagem de estado vazio para listas e painéis.</summary>
    public static Label CreateEmptyState(string text, Color? backColor = null)
    {
        return new Label
        {
            Text = text,
            Font = FontCaption(8.5f),
            ForeColor = TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = backColor ?? SurfaceAlt
        };
    }

    public static void FillLastColumn(ListView lv, int minWidth = 100)
    {
        lv.Resize += (_, _) =>
        {
            if (lv.Columns.Count == 0) return;
            var totalWidth = lv.ClientSize.Width;
            for (var i = 0; i < lv.Columns.Count - 1; i++)
            {
                totalWidth -= lv.Columns[i].Width;
            }
            lv.Columns[^1].Width = Math.Max(minWidth, totalWidth);
        };
    }

    #endregion
}
