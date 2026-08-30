using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Core.Logging;
using Synapse.Sync.Config;
using Synapse.Tray.UI;

namespace Synapse.Tray.Chat;

/// <summary>
/// Chat e Busca Semantica com o cofre do Obsidian (RAG).
///
/// As fontes citadas vivem dentro da bolha da resposta que as usou, como fichas
/// clicaveis. A tabela fixa que existia antes reservava 240px da janela mesmo vazia —
/// e ela ficava vazia na maior parte do tempo, porque so a ultima resposta tinha fontes.
/// </summary>
public partial class ChatVaultWindow : PixelWindow
{
    private readonly SynapseConfigManager _configManager;
    private readonly ObservableCollection<ChatMessage> _messages = [];
    private VaultRagEngine? _ragEngine;
    private RagAnswer? _lastAnswer;
    private string _vaultPath = string.Empty;
    private ChatMessage? _thinking;

    /// <summary>Ver a nota em VaultStatsWindow: evita a corrida do Loaded async void.</summary>
    private bool _sampleMode;

    public ChatVaultWindow(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        InitializeComponent();
        HistoryList.ItemsSource = _messages;

        Loaded += async (_, _) => await InitializeRagAsync();
    }

    /// <summary>Injeta uma conversa de exemplo. Usado pelo harness de captura.</summary>
    public void SetSampleConversation(IEnumerable<ChatMessage> messages, string? status = null)
    {
        _sampleMode = true;
        _messages.Clear();
        foreach (var message in messages)
        {
            _messages.Add(message);
        }

        HistoryEmptyState.Visibility = _messages.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (status is not null)
        {
            StatusText.Text = status;
        }

        // Mostra o fim da conversa, que e o que o usuario ve de fato.
        HistoryScroll.UpdateLayout();
        HistoryScroll.ScrollToEnd();
    }

    private void Append(ChatMessage message)
    {
        _messages.Add(message);
        HistoryEmptyState.Visibility = Visibility.Collapsed;
        HistoryScroll.ScrollToEnd();
    }

    private void ShowThinking()
    {
        _thinking = ChatMessage.Thinking();
        Append(_thinking);
    }

    private void ClearThinking()
    {
        if (_thinking is null)
        {
            return;
        }

        var index = _messages.IndexOf(_thinking);
        if (index >= 0)
        {
            _messages.RemoveAt(index);
        }

        _thinking = null;
    }

    private async Task InitializeRagAsync()
    {
        if (_sampleMode)
        {
            return;
        }

        var config = await _configManager.LoadAsync();

        // Revalida depois do await: o guard do topo roda antes de o harness injetar
        // o exemplo. Ver a nota em VaultStatsWindow.
        if (_sampleMode)
        {
            return;
        }

        _vaultPath = config.VaultPath;

        if (string.IsNullOrEmpty(_vaultPath) || !Directory.Exists(_vaultPath))
        {
            StatusText.Text = "Cofre nao configurado. Conclua o Onboarding primeiro.";
            return;
        }

        var brainConfig = new BrainConfig
        {
            GeminiApiKey = config.GeminiApiKey,
            GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.6-flash" : config.GeminiModel
        };

        _ragEngine = new VaultRagEngine(
            new GeminiEmbeddingProvider(brainConfig),
            new GeminiAiProvider(brainConfig),
            brainConfig);

        StatusText.Text = "Indexando notas do cofre...";
        await Task.Run(() => _ragEngine.IndexVaultAsync(_vaultPath));

        if (_sampleMode)
        {
            return;
        }

        SynapseActivityLogger.Instance.SetVaultPath(_vaultPath);
        _ = SynapseActivityLogger.Instance.LogActionAsync("ChatVault", "InitializeRag", $"VaultPath: {_vaultPath}");

        StatusText.Text = "Pronto para conversar com o seu Segundo Cerebro.";
        Append(ChatMessage.Assistant(
            "Ola! Sou o assistente do seu Segundo Cerebro. Pode me contar o que quiser guardar, "
            + "ou perguntar algo sobre o seu cofre."));
    }

    private async void OnSend(object sender, RoutedEventArgs e) => await SendQuestionAsync();

    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendQuestionAsync();
        }
    }

    private async Task SendQuestionAsync()
    {
        var question = InputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(question) || _ragEngine is null)
        {
            return;
        }

        InputBox.Text = string.Empty;
        SendButton.IsEnabled = false;
        SaveNoteButton.IsEnabled = false;
        StatusText.Text = "Processando mensagem...";

        Append(ChatMessage.User(question));
        ShowThinking();

        _ = SynapseActivityLogger.Instance.LogClickAsync("ChatVault", "BtnSend", $"Pergunta: \"{question}\"");
        var sw = Stopwatch.StartNew();

        try
        {
            var outcome = await Task.Run(() => _ragEngine.ProcessChatTurnAsync(question, _vaultPath));
            sw.Stop();
            ClearThinking();

            _ = SynapseActivityLogger.Instance.LogChatAsync(
                question,
                outcome.ReplyMessage,
                sw.ElapsedMilliseconds,
                "Success",
                outcome.Sources.Select(s => s.Title).ToList(),
                outcome.SavedNotePath);

            // As fontes entram NA mensagem: e ela que sera perguntada "de onde veio isso?".
            var sources = outcome.Sources
                .Select(s => new ChatSource(
                    s.Title,
                    $"{s.SimilarityScore * 100:F0}%",
                    Path.Combine(_vaultPath, s.RelativePath)))
                .ToList();

            Append(ChatMessage.Assistant(outcome.ReplyMessage, sources.Count > 0 ? sources : null));

            if (!string.IsNullOrWhiteSpace(outcome.SavedNotePath))
            {
                Append(ChatMessage.System(
                    $"Salvo em: [[{Path.GetFileNameWithoutExtension(outcome.SavedNotePath)}]] ({outcome.SavedNotePath})"));
            }

            if (outcome.Sources.Count > 0)
            {
                _lastAnswer = new RagAnswer(question, outcome.ReplyMessage, outcome.Sources);
                SaveNoteButton.IsEnabled = true;
            }
            else
            {
                _lastAnswer = null;
                SaveNoteButton.IsEnabled = false;
            }

            StatusText.Text = outcome.SavedNotePath is not null ? $"Nota salva em {outcome.SavedNotePath}" : "Pronto.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            ClearThinking();

            _ = SynapseActivityLogger.Instance.LogChatAsync(
                question, string.Empty, sw.ElapsedMilliseconds, "Failed", null, null, ex.Message);

            Append(ChatMessage.System($"Erro ao processar mensagem: {ex.Message}"));
            StatusText.Text = "Erro na conversa.";
        }
        finally
        {
            SendButton.IsEnabled = true;
            InputBox.Focus();
        }
    }

    private async void OnSaveNote(object sender, RoutedEventArgs e)
    {
        if (_lastAnswer is null || _ragEngine is null || string.IsNullOrWhiteSpace(_vaultPath))
        {
            return;
        }

        SaveNoteButton.IsEnabled = false;
        StatusText.Text = "Salvando resposta como nota no cofre...";
        _ = SynapseActivityLogger.Instance.LogClickAsync("ChatVault", "BtnSaveNote", $"Pergunta: \"{_lastAnswer.Question}\"");

        try
        {
            var sw = Stopwatch.StartNew();
            var relativePath = await Task.Run(() => _ragEngine.SaveAnswerAsNoteAsync(_lastAnswer, _vaultPath));
            sw.Stop();

            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "ChatVault", "SaveAnswerNote", $"Salvo em: {relativePath}", "Success", sw.ElapsedMilliseconds,
                affectedPath: relativePath);

            Append(ChatMessage.System($"Resposta salva no cofre: [[{relativePath}]]"));
            StatusText.Text = $"Nota salva em {relativePath}";
        }
        catch (Exception ex)
        {
            Append(ChatMessage.System($"Erro ao salvar nota no cofre: {ex.Message}"));
            StatusText.Text = "Erro ao salvar nota.";
        }
        finally
        {
            SaveNoteButton.IsEnabled = _lastAnswer is not null;
        }
    }

    /// <summary>Abre no editor padrao a nota da ficha clicada.</summary>
    private void OnSourceChip(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatSource source })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source.FullPath) || !File.Exists(source.FullPath))
        {
            StatusText.Text = $"Nota nao encontrada: {source.Title}";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = source.FullPath,
            UseShellExecute = true
        });
    }
}
