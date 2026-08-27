using System.Diagnostics;
using Synapse.Sync.Config;
using Synapse.Sync.Diagnostics;

namespace Synapse.Tray.Diagnostics;

/// <summary>
/// Janela de diagnóstico, visualização de logs e inspeção de conflitos (US-UX.4, RNF-2).
/// </summary>
public sealed class DiagnosticsForm : Form
{
    private readonly TabControl _tabControl;
    private readonly ListView _lstConflicts;
    private readonly RichTextBox _txtLogs;
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;
    private readonly SynapseConfigManager _configManager;
    private string _vaultPath = string.Empty;
    private string _logDir = string.Empty;

    public DiagnosticsForm(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        Text = "Synapse — Diagnóstico e Conflitos";
        Size = new Size(860, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(24, 24, 27)
        };

        var lblTitle = new Label
        {
            Text = "Diagnóstico & Histórico do Synapse",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 10),
            AutoSize = true
        };

        var lblSubtitle = new Label
        {
            Text = "Inspeção de conflitos preservados e visualização de logs em tempo real",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(20, 34),
            AutoSize = true
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);
        Controls.Add(pnlHeader);

        // Tab Control
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6)
        };

        // Tab 1: Conflitos
        var tabConflicts = new TabPage("Conflitos Preservados (_conflitos/)");
        tabConflicts.Padding = new Padding(12);

        var lblConflictInfo = new Label
        {
            Text = "Política de Zero Perda de Dados (RNF-2): Conflitos não resolvidos automaticamente são mantidos com segurança na pasta _conflitos/.",
            ForeColor = Color.FromArgb(113, 113, 122),
            Dock = DockStyle.Top,
            Height = 28
        };
        tabConflicts.Controls.Add(lblConflictInfo);

        _lstConflicts = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _lstConflicts.Columns.Add("Arquivo", 240);
        _lstConflicts.Columns.Add("Caminho Relativo", 320);
        _lstConflicts.Columns.Add("Modificado em", 140);
        _lstConflicts.Columns.Add("Tamanho", 90);
        tabConflicts.Controls.Add(_lstConflicts);

        var pnlConflictActions = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            Padding = new Padding(0, 8, 0, 0)
        };

        var btnResolveDiff = new Button
        {
            Text = "Resolver (3-Way Diff)...",
            Location = new Point(0, 8),
            Width = 170,
            Height = 30,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        btnResolveDiff.Click += (_, _) => OpenThreeWayDiffForSelectedConflict();

        var btnOpenConflictFile = new Button
        {
            Text = "Abrir Arquivo",
            Location = new Point(180, 8),
            Width = 110,
            Height = 30
        };
        btnOpenConflictFile.Click += (_, _) => OpenSelectedConflict();

        var btnOpenConflictFolder = new Button
        {
            Text = "Abrir Pasta _conflitos",
            Location = new Point(300, 8),
            Width = 160,
            Height = 30
        };
        btnOpenConflictFolder.Click += (_, _) => OpenConflictsDirectory();

        var btnRefreshConflicts = new Button
        {
            Text = "Atualizar Lista",
            Location = new Point(470, 8),
            Width = 120,
            Height = 30
        };
        btnRefreshConflicts.Click += async (_, _) => await RefreshConflictsAsync();

        pnlConflictActions.Controls.Add(btnResolveDiff);
        pnlConflictActions.Controls.Add(btnOpenConflictFile);
        pnlConflictActions.Controls.Add(btnOpenConflictFolder);
        pnlConflictActions.Controls.Add(btnRefreshConflicts);
        tabConflicts.Controls.Add(pnlConflictActions);

        _tabControl.TabPages.Add(tabConflicts);

        // Tab 2: Logs
        var tabLogs = new TabPage("Logs do Serviço (Serilog)");
        tabLogs.Padding = new Padding(12);

        _txtLogs = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(24, 24, 27),
            ForeColor = Color.FromArgb(228, 228, 231),
            Font = new Font("Consolas", 9.5f, FontStyle.Regular),
            WordWrap = false
        };
        tabLogs.Controls.Add(_txtLogs);

        var pnlLogActions = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            Padding = new Padding(0, 8, 0, 0)
        };

        var btnCopyLogs = new Button
        {
            Text = "Copiar Logs",
            Location = new Point(0, 8),
            Width = 110,
            Height = 30
        };
        btnCopyLogs.Click += (_, _) => CopyLogsToClipboard();

        var btnRefreshLogs = new Button
        {
            Text = "Atualizar Logs",
            Location = new Point(120, 8),
            Width = 120,
            Height = 30
        };
        btnRefreshLogs.Click += async (_, _) => await RefreshLogsAsync();

        var btnOpenLogFile = new Button
        {
            Text = "Abrir Arquivo de Log",
            Location = new Point(250, 8),
            Width = 150,
            Height = 30
        };
        btnOpenLogFile.Click += (_, _) => OpenCurrentLogFile();

        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        var chkAutoRefresh = new CheckBox
        {
            Text = "Auto-atualizar (3s)",
            Checked = true,
            Location = new Point(420, 13),
            AutoSize = true
        };
        chkAutoRefresh.CheckedChanged += (_, _) => _autoRefreshTimer.Enabled = chkAutoRefresh.Checked;

        pnlLogActions.Controls.Add(btnCopyLogs);
        pnlLogActions.Controls.Add(btnRefreshLogs);
        pnlLogActions.Controls.Add(btnOpenLogFile);
        pnlLogActions.Controls.Add(chkAutoRefresh);
        tabLogs.Controls.Add(pnlLogActions);

        _tabControl.TabPages.Add(tabLogs);

        Controls.Add(_tabControl);
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            if (_tabControl.SelectedTab == tabLogs)
            {
                await RefreshLogsAsync();
            }
        };

        Shown += async (_, _) =>
        {
            await InitializeAsync();
            _autoRefreshTimer.Start();
        };

        FormClosing += (_, _) => _autoRefreshTimer.Stop();
    }

    private async Task InitializeAsync()
    {
        var config = await _configManager.LoadAsync();
        _vaultPath = config.VaultPath;
        _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse", "logs");

        await RefreshConflictsAsync();
        await RefreshLogsAsync();
    }

    private async Task RefreshConflictsAsync()
    {
        _lstConflicts.Items.Clear();

        if (string.IsNullOrEmpty(_vaultPath)) return;

        var conflicts = await ConflictInspector.ListConflictsAsync(_vaultPath);

        foreach (var item in conflicts)
        {
            var lvi = new ListViewItem(item.FileName);
            lvi.SubItems.Add(item.RelativePath);
            lvi.SubItems.Add(item.ModifiedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            lvi.SubItems.Add(FormatFileSize(item.FileSizeBytes));
            lvi.Tag = item.FullPath;
            _lstConflicts.Items.Add(lvi);
        }

        if (conflicts.Count == 0)
        {
            var lvi = new ListViewItem("Nenhum conflito encontrado. Seu cofre está 100% sincronizado.");
            lvi.ForeColor = Color.DarkGray;
            _lstConflicts.Items.Add(lvi);
        }
    }

    private async Task RefreshLogsAsync()
    {
        var latestLog = LogReader.FindLatestLogFile(_logDir);
        if (latestLog == null)
        {
            _txtLogs.Text = "Nenhum arquivo de log encontrado em: " + _logDir;
            return;
        }

        var lines = await LogReader.ReadTailLinesAsync(latestLog, 80);
        var text = string.Join(Environment.NewLine, lines);

        if (_txtLogs.Text != text)
        {
            _txtLogs.Text = text;
            _txtLogs.SelectionStart = _txtLogs.Text.Length;
            _txtLogs.ScrollToCaret();
        }
    }

    private void OpenThreeWayDiffForSelectedConflict()
    {
        if (_lstConflicts.SelectedItems.Count == 0)
        {
            MessageBox.Show("Selecione um arquivo de conflito na lista para resolver.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var fullPath = _lstConflicts.SelectedItems[0].Tag as string;
        if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
        {
            using var diffForm = new ThreeWayDiffViewerForm(_vaultPath, fullPath);
            if (diffForm.ShowDialog() == DialogResult.OK)
            {
                _ = RefreshConflictsAsync();
            }
        }
    }

    private void OpenSelectedConflict()
    {
        if (_lstConflicts.SelectedItems.Count == 0) return;
        var fullPath = _lstConflicts.SelectedItems[0].Tag as string;
        if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
    }

    private void OpenConflictsDirectory()
    {
        if (string.IsNullOrEmpty(_vaultPath)) return;
        var dir = Path.Combine(_vaultPath, "_conflitos");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });
    }

    private void OpenCurrentLogFile()
    {
        var latestLog = LogReader.FindLatestLogFile(_logDir);
        if (latestLog != null && File.Exists(latestLog))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = latestLog,
                UseShellExecute = true
            });
        }
    }

    private void CopyLogsToClipboard()
    {
        if (!string.IsNullOrEmpty(_txtLogs.Text))
        {
            Clipboard.SetText(_txtLogs.Text);
            MessageBox.Show("Logs copiados para a área de transferência.", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
