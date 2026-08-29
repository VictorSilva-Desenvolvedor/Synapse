using System.Diagnostics;
using Synapse.Sync.Config;
using Synapse.Sync.Diagnostics;
using Synapse.Tray.UI;

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
        Text = "Synapse — Diagnóstico e Conflitos [Pixel Edition]";
        Size = new Size(920, 640);
        StartPosition = FormStartPosition.CenterScreen;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            "► DIAGNÓSTICO & HISTÓRICO DO SYNAPSE",
            "Inspeção de conflitos preservados e visualização de logs em tempo real",
            70);

        // Tab Control
        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = SynapseTheme.Surface,
            Font = SynapseTheme.FontHeadline(8f)
        };

        // Tab 1: Conflitos
        var tabConflicts = new TabPage("► CONFLITOS PRESERVADOS");
        tabConflicts.Padding = new Padding(12);
        tabConflicts.BackColor = SynapseTheme.Background;

        var lblConflictInfo = new Label
        {
            Text = "● Zero Perda de Dados (RNF-2): conflitos são mantidos na pasta _conflitos/.",
            ForeColor = SynapseTheme.Warning,
            Font = SynapseTheme.FontCaption(8f),
            Dock = DockStyle.Top,
            Height = 26
        };
        tabConflicts.Controls.Add(lblConflictInfo);

        _lstConflicts = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false
        };
        SynapseTheme.StyleListView(_lstConflicts);
        _lstConflicts.Columns.Add("Arquivo", 240);
        _lstConflicts.Columns.Add("Caminho Relativo", 320);
        _lstConflicts.Columns.Add("Modificado em", 160);
        _lstConflicts.Columns.Add("Tamanho", 100);
        SynapseTheme.FillLastColumn(_lstConflicts, 100);
        tabConflicts.Controls.Add(_lstConflicts);

        var pnlConflictActions = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = SynapseTheme.Background
        };

        var btnResolveDiff = new SynapseButton
        {
            Text = "► Resolver (3-Way Diff)...",
            Location = new Point(0, 8),
            Width = 220,
            Height = 36,
            Variant = SynapseButtonVariant.Primary
        };
        btnResolveDiff.Click += (_, _) => OpenThreeWayDiffForSelectedConflict();

        var btnOpenConflictFile = new SynapseButton
        {
            Text = "Abrir Arquivo",
            Location = new Point(230, 8),
            Width = 140,
            Height = 36,
            Variant = SynapseButtonVariant.Secondary
        };
        btnOpenConflictFile.Click += (_, _) => OpenSelectedConflict();

        var btnOpenConflictFolder = new SynapseButton
        {
            Text = "Abrir Pasta",
            Location = new Point(380, 8),
            Width = 140,
            Height = 36,
            Variant = SynapseButtonVariant.Secondary
        };
        btnOpenConflictFolder.Click += (_, _) => OpenConflictsDirectory();

        var btnRefreshConflicts = new SynapseButton
        {
            Text = "Atualizar Lista",
            Location = new Point(530, 8),
            Width = 150,
            Height = 36,
            Variant = SynapseButtonVariant.Ghost
        };
        btnRefreshConflicts.Click += async (_, _) => await RefreshConflictsAsync();

        pnlConflictActions.Controls.Add(btnResolveDiff);
        pnlConflictActions.Controls.Add(btnOpenConflictFile);
        pnlConflictActions.Controls.Add(btnOpenConflictFolder);
        pnlConflictActions.Controls.Add(btnRefreshConflicts);
        tabConflicts.Controls.Add(pnlConflictActions);

        _tabControl.TabPages.Add(tabConflicts);

        // Tab 2: Logs
        var tabLogs = new TabPage("► LOGS DO SERVIÇO");
        tabLogs.Padding = new Padding(12);
        tabLogs.BackColor = SynapseTheme.Background;

        _txtLogs = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = SynapseTheme.SurfaceInput,
            ForeColor = SynapseTheme.TextPrimary,
            Font = SynapseTheme.FontMono(8.5f),
            BorderStyle = BorderStyle.FixedSingle
        };
        tabLogs.Controls.Add(_txtLogs);

        var pnlLogActions = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = SynapseTheme.Background
        };

        var btnCopyLogs = new SynapseButton
        {
            Text = "Copiar Logs",
            Location = new Point(0, 6),
            Width = 110,
            Height = 32,
            Variant = SynapseButtonVariant.Secondary
        };
        btnCopyLogs.Click += (_, _) => CopyLogsToClipboard();

        var btnRefreshLogs = new SynapseButton
        {
            Text = "Atualizar Logs",
            Location = new Point(120, 6),
            Width = 120,
            Height = 32,
            Variant = SynapseButtonVariant.Secondary
        };
        btnRefreshLogs.Click += async (_, _) => await RefreshLogsAsync();

        var btnOpenLogFile = new SynapseButton
        {
            Text = "Abrir Arquivo de Log",
            Location = new Point(250, 6),
            Width = 150,
            Height = 32,
            Variant = SynapseButtonVariant.Secondary
        };
        btnOpenLogFile.Click += (_, _) => OpenCurrentLogFile();

        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        var chkAutoRefresh = new CheckBox
        {
            Text = "Auto-atualizar (3s)",
            Checked = true,
            ForeColor = SynapseTheme.TextSecondary,
            Font = SynapseTheme.FontBody(9f),
            Location = new Point(420, 11),
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
        Controls.Add(pnlHeader);
        SynapseTheme.StyleTabControl(_tabControl);
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
            lvi.ForeColor = SynapseTheme.TextSecondary;
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
