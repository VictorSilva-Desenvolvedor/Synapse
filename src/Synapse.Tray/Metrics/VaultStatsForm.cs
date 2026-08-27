using Synapse.Sync.Backup;
using Synapse.Sync.Config;
using Synapse.Sync.Metrics;

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
    private readonly Button _btnExportBackup;
    private string _vaultPath = string.Empty;

    public VaultStatsForm(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        Text = "Synapse — Estatísticas & Backup do Cofre";
        Size = new Size(740, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(248, 250, 252);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(16, 10, 16, 10)
        };

        var lblTitle = new Label
        {
            Text = "📊 Métricas de Produtividade & Saúde do Cofre",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(16, 8),
            AutoSize = true
        };

        var lblSubtitle = new Label
        {
            Text = "Acompanhe seu volume de escrita, distribuição temática e gere backups seguros.",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(16, 32),
            AutoSize = true
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);
        Controls.Add(pnlHeader);

        // Cards Panel
        var pnlCards = new Panel
        {
            Location = new Point(20, 80),
            Size = new Size(685, 120)
        };

        var card1 = CreateKpiCard("📝 Total de Notas", out _lblNotes, 0);
        var card2 = CreateKpiCard("✍️ Volume de Palavras", out _lblWords, 175);
        var card3 = CreateKpiCard("⏱️ Tempo de Leitura", out _lblReadingTime, 350);
        var card4 = CreateKpiCard("⚡ Atividade Recente", out _lblRecentActivity, 525);

        pnlCards.Controls.Add(card1);
        pnlCards.Controls.Add(card2);
        pnlCards.Controls.Add(card3);
        pnlCards.Controls.Add(card4);
        Controls.Add(pnlCards);

        // Categories Header
        var lblCatHeader = new Label
        {
            Text = "Distribuição por Categorias (PKM):",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Location = new Point(20, 215),
            AutoSize = true
        };
        Controls.Add(lblCatHeader);

        // Categories ListView
        _lstCategories = new ListView
        {
            Location = new Point(20, 240),
            Size = new Size(685, 200),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _lstCategories.Columns.Add("Categoria", 280);
        _lstCategories.Columns.Add("Quantidade de Notas", 160);
        _lstCategories.Columns.Add("Proporção", 200);
        Controls.Add(_lstCategories);

        // Footer Actions
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 65,
            BackColor = Color.FromArgb(241, 245, 249),
            Padding = new Padding(16, 12, 16, 12)
        };

        var btnRefresh = new Button
        {
            Text = "🔄 Atualizar Métricas",
            Location = new Point(20, 12),
            Size = new Size(160, 38),
            BackColor = Color.FromArgb(226, 232, 240),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        btnRefresh.Click += async (_, _) => await LoadMetricsAsync();

        _btnExportBackup = new Button
        {
            Text = "🔒 Exportar Backup Criptografado...",
            Location = new Point(460, 12),
            Size = new Size(245, 38),
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _btnExportBackup.Click += async (_, _) => await ExportBackupAsync();

        pnlFooter.Controls.Add(btnRefresh);
        pnlFooter.Controls.Add(_btnExportBackup);
        Controls.Add(pnlFooter);

        Shown += async (_, _) => await LoadMetricsAsync();
    }

    private static Panel CreateKpiCard(string title, out Label valueLabel, int left)
    {
        var panel = new Panel
        {
            Location = new Point(left, 0),
            Size = new Size(160, 110),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(10)
        };

        var lblT = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(10, 10),
            Size = new Size(140, 20)
        };

        valueLabel = new Label
        {
            Text = "...",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(10, 38),
            Size = new Size(140, 35)
        };

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
            Size = new Size(420, 180),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new Label { Text = message, Left = 20, Top = 15, Width = 360 };
        var textBox = new TextBox { Left = 20, Top = 45, Width = 360, UseSystemPasswordChar = true };
        var buttonOk = new Button { Text = "Criptografar", Left = 270, Top = 90, Width = 110, DialogResult = DialogResult.OK };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(buttonOk);
        form.AcceptButton = buttonOk;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : "";
    }
}
