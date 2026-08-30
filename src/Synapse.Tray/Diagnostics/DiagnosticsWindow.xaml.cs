using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Synapse.Sync.Config;
using Synapse.Sync.Diagnostics;
using Synapse.Tray.UI;

namespace Synapse.Tray.Diagnostics;

/// <summary>Uma linha da lista de conflitos preservados.</summary>
public sealed record ConflictRow(string FileName, string RelativePath, string ModifiedAt, string Size, string FullPath)
{
    /// <summary>
    /// So a pasta, sem repetir o nome do arquivo que ja aparece acima no cartao.
    /// A redundancia estourava a largura e cortava a data.
    /// </summary>
    public string Folder
    {
        get
        {
            var dir = Path.GetDirectoryName(RelativePath)?.Replace('\\', '/');
            return string.IsNullOrEmpty(dir) ? "raiz do cofre" : $"{dir}/";
        }
    }
}

/// <summary>
/// Diagnostico numa tela so.
///
/// As abas sairam: um aviso no topo responde "esta tudo bem?" sem exigir clique, e o log
/// ocupa todo o resto. Antes era preciso escolher uma aba so para descobrir se havia
/// problema — e a aba de conflitos ficava vazia na maior parte do tempo.
/// </summary>
public partial class DiagnosticsWindow : PixelWindow
{
    private readonly SynapseConfigManager _configManager;
    private readonly DispatcherTimer _autoRefreshTimer;
    private IReadOnlyList<ConflictRow> _conflicts = [];
    private string _vaultPath = string.Empty;
    private string _logDir = string.Empty;
    private string _rawLog = string.Empty;

    /// <summary>Ver a nota em VaultStatsWindow: revalidado depois de cada await.</summary>
    private bool _sampleMode;

    public DiagnosticsWindow(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        InitializeComponent();

        _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshLogsAsync();

        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => _autoRefreshTimer.Stop();
    }

    /// <summary>Popula a tela com dados fixos. Usado pelo harness de captura.</summary>
    public void SetSampleData(IReadOnlyList<ConflictRow> conflicts, string logs)
    {
        _sampleMode = true;
        _conflicts = conflicts;
        _rawLog = logs;

        ApplyConflictState();
        ApplyLog(logs);
    }

    private async Task InitializeAsync()
    {
        if (_sampleMode)
        {
            return;
        }

        var config = await _configManager.LoadAsync();

        if (_sampleMode)
        {
            return;
        }

        _vaultPath = config.VaultPath;
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Synapse",
            "logs");

        await RefreshConflictsAsync();

        if (_sampleMode)
        {
            return;
        }

        await RefreshLogsAsync();
    }

    // -------------------------------------------------------------- conflitos

    private async Task RefreshConflictsAsync()
    {
        if (string.IsNullOrEmpty(_vaultPath))
        {
            _conflicts = [];
            ApplyConflictState();
            return;
        }

        var found = await ConflictInspector.ListConflictsAsync(_vaultPath);

        if (_sampleMode)
        {
            return;
        }

        _conflicts = found
            .Select(c => new ConflictRow(
                c.FileName,
                c.RelativePath,
                c.ModifiedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                FormatFileSize(c.FileSizeBytes),
                c.FullPath))
            .ToList();

        ApplyConflictState();
    }

    /// <summary>Pinta o aviso do topo conforme haja ou nao conflito pendente.</summary>
    private void ApplyConflictState()
    {
        if (_conflicts.Count == 0)
        {
            StateStripe.Fill = (Brush)FindResource("SuccessBrush");
            StateText.Foreground = (Brush)FindResource("SuccessBrush");
            StateText.Text = "TUDO SINCRONIZADO";
            StateDetail.Text = "Nenhum conflito pendente.";
            ResolveButton.Visibility = Visibility.Collapsed;
            return;
        }

        StateStripe.Fill = (Brush)FindResource("WarningBrush");
        StateText.Foreground = (Brush)FindResource("WarningBrush");
        StateText.Text = _conflicts.Count == 1 ? "1 CONFLITO" : $"{_conflicts.Count} CONFLITOS";
        StateDetail.Text = "Preservados em _conflitos/ - nenhum dado perdido.";
        ResolveButton.Visibility = Visibility.Visible;
    }

    private async void OnResolveConflicts(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0)
        {
            return;
        }

        var picker = new ConflictPickerWindow(_vaultPath, _conflicts) { Owner = this };
        picker.ShowDialog();

        if (picker.ResolvedAny)
        {
            await RefreshConflictsAsync();
        }
    }

    // ------------------------------------------------------------------- log

    private async Task RefreshLogsAsync()
    {
        var latestLog = LogReader.FindLatestLogFile(_logDir);
        if (latestLog is null)
        {
            ApplyLog($"Nenhum arquivo de log encontrado em: {_logDir}");
            return;
        }

        var lines = await LogReader.ReadTailLinesAsync(latestLog, 80);

        if (_sampleMode)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, lines);
        if (_rawLog == text)
        {
            return;
        }

        _rawLog = text;
        ApplyLog(text);
    }

    private void ApplyLog(string text)
    {
        _rawLog = text;
        LogList.ItemsSource = LogLine.ParseAll(text.Split(Environment.NewLine, StringSplitOptions.None));
        LogScroll.ScrollToEnd();
    }

    private void OnAutoRefreshChanged(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshCheck.IsChecked == true)
        {
            _autoRefreshTimer.Start();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    private void OnOpenLogFile(object sender, RoutedEventArgs e)
    {
        var latestLog = LogReader.FindLatestLogFile(_logDir);
        if (latestLog is not null && File.Exists(latestLog))
        {
            OpenWithShell(latestLog);
        }
    }

    private void OnCopyLogs(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_rawLog))
        {
            return;
        }

        Clipboard.SetText(_rawLog);
        PixelMessageBox.Show("Logs copiados para a area de transferencia.", "SYNAPSE", PixelMessageKind.Success, this);
    }

    private void OnOpenConflictFolder(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vaultPath))
        {
            return;
        }

        var dir = Path.Combine(_vaultPath, "_conflitos");
        Directory.CreateDirectory(dir);
        OpenWithShell(dir);
    }

    private static void OpenWithShell(string path)
        => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}
