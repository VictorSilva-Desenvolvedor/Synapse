using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Core.Logging;
using Synapse.Sync.Config;
using Synapse.Tray.UI;

namespace Synapse.Tray.QuickCapture;

/// <summary>
/// Captura Rapida em dois estados.
///
/// RECOLHIDA (padrao): uma barra de duas linhas que aparece sob o cursor. Enter salva,
/// Ctrl+Enter processa com IA. E o caminho rapido — nenhuma chamada de rede acontece
/// enquanto voce digita.
///
/// EXPANDIDA (a seta na esquerda): abre a bancada, que separa o que e FATO do que a IA
/// decide. Titulo, pasta e conexoes nao sao previsiveis localmente — quem os escolhe e
/// o provedor, dentro de NoteFileWriter.WriteStructuredNoteAsync — entao o painel nao
/// os adivinha: o botao Pre-visualizar chama a IA sem gravar nada e mostra os valores
/// reais. Confirmar depois grava a nota ja processada, sem uma segunda chamada.
/// </summary>
public partial class QuickCaptureWindow : PixelWindow
{
    private static readonly Regex TagPattern = new(@"#[\p{L}\p{N}_\-/]+", RegexOptions.Compiled);

    private readonly SynapseConfigManager _configManager;

    private bool _isExpanded;
    private AiStructuredNote? _previewed;
    private string _previewedFor = string.Empty;

    public QuickCaptureWindow(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        InitializeComponent();

        ProviderBox.Items.Add("Google Gemini (Free Tier)");
        ProviderBox.Items.Add("Ollama (Local Offline)");
        ProviderBox.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            RefreshLocalFacts();
        };
    }

    /// <summary>Texto digitado. Exposto para o harness de captura popular a tela.</summary>
    public string InputText
    {
        get => InputBox.Text;
        set
        {
            InputBox.Text = value;
            RefreshLocalFacts();
        }
    }

    /// <summary>Estado da bancada. Exposto para o harness capturar os dois.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { ToggleExpanded(); } }
    }

    // ---------------------------------------------------------------- estados

    private void OnToggleExpand(object sender, RoutedEventArgs e) => ToggleExpanded();

    private void ToggleExpanded()
    {
        _isExpanded = !_isExpanded;

        // Sem animacao de propósito: animar Width/Height produz valores fracionarios
        // durante a transicao, e a grade de 2px nao sobrevive a meio pixel.
        var left = Left;
        var top = Top;

        ExpandButton.Content = _isExpanded ? "▼" : "▶";

        // A faixa recua quando expandida: musgo cheio convida ao clique, musgo escuro
        // sai da frente do conteudo depois que o clique ja aconteceu.
        ExpandButton.Background = (System.Windows.Media.Brush)FindResource(
            _isExpanded ? "AccentPrimaryPressedBrush" : "AccentPrimaryBrush");

        PreviewPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        FooterStrip.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        HintStrip.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;

        if (_isExpanded)
        {
            SizeToContent = SizeToContent.Manual;
            InputBox.MinHeight = 320;
            Width = 940;
            Height = 560;
        }
        else
        {
            InputBox.MinHeight = 46;
            Width = 700;
            SizeToContent = SizeToContent.Height;
        }

        // A janela cresce para baixo e para a direita: a barra fica onde estava.
        if (!double.IsNaN(left))
        {
            Left = left;
            Top = top;
        }

        RefreshLocalFacts();
        InputBox.Focus();
    }

    // ---------------------------------------------------------------- teclado

    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.E)
        {
            e.Handled = true;
            ToggleExpanded();
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (shift)
        {
            return; // Shift+Enter cai no TextBox e insere a quebra de linha.
        }

        e.Handled = true;

        if (ctrl)
        {
            await ProcessWithAiAsync();
        }
        else
        {
            await SaveRawAsync();
        }
    }

    // ------------------------------------------------------- fatos locais

    private void OnInputChanged(object sender, RoutedEventArgs e) => RefreshLocalFacts();

    /// <summary>
    /// Recalcula so o que da para saber sem rede: destino do salvamento cru, tags
    /// digitadas e tamanho. Roda a cada tecla, entao nao pode custar nada.
    /// </summary>
    private void RefreshLocalFacts()
    {
        var text = InputBox.Text;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        SizeValue.Text = words == 1 ? "1 palavra" : $"{words} palavras";

        RawPathValue.Text = $"Inbox/Captura-{DateTime.Now:yyyyMMdd-HHmmss}.md";

        var tags = TagPattern.Matches(text)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TypedTags.ItemsSource = tags;

        // Uma previa deixa de valer assim que o texto muda.
        if (_previewed is not null && !string.Equals(text, _previewedFor, StringComparison.Ordinal))
        {
            ClearPreview();
        }
    }

    private void ClearPreview()
    {
        _previewed = null;
        _previewedFor = string.Empty;
        AiTitleValue.Text = "-";
        AiTitleValue.Foreground = (System.Windows.Media.Brush)FindResource("TextDisabledBrush");
        AiFolderValue.Text = "-";
        AiFolderValue.Foreground = (System.Windows.Media.Brush)FindResource("TextDisabledBrush");
        AiLinks.ItemsSource = null;
        AiEmptyHint.Visibility = Visibility.Visible;
        ProcessButton.Content = "PROCESSAR COM IA";
    }

    // ---------------------------------------------------------------- previa

    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("DIGITE ALGO PRIMEIRO.", "WarningBrush");
            return;
        }

        var config = await _configManager.LoadAsync();
        if (string.IsNullOrEmpty(config.VaultPath))
        {
            PixelMessageBox.Show("Cofre nao configurado. Conclua o Onboarding primeiro.", "AVISO", PixelMessageKind.Warning, this);
            return;
        }

        PreviewButton.IsEnabled = false;
        SetStatus("CONSULTANDO A IA...", "TextSecondaryBrush");

        try
        {
            var provider = BuildProvider(config);
            var existingNotes = NoteFileWriter.GetVaultNoteTitles(config.VaultPath);

            // ProcessRawNoteAsync estrutura a nota mas nao escreve nada em disco:
            // e exatamente a previa que a bancada precisa.
            var structured = await Task.Run(() => provider.ProcessRawNoteAsync(text, existingNotes));

            _previewed = structured;
            _previewedFor = InputBox.Text;

            var brainConfig = BuildBrainConfig(config);
            var folder = brainConfig.AutoCategorizeFolders && !string.IsNullOrWhiteSpace(structured.Category)
                ? $"{brainConfig.DefaultFolder}/{structured.Category}"
                : brainConfig.DefaultFolder;

            AiTitleValue.Text = structured.Title;
            AiTitleValue.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            AiFolderValue.Text = $"{folder}/{structured.Title}.md";
            AiFolderValue.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            AiLinks.ItemsSource = structured.SuggestedConnections;
            AiEmptyHint.Visibility = Visibility.Collapsed;

            ProcessButton.Content = "CONFIRMAR E SALVAR";
            SetStatus("PREVIA PRONTA, NADA GRAVADO.", "SuccessBrush");
        }
        catch (Exception ex)
        {
            SetStatus("FALHA NA PREVIA.", "ErrorBrush");
            PixelMessageBox.Show($"Nao foi possivel pre-visualizar: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
        finally
        {
            PreviewButton.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- gravar

    private async void OnProcessWithAi(object sender, RoutedEventArgs e) => await ProcessWithAiAsync();

    private async Task ProcessWithAiAsync()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("DIGITE ALGO PRIMEIRO.", "WarningBrush");
            return;
        }

        var config = await _configManager.LoadAsync();
        if (string.IsNullOrEmpty(config.VaultPath))
        {
            PixelMessageBox.Show("Cofre nao configurado. Conclua o Onboarding primeiro.", "AVISO", PixelMessageKind.Warning, this);
            return;
        }

        _ = SynapseActivityLogger.Instance.LogClickAsync("QuickCapture", "BtnProcessAi", $"Length: {text.Length}");
        SetBusy(true, "PROCESSANDO...");
        var sw = Stopwatch.StartNew();

        try
        {
            var brainConfig = BuildBrainConfig(config);
            string relativePath;

            // Se a previa ainda vale para este texto, grava o que ja foi estruturado —
            // confirmar nao paga uma segunda chamada de IA.
            if (_previewed is not null && string.Equals(InputBox.Text, _previewedFor, StringComparison.Ordinal))
            {
                var existingNotes = NoteFileWriter.GetVaultNoteTitles(config.VaultPath);
                relativePath = await NoteFileWriter.WriteStructuredNoteAsync(
                    _previewed, config.VaultPath, brainConfig, existingNotes);
            }
            else
            {
                var captureService = new SmartCaptureService(BuildProvider(config), brainConfig);
                relativePath = await captureService.ProcessAndSaveToVaultAsync(text, config.VaultPath);
            }

            sw.Stop();

            SynapseActivityLogger.Instance.SetVaultPath(config.VaultPath);
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "QuickCapture",
                "ProcessAndSaveToVault",
                $"Provider: {ProviderName()} | Input: \"{(text.Length > 60 ? text[..57] + "..." : text)}\"",
                "Success",
                sw.ElapsedMilliseconds,
                affectedPath: relativePath);

            SetBusy(false, $"SALVO EM: {relativePath}");
            PixelMessageBox.Show(
                $"Nota criada e conectada no cofre.\n\nArquivo: {relativePath}",
                "SYNAPSE BRAIN",
                PixelMessageKind.Success,
                this);

            Close();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "QuickCapture", "ProcessAndSaveToVault", $"Provider: {ProviderName()}",
                "Failed", sw.ElapsedMilliseconds, errorMessage: ex.Message);

            SetBusy(false, "ERRO AO PROCESSAR.");
            PixelMessageBox.Show($"Falha ao processar captura com IA: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
    }

    private async void OnSaveRaw(object sender, RoutedEventArgs e) => await SaveRawAsync();

    private async Task SaveRawAsync()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var config = await _configManager.LoadAsync();
        if (string.IsNullOrEmpty(config.VaultPath))
        {
            PixelMessageBox.Show("Cofre nao configurado. Conclua o Onboarding primeiro.", "AVISO", PixelMessageKind.Warning, this);
            return;
        }

        try
        {
            var targetDir = Path.Combine(config.VaultPath, "Inbox");
            Directory.CreateDirectory(targetDir);

            var title = $"Captura-{DateTime.Now:yyyyMMdd-HHmmss}";
            var path = Path.Combine(targetDir, $"{title}.md");
            await File.WriteAllTextAsync(path, $"# {title}\n\n{text}");

            SetStatus($"SALVO EM Inbox/{title}.md", "SuccessBrush");
            Close();
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Erro: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
    }

    // ---------------------------------------------------------------- apoio

    private bool IsGemini => ProviderBox.SelectedIndex == 0;

    private string ProviderName() => IsGemini ? "Gemini" : "Ollama";

    private BrainConfig BuildBrainConfig(SynapseConfig config) => new()
    {
        ProviderType = IsGemini ? AiProviderType.Gemini : AiProviderType.Ollama,
        GeminiApiKey = config.GeminiApiKey,
        GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.6-flash" : config.GeminiModel
    };

    private IBrainAiProvider BuildProvider(SynapseConfig config)
    {
        var brainConfig = BuildBrainConfig(config);
        return IsGemini ? new GeminiAiProvider(brainConfig) : new OllamaAiProvider(brainConfig);
    }

    private void SetBusy(bool busy, string message)
    {
        ProcessButton.IsEnabled = !busy;
        SaveRawButton.IsEnabled = !busy;
        PreviewButton.IsEnabled = !busy;
        InputBox.IsReadOnly = busy;
        SetStatus(message, busy ? "TextSecondaryBrush" : "SuccessBrush");
    }

    private void SetStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
        CollapsedStatus.Text = message.Length > 28 ? message[..25] + "..." : message;
        CollapsedStatus.Foreground = StatusText.Foreground;
    }
}
