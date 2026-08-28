namespace Synapse.Tray.UI;

/// <summary>
/// Identidade visual central do Synapse: paleta escura, tipografia e espaçamentos
/// compartilhados por todas as janelas do app. Baseado no design system "Synapse Dark"
/// (Space Grotesk/Inter, acento esmeralda #10B981 + índigo #6366F1, cantos arredondados).
/// </summary>
public static class SynapseTheme
{
    // Fundos
    public static readonly Color Background = Color.FromArgb(11, 11, 15);
    public static readonly Color Surface = Color.FromArgb(31, 31, 35);
    public static readonly Color SurfaceAlt = Color.FromArgb(24, 24, 27);
    public static readonly Color SurfaceInput = Color.FromArgb(24, 24, 27);
    public static readonly Color Border = Color.FromArgb(42, 42, 48);
    public static readonly Color BorderStrong = Color.FromArgb(63, 63, 70);

    // Texto
    public static readonly Color TextPrimary = Color.FromArgb(244, 244, 245);
    public static readonly Color TextSecondary = Color.FromArgb(161, 161, 170);
    public static readonly Color TextDisabled = Color.FromArgb(113, 113, 122);

    // Acentos
    public static readonly Color AccentPrimary = Color.FromArgb(16, 185, 129);
    public static readonly Color AccentPrimaryHover = Color.FromArgb(5, 150, 105);
    public static readonly Color AccentPrimaryPressed = Color.FromArgb(4, 120, 87);
    public static readonly Color AccentSecondary = Color.FromArgb(99, 102, 241);
    public static readonly Color AccentSecondaryHover = Color.FromArgb(79, 82, 221);

    // Estados
    public static readonly Color Error = Color.FromArgb(239, 68, 68);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color Success = Color.FromArgb(16, 185, 129);
    public static readonly Color Info = Color.FromArgb(99, 102, 241);

    public const string FontFamily = "Segoe UI";
    public const string FontFamilyMono = "Consolas";

    public const int RadiusSmall = 6;
    public const int RadiusMedium = 8;
    public const int RadiusLarge = 12;

    public static Font FontDisplay(float size = 16f) => new(FontFamily, size, FontStyle.Bold);
    public static Font FontHeadline(float size = 12f) => new(FontFamily, size, FontStyle.Bold);
    public static Font FontBody(float size = 9.5f) => new(FontFamily, size, FontStyle.Regular);
    public static Font FontBodyBold(float size = 9.5f) => new(FontFamily, size, FontStyle.Bold);
    public static Font FontCaption(float size = 8.5f) => new(FontFamily, size, FontStyle.Regular);
    public static Font FontCaptionItalic(float size = 8.5f) => new(FontFamily, size, FontStyle.Italic);
    public static Font FontMono(float size = 9.5f) => new(FontFamilyMono, size, FontStyle.Regular);

    /// <summary>Aplica o pano de fundo escuro padrão a uma janela.</summary>
    public static void ApplyFormChrome(Form form)
    {
        // Layouts usam coordenadas em pixel fixas; AutoScaleMode.Font (padrão) reescala
        // de forma não-uniforme conforme a métrica da fonte ambiente e pode desalinhar/
        // sobrepor controles em DPIs diferentes do monitor de desenvolvimento. Dpi escala
        // de forma previsível e uniforme por um único fator.
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = Background;
        form.ForeColor = TextPrimary;
        form.Font = FontBody();
    }

    /// <summary>Cria a barra de topo escura padrão (título + subtítulo) usada em todas as janelas.</summary>
    public static Panel CreateHeaderBar(string title, string subtitle, int height = 60)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = height,
            BackColor = SurfaceAlt,
            Padding = new Padding(20, 10, 20, 10)
        };

        var accent = new Panel
        {
            Dock = DockStyle.Left,
            Width = 3,
            BackColor = AccentPrimary
        };

        var lblTitle = new Label
        {
            Text = title,
            Font = FontHeadline(12f),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(20, string.IsNullOrEmpty(subtitle) ? (height - 20) / 2 : 10)
        };

        panel.Controls.Add(lblTitle);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var lblSubtitle = new Label
            {
                Text = subtitle,
                Font = FontCaption(9f),
                ForeColor = TextSecondary,
                AutoSize = true,
                Location = new Point(20, 33)
            };
            panel.Controls.Add(lblSubtitle);
        }

        panel.Controls.Add(accent);
        return panel;
    }

    /// <summary>Aplica a aparência escura padrão a um TextBox (usar com BorderStyle.FixedSingle).</summary>
    public static void StyleInput(TextBoxBase input)
    {
        input.BackColor = SurfaceInput;
        input.ForeColor = TextPrimary;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = FontBody();
    }

    public static void StyleListView(ListView listView)
    {
        listView.BackColor = SurfaceAlt;
        listView.ForeColor = TextPrimary;
        listView.BorderStyle = BorderStyle.FixedSingle;
        listView.Font = FontBody();
        listView.OwnerDraw = true;
        listView.DrawColumnHeader += (s, e) =>
        {
            using var bg = new SolidBrush(Surface);
            e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, FontBodyBold(9f), e.Bounds, TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        };
        listView.DrawItem += (s, e) => e.DrawDefault = true;
        listView.DrawSubItem += (s, e) => e.DrawDefault = true;
    }

    /// <summary>Painel "card" com fundo elevado e borda sutil de 1px, ao estilo do design system.</summary>
    public static Panel CreateCard(int padding = 16)
    {
        return new RoundedPanel
        {
            BackColor = Surface,
            Padding = new Padding(padding),
            BorderColor = Border,
            Radius = RadiusMedium
        };
    }

    /// <summary>Aplica tema escuro a um TabControl via desenho customizado das abas.</summary>
    public static void StyleTabControl(TabControl tabControl)
    {
        tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabControl.SizeMode = TabSizeMode.Normal;
        tabControl.ItemSize = new Size(0, 32);
        tabControl.Padding = new Point(16, 6);
        tabControl.Font = FontBodyBold(9.5f);

        tabControl.DrawItem += (s, e) =>
        {
            var tc = (TabControl)s!;
            var tabRect = tc.GetTabRect(e.Index);
            var selected = e.Index == tc.SelectedIndex;

            using var bg = new SolidBrush(selected ? Surface : SurfaceAlt);
            e.Graphics.FillRectangle(bg, tabRect);

            if (selected)
            {
                using var accent = new SolidBrush(AccentPrimary);
                e.Graphics.FillRectangle(accent, tabRect.X, tabRect.Bottom - 2, tabRect.Width, 2);
            }

            TextRenderer.DrawText(e.Graphics, tc.TabPages[e.Index].Text, tc.Font, tabRect,
                selected ? TextPrimary : TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        foreach (TabPage page in tabControl.TabPages)
        {
            page.BackColor = Surface;
            page.ForeColor = TextPrimary;
        }
    }

    /// <summary>Badge/pill de status colorido (ex.: "Sincronizado", "Erro", "Pendente").</summary>
    public static Label CreateStatusBadge(string text, Color color)
    {
        var badge = new Label
        {
            Text = "  " + text + "  ",
            AutoSize = true,
            Font = FontBodyBold(8.5f),
            ForeColor = color,
            BackColor = Color.FromArgb(38, color.R, color.G, color.B),
            Padding = new Padding(2)
        };
        return badge;
    }
}
