using System.Drawing.Drawing2D;
using Synapse.Sync.Backup;
using Synapse.Sync.Config;
using Synapse.Sync.Metrics;
using Synapse.Tray.UI;

namespace Synapse.Tray.Metrics;

/// <summary>
/// Painel visual de estatísticas, produtividade e exportação de backup criptografado (V8.3).
/// </summary>
public sealed class VaultStatsForm : Form
{
    private readonly SynapseConfigManager _configManager;
    private readonly Label _lblNotes;
    private readonly Label _lblWords;
    private readonly Label _lblReadingTime;
    private readonly Label _lblRecentActivity;
    private readonly ListView _lstCategories;
    private readonly SynapseButton _btnExportBackup;
    private string _vaultPath = string.Empty;

    public VaultStatsForm(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        Text = "Synapse — Estatísticas && Backup do Cofre [Pixel Edition]";
        Size = new Size(760, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            "► ESTATÍSTICAS && BACKUP DO COFRE",
            "Acompanhe seu volume de escrita, distribuição temática e gere backups seguros.",
            70);
        Controls.Add(pnlHeader);

        // Cards Panel
        var pnlCards = new Panel
        {
            Location = new Point(20, 84),
            Size = new Size(705, 120),
            BackColor = SynapseTheme.Background
        };

        var card1 = CreateKpiCard("TOTAL DE NOTAS", out _lblNotes, 0, SynapseTheme.NeonGreen);
        var card2 = CreateKpiCard("TOTAL PALAVRAS", out _lblWords, 178, SynapseTheme.AccentPrimary);
        var card3 = CreateKpiCard("TEMPO LEITURA", out _lblReadingTime, 356, SynapseTheme.Warning);
        var card4 = CreateKpiCard("ATIVIDADE (7D)", out _lblRecentActivity, 534, SynapseTheme.AccentSecondary);

        pnlCards.Controls.Add(card1);
        pnlCards.Controls.Add(card2);
        pnlCards.Controls.Add(card3);
        pnlCards.Controls.Add(card4);
        Controls.Add(pnlCards);

        // Categories Header
        var lblCatHeader = new Label
        {
            Text = "► DISTRIBUIÇÃO POR CATEGORIAS (PKM):",
            Font = SynapseTheme.FontHeadline(8.5f),
            ForeColor = SynapseTheme.AccentPrimary,
            Location = new Point(20, 218),
            AutoSize = true
        };
        Controls.Add(lblCatHeader);

        // Categories ListView
        _lstCategories = new ListView
        {
            Location = new Point(20, 246),
            Size = new Size(705, 260),
            View = View.Details,
            FullRowSelect = true,
            GridLines = false
        };
        SynapseTheme.StyleListView(_lstCategories);
        _lstCategories.Columns.Add("Categoria", 300);
        _lstCategories.Columns.Add("Qtd. Notas", 180);
        _lstCategories.Columns.Add("Proporção", 200);
        SynapseTheme.FillLastColumn(_lstCategories, 180);
        Controls.Add(_lstCategories);

        // Footer Actions
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = SynapseTheme.Surface,
            Padding = new Padding(16, 12, 16, 12)
        };
        pnlFooter.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var penDark = new Pen(SynapseTheme.Border, 2);
            e.Graphics.DrawLine(penDark, 0, 0, pnlFooter.Width, 0);
            using var penLight = new Pen(SynapseTheme.BorderLight, 1);
            e.Graphics.DrawLine(penLight, 0, 1, pnlFooter.Width, 1);
        };

        var btnRefresh = new SynapseButton
        {
            Text = "► Atualizar",
            Location = new Point(20, 16),
            Size = new Size(160, 36),
            Variant = SynapseButtonVariant.Secondary
        };
        btnRefresh.Click += async (_, _) => await LoadMetricsAsync();

        _btnExportBackup = new SynapseButton
        {
            Text = "► Exportar Backup (.enc)...",
            Location = new Point(pnlFooter.Width - 280, 16),
            Size = new Size(260, 36),
            Variant = SynapseButtonVariant.Primary,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnExportBackup.Click += async (_, _) => await ExportBackupAsync();

        pnlFooter.Controls.Add(btnRefresh);
        pnlFooter.Controls.Add(_btnExportBackup);
        Controls.Add(pnlFooter);
        pnlHeader.BringToFront();
        pnlFooter.BringToFront();

        Shown += async (_, _) => await LoadMetricsAsync();
    }

    private static Panel CreateKpiCard(string title, out Label valueLabel, int left, Color accentColor)
    {
        var panel = new RoundedPanel
        {
            Location = new Point(left, 0),
            Size = new Size(168, 115),
            BackColor = SynapseTheme.SurfaceAlt,
            BorderColor = SynapseTheme.BorderLight,
            Padding = new Padding(10)
        };

        var accentBar = new Panel { Location = new Point(2, 2), Size = new Size(164, 3), BackColor = accentColor };

        var lblT = new Label
        {
            Text = title,
            Font = SynapseTheme.FontHeadline(7.5f),
            ForeColor = SynapseTheme.TextSecondary,
            Location = new Point(8, 14),
            Size = new Size(150, 24)
        };

        valueLabel = new Label
        {
            Text = "...",
            Font = SynapseTheme.FontHeadline(10.5f),
            ForeColor = SynapseTheme.TextPrimary,
            Location = new Point(8, 54),
            Size = new Size(150, 40),
            TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(accentBar);
        panel.Controls.Add(lblT);
        panel.Controls.Add(valueLabel);
        return panel;
    }

    private async Task LoadMetricsAsync()
    {
        var config = await _configManager.LoadAsync();
        _vaultPath = config.VaultPath;

        if (string.IsNullOrEmpty(_vaultPath) || !Directory.Exists(_vaultPath))
        {
            _lblNotes.Text = "0";
            _lblWords.Text = "0";
            _lblReadingTime.Text = "0 min";
            _lblRecentActivity.Text = "0 notas";
            return;
        }

        var report = await Task.Run(async () => await VaultMetricsCollector.CollectMetricsAsync(_vaultPath));

        _lblNotes.Text = $"{report.TotalNotes:N0}";
        _lblWords.Text = $"{report.TotalWords:N0}";
        _lblReadingTime.Text = $"~{report.EstimatedReadingMinutes} min";
        _lblRecentActivity.Text = $"{report.NotesCreatedLast7Days} (7d)";

        _lstCategories.Items.Clear();
        foreach (var (cat, count) in report.CategoryCounts.OrderByDescending(c => c.Value))
        {
            var percentage = report.TotalNotes > 0 ? (count * 100.0 / report.TotalNotes) : 0;
            var item = new ListViewItem(cat);
            item.SubItems.Add(count.ToString());
            item.SubItems.Add($"{percentage:F1}%");
            _lstCategories.Items.Add(item);
        }
    }

    private async Task ExportBackupAsync()
    {
        if (string.IsNullOrEmpty(_vaultPath) || !Directory.Exists(_vaultPath))
        {
            MessageBox.Show("Cofre não configurado.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title = "Salvar Backup Criptografado do Cofre",
            Filter = "Synapse Encrypted Backup (*.synapse-backup)|*.synapse-backup",
            FileName = $"Synapse-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.synapse-backup"
        };

        if (sfd.ShowDialog() != DialogResult.OK) return;

        // Solicita senha de criptografia
        var password = PromptForPassword("Digite a senha mestre para criptografar o backup:");
        if (string.IsNullOrWhiteSpace(password)) return;

        try
        {
            _btnExportBackup.Enabled = false;
            await Task.Run(async () => await VaultBackupExporter.ExportEncryptedBackupAsync(_vaultPath, sfd.FileName, password));
            MessageBox.Show("✅ Backup criptografado e verificado com sucesso!", "Backup Concluído", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar backup: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnExportBackup.Enabled = true;
        }
    }

    private static string PromptForPassword(string message)
    {
        using var form = new Form
        {
            Text = "Criptografia de Backup",
            Size = new Size(420, 190),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        SynapseTheme.ApplyFormChrome(form);

        var label = new Label { Text = message, ForeColor = SynapseTheme.TextSecondary, Font = SynapseTheme.FontBody(9f), Left = 20, Top = 18, Width = 370 };
        var textBox = new TextBox { Left = 20, Top = 46, Width = 370, UseSystemPasswordChar = true };
        SynapseTheme.StyleInput(textBox);
        var buttonOk = new SynapseButton { Text = "Criptografar", Variant = SynapseButtonVariant.Primary, Left = 260, Top = 95, Width = 130, Height = 34, DialogResult = DialogResult.OK };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(buttonOk);
        form.AcceptButton = buttonOk;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : "";
    }
}
