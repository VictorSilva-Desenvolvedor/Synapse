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

        Text = "Synapse — Estatísticas & Backup do Cofre";
        Size = new Size(740, 580);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            "📊 Métricas de Produtividade & Saúde do Cofre",
            "Acompanhe seu volume de escrita, distribuição temática e gere backups seguros.",
            64);
        Controls.Add(pnlHeader);

        // Cards Panel
        var pnlCards = new Panel
        {
            Location = new Point(20, 84),
            Size = new Size(685, 120),
            BackColor = SynapseTheme.Background
        };

        var card1 = CreateKpiCard("📝 Total de Notas", out _lblNotes, 0, SynapseTheme.AccentPrimary);
        var card2 = CreateKpiCard("✍️ Volume de Palavras", out _lblWords, 172, SynapseTheme.AccentSecondary);
        var card3 = CreateKpiCard("⏱️ Tempo de Leitura", out _lblReadingTime, 344, SynapseTheme.Warning);
        var card4 = CreateKpiCard("⚡ Atividade Recente", out _lblRecentActivity, 516, SynapseTheme.AccentPrimary);

        pnlCards.Controls.Add(card1);
        pnlCards.Controls.Add(card2);
        pnlCards.Controls.Add(card3);
        pnlCards.Controls.Add(card4);
        Controls.Add(pnlCards);

        // Categories Header
        var lblCatHeader = new Label
        {
            Text = "Distribuição por Categorias (PKM)",
            Font = SynapseTheme.FontHeadline(10.5f),
            ForeColor = SynapseTheme.TextPrimary,
            Location = new Point(20, 220),
            AutoSize = true
        };
        Controls.Add(lblCatHeader);

        // Categories ListView
        _lstCategories = new ListView
        {
            Location = new Point(20, 248),
            Size = new Size(685, 220),
            View = View.Details,
            FullRowSelect = true,
            GridLines = false
        };
        SynapseTheme.StyleListView(_lstCategories);
        _lstCategories.Columns.Add("Categoria", 280);
        _lstCategories.Columns.Add("Quantidade de Notas", 160);
        _lstCategories.Columns.Add("Proporção", 200);
        Controls.Add(_lstCategories);

        // Footer Actions
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            BackColor = SynapseTheme.SurfaceAlt,
            Padding = new Padding(16, 12, 16, 12)
        };

        var btnRefresh = new SynapseButton
        {
            Text = "🔄 Atualizar Métricas",
            Location = new Point(20, 12),
            Size = new Size(160, 38),
            Variant = SynapseButtonVariant.Secondary
        };
        btnRefresh.Click += async (_, _) => await LoadMetricsAsync();

        _btnExportBackup = new SynapseButton
        {
            Text = "🔒 Exportar Backup Criptografado...",
            Location = new Point(460, 12),
            Size = new Size(245, 38),
            Variant = SynapseButtonVariant.Primary
        };
        _btnExportBackup.Click += async (_, _) => await ExportBackupAsync();

        pnlFooter.Controls.Add(btnRefresh);
        pnlFooter.Controls.Add(_btnExportBackup);
        Controls.Add(pnlFooter);

        Shown += async (_, _) => await LoadMetricsAsync();
    }

    private static Panel CreateKpiCard(string title, out Label valueLabel, int left, Color accentColor)
    {
        var panel = new RoundedPanel
        {
            Location = new Point(left, 0),
            Size = new Size(160, 112),
            BackColor = SynapseTheme.Surface,
            BorderColor = SynapseTheme.Border,
            Radius = SynapseTheme.RadiusMedium,
            Padding = new Padding(12)
        };

        var accentBar = new Panel { Location = new Point(0, 0), Size = new Size(36, 3), BackColor = accentColor };

        var lblT = new Label
        {
            Text = title,
            Font = SynapseTheme.FontBodyBold(8.5f),
            ForeColor = SynapseTheme.TextSecondary,
            Location = new Point(12, 14),
            Size = new Size(136, 32)
        };

        valueLabel = new Label
        {
            Text = "...",
            Font = SynapseTheme.FontDisplay(15f),
            ForeColor = SynapseTheme.TextPrimary,
            Location = new Point(12, 62),
            Size = new Size(136, 35)
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
