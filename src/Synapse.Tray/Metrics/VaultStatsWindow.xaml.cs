using System.Windows;
using Microsoft.Win32;
using Synapse.Sync.Backup;
using Synapse.Sync.Config;
using Synapse.Sync.Metrics;
using Synapse.Tray.UI;

namespace Synapse.Tray.Metrics;

/// <summary>
/// Uma linha da distribuicao por categoria.
///
/// <paramref name="BarFraction"/> e 0..1 relativo a MAIOR categoria, nao ao total: a
/// barra existe para comparar categorias entre si, e normalizar pelo total deixaria
/// todas curtas num cofre com muitos temas.
/// </summary>
public sealed record CategoryRow(string Category, string Count, string Share, double BarFraction = 0)
{
    // Duas colunas em estrela dao a proporcao exata sem conversor e sem depender de
    // ActualWidth. O minimo evita GridLength(0), que colapsaria a celula.
    public GridLength BarFilled => new(Math.Clamp(BarFraction, 0.001, 1), GridUnitType.Star);

    public GridLength BarRest => new(Math.Clamp(1 - BarFraction, 0.001, 1), GridUnitType.Star);
}

/// <summary>
/// Painel de estatisticas, produtividade e exportacao de backup criptografado.
/// A distribuicao por categoria e desenhada como barra proporcional, nao como texto
/// de porcentagem: comparar "24,3%" com "20,9%" de cabeca e trabalho que a figura faz.
/// </summary>
public partial class VaultStatsWindow : PixelWindow
{
    private readonly SynapseConfigManager _configManager;
    private string _vaultPath = string.Empty;

    /// <summary>
    /// Ligado por SetSampleData, para o harness de captura.
    ///
    /// Precisa ser revalidado DEPOIS de cada await: o carregamento real comeca no
    /// Loaded, antes de o harness injetar o exemplo, entao checar so no topo do metodo
    /// deixa a continuacao sobrescrever a tela com os dados do cofre da maquina.
    /// A corrida e nao deterministica — ja "passou" uma vez por sorte.
    /// </summary>
    private bool _sampleMode;

    public VaultStatsWindow(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        InitializeComponent();

        Loaded += async (_, _) => await LoadMetricsAsync();
    }

    /// <summary>Popula a tela com dados fixos. Usado pelo harness de captura.</summary>
    public void SetSampleData(
        string notes,
        string words,
        string reading,
        string activity,
        IReadOnlyList<CategoryRow> categories)
    {
        _sampleMode = true;
        NotesValue.Text = notes;
        WordsValue.Text = words;
        ReadingValue.Text = reading;
        ActivityValue.Text = activity;
        CategoriesList.ItemsSource = categories;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadMetricsAsync();

    private async Task LoadMetricsAsync()
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

        if (string.IsNullOrEmpty(_vaultPath) || !Directory.Exists(_vaultPath))
        {
            NotesValue.Text = "0";
            WordsValue.Text = "0";
            ReadingValue.Text = "0 min";
            ActivityValue.Text = "0";
            CategoriesList.ItemsSource = Array.Empty<CategoryRow>();
            return;
        }

        var report = await Task.Run(() => VaultMetricsCollector.CollectMetricsAsync(_vaultPath));

        if (_sampleMode)
        {
            return;
        }

        NotesValue.Text = $"{report.TotalNotes:N0}";
        WordsValue.Text = $"{report.TotalWords:N0}";
        ReadingValue.Text = $"~{report.EstimatedReadingMinutes} min";
        ActivityValue.Text = $"{report.NotesCreatedLast7Days}";

        // A barra e normalizada pela maior categoria; a porcentagem continua sobre o total.
        var maxCount = report.CategoryCounts.Count > 0 ? report.CategoryCounts.Values.Max() : 0;

        CategoriesList.ItemsSource = report.CategoryCounts
            .OrderByDescending(c => c.Value)
            .Select(c => new CategoryRow(
                c.Key,
                c.Value.ToString(),
                report.TotalNotes > 0 ? $"{c.Value * 100.0 / report.TotalNotes:F1}%" : "0,0%",
                maxCount > 0 ? (double)c.Value / maxCount : 0))
            .ToList();
    }

    private async void OnExportBackup(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vaultPath) || !Directory.Exists(_vaultPath))
        {
            PixelMessageBox.Show("Cofre nao configurado.", "AVISO", PixelMessageKind.Warning, this);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Salvar Backup Criptografado do Cofre",
            Filter = "Synapse Encrypted Backup (*.synapse-backup)|*.synapse-backup",
            FileName = $"Synapse-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.synapse-backup"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var password = PixelPasswordPrompt.Ask(
            "Digite a senha mestre para criptografar o backup:",
            this);

        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        try
        {
            ExportButton.IsEnabled = false;
            await Task.Run(() => VaultBackupExporter.ExportEncryptedBackupAsync(_vaultPath, dialog.FileName, password));
            PixelMessageBox.Show("Backup criptografado e verificado com sucesso.", "BACKUP CONCLUIDO", PixelMessageKind.Success, this);
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Erro ao gerar backup: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }
}
