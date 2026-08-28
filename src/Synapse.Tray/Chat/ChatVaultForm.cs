using System.Diagnostics;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Core.Logging;
using Synapse.Sync.Config;
using Synapse.Tray.UI;

namespace Synapse.Tray.Chat;

/// <summary>
/// Interface visual de Chat e Busca Semântica com o Cofre do Obsidian (RAG, V5.1).
/// </summary>
public sealed class ChatVaultForm : Form
{
    private readonly RichTextBox _txtHistory;
    private readonly Panel _pnlHistoryEmpty;
    private readonly TextBox _txtInput;
    private readonly SynapseButton _btnSend;
    private readonly SynapseButton _btnSaveNote;
    private readonly ListView _lstSources;
    private readonly Label _lblSourcesEmpty;
    private readonly Label _lblStatus;
    private readonly SynapseConfigManager _configManager;
    private VaultRagEngine? _ragEngine;
    private RagAnswer? _lastAnswer;
    private string _vaultPath = string.Empty;

    public ChatVaultForm(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        Text = "Synapse — Conversar com o Segundo Cérebro";
        Size = new Size(880, 680);
        StartPosition = FormStartPosition.CenterScreen;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            "Conversar com o Segundo Cérebro",
            "Guarde pensamentos, tarefas ou tire dúvidas sobre as suas anotações do cofre.",
            64);
        Controls.Add(pnlHeader);

        // Split Container (Top: Chat History, Bottom: Sources)
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 420,
            BackColor = SynapseTheme.Border
        };

        // Chat History
        _txtHistory = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = SynapseTheme.Background,
            ForeColor = SynapseTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = new Font(SynapseTheme.FontFamily, 10f, FontStyle.Regular),
            Padding = new Padding(20, 16, 20, 16)
        };
        splitContainer.Panel1.Controls.Add(_txtHistory);
        splitContainer.Panel1.BackColor = SynapseTheme.Background;

        // Estado vazio do histórico: some assim que a primeira mensagem é escrita
        // (evita a tela ficar com uma área enorme e sem graça antes da conversa começar).
        _pnlHistoryEmpty = new Panel { Dock = DockStyle.Fill, BackColor = SynapseTheme.Background };
        _pnlHistoryEmpty.Controls.Add(SynapseTheme.CreateEmptyState(
            "Pronto para conversar.\n\nConte algo que queira guardar ou pergunte sobre suas notas.",
            SynapseTheme.Background));
        splitContainer.Panel1.Controls.Add(_pnlHistoryEmpty);
        _pnlHistoryEmpty.BringToFront();

        // Sources Panel
        var pnlSources = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = SynapseTheme.SurfaceAlt };
        var lblSourcesHeader = new Label
        {
            Text = "Notas de Referência Citadas (duplo clique para abrir)",
            Font = SynapseTheme.FontBodyBold(9f),
            ForeColor = SynapseTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 24
        };

        _lstSources = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            Visible = false
        };
        SynapseTheme.StyleListView(_lstSources);
        _lstSources.Columns.Add("Nota Citada", 220);
        _lstSources.Columns.Add("Similaridade", 90);
        _lstSources.Columns.Add("Trecho", 520);
        _lstSources.DoubleClick += (_, _) => OpenSelectedSource();
        SynapseTheme.FillLastColumn(_lstSources, 200);

        _lblSourcesEmpty = SynapseTheme.CreateEmptyState(
            "Nenhuma fonte consultada ainda.\nAs notas usadas nas respostas do Segundo Cérebro aparecerão aqui.");

        pnlSources.Controls.Add(_lblSourcesEmpty);
        pnlSources.Controls.Add(_lstSources);
        pnlSources.Controls.Add(lblSourcesHeader);
        splitContainer.Panel2.Controls.Add(pnlSources);
        splitContainer.Panel2.BackColor = SynapseTheme.SurfaceAlt;

        Controls.Add(splitContainer);

        // Footer Input Panel
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            BackColor = SynapseTheme.SurfaceAlt,
            Padding = new Padding(16, 12, 16, 12)
        };

        _btnSaveNote = new SynapseButton
        {
            Text = "Salvar como Nota",
            Width = 180,
            Height = 36,
            Variant = SynapseButtonVariant.Primary,
            Enabled = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnSaveNote.Location = new Point(pnlFooter.Width - _btnSaveNote.Width - 16, 12);
        _btnSaveNote.Click += async (_, _) => await SaveAnswerAsNoteAsync();

        _btnSend = new SynapseButton
        {
            Text = "Enviar",
            Width = 120,
            Height = 36,
            Variant = SynapseButtonVariant.Secondary,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _btnSend.Location = new Point(_btnSaveNote.Left - _btnSend.Width - 10, 12);
        _btnSend.Click += async (_, _) => await SendQuestionAsync();

        _txtInput = new TextBox
        {
            Location = new Point(16, 14),
            Width = _btnSend.Left - 16 - 10,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(SynapseTheme.FontFamily, 10.5f, FontStyle.Regular)
        };
        SynapseTheme.StyleInput(_txtInput);
        _txtInput.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendQuestionAsync();
            }
        };

        _lblStatus = new Label
        {
            Text = "Inicializando índice semântico...",
            ForeColor = SynapseTheme.TextSecondary,
            Location = new Point(16, 50),
            AutoSize = true,
            Font = SynapseTheme.FontCaption()
        };

        pnlFooter.Controls.Add(_txtInput);
        pnlFooter.Controls.Add(_btnSend);
        pnlFooter.Controls.Add(_btnSaveNote);
        pnlFooter.Controls.Add(_lblStatus);
        Controls.Add(pnlFooter);

        Shown += async (_, _) => await InitializeRagAsync();
    }

    private async Task InitializeRagAsync()
    {
        var config = await _configManager.LoadAsync();
        _vaultPath = config.VaultPath;

        if (string.IsNullOrEmpty(_vaultPath) || !Directory.Exists(_vaultPath))
        {
            _lblStatus.Text = "Cofre não configurado. Conclua o Onboarding primeiro.";
            return;
        }

        var brainConfig = new BrainConfig
        {
            GeminiApiKey = config.GeminiApiKey,
            GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.6-flash" : config.GeminiModel
        };

        var embeddingProvider = new GeminiEmbeddingProvider(brainConfig);
        var aiProvider = new GeminiAiProvider(brainConfig);

        _ragEngine = new VaultRagEngine(embeddingProvider, aiProvider, brainConfig);

        _lblStatus.Text = "Indexando notas do cofre...";
        await Task.Run(async () => await _ragEngine.IndexVaultAsync(_vaultPath));

        SynapseActivityLogger.Instance.SetVaultPath(_vaultPath);
        _ = SynapseActivityLogger.Instance.LogActionAsync("ChatVault", "InitializeRag", $"VaultPath: {_vaultPath}");

        _lblStatus.Text = "Pronto para conversar com o seu Segundo Cérebro.";
        AppendMessage("Synapse Brain", "Olá! Sou o assistente de inteligência do seu Segundo Cérebro. Pronto. Pode me contar o que quiser guardar, ou perguntar algo sobre o seu cofre.");
    }

    private async Task SendQuestionAsync()
    {
        var question = _txtInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(question) || _ragEngine == null) return;

        _txtInput.Text = "";
        _btnSend.Enabled = false;
        _btnSaveNote.Enabled = false;
        _lblStatus.Text = "Processando mensagem...";

        AppendMessage("Você", question);

        _ = SynapseActivityLogger.Instance.LogClickAsync("ChatVault", "BtnSend", $"Pergunta: \"{question}\"");
        var sw = Stopwatch.StartNew();

        try
        {
            var outcome = await Task.Run(async () => await _ragEngine.ProcessChatTurnAsync(question, _vaultPath));
            sw.Stop();

            _ = SynapseActivityLogger.Instance.LogChatAsync(
                question,
                outcome.ReplyMessage,
                sw.ElapsedMilliseconds,
                "Success",
                outcome.Sources.Select(s => s.Title).ToList(),
                outcome.SavedNotePath);

            AppendMessage("Synapse Brain", outcome.ReplyMessage);

            if (!string.IsNullOrWhiteSpace(outcome.SavedNotePath))
            {
                AppendMessage("Sistema", $"Salvo em: [[{Path.GetFileNameWithoutExtension(outcome.SavedNotePath)}]] ({outcome.SavedNotePath})");
            }

            if (outcome.Sources.Count > 0)
            {
                UpdateSourcesList(outcome.Sources);
                _lastAnswer = new RagAnswer(question, outcome.ReplyMessage, outcome.Sources);
                _btnSaveNote.Enabled = true;
            }
            else
            {
                ClearSourcesList();
                _lastAnswer = null;
                _btnSaveNote.Enabled = false;
            }

            _lblStatus.Text = outcome.SavedNotePath != null ? $"Nota salva em {outcome.SavedNotePath}" : "Pronto.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = SynapseActivityLogger.Instance.LogChatAsync(
                question,
                string.Empty,
                sw.ElapsedMilliseconds,
                "Failed",
                null,
                null,
                ex.Message);

            AppendMessage("Sistema", $"Erro ao processar mensagem: {ex.Message}");
            _lblStatus.Text = "Erro na conversa.";
        }
        finally
        {
            _btnSend.Enabled = true;
            _txtInput.Focus();
        }
    }

    private async Task SaveAnswerAsNoteAsync()
    {
        if (_lastAnswer == null || _ragEngine == null || string.IsNullOrWhiteSpace(_vaultPath)) return;

        _btnSaveNote.Enabled = false;
        _lblStatus.Text = "Salvando resposta como nota no cofre...";
        _ = SynapseActivityLogger.Instance.LogClickAsync("ChatVault", "BtnSaveNote", $"Pergunta: \"{_lastAnswer.Question}\"");

        try
        {
            var sw = Stopwatch.StartNew();
            var relativePath = await Task.Run(async () =>
                await _ragEngine.SaveAnswerAsNoteAsync(_lastAnswer, _vaultPath));
            sw.Stop();

            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "ChatVault",
                "SaveAnswerNote",
                $"Salvo em: {relativePath}",
                "Success",
                sw.ElapsedMilliseconds,
                affectedPath: relativePath);

            AppendMessage("Sistema", $"Resposta salva com sucesso no cofre: [[{relativePath}]]");
            _lblStatus.Text = $"Nota salva em {relativePath}";
        }
        catch (Exception ex)
        {
            AppendMessage("Sistema", $"Erro ao salvar nota no cofre: {ex.Message}");
            _lblStatus.Text = "Erro ao salvar nota.";
        }
        finally
        {
            _btnSaveNote.Enabled = _lastAnswer != null;
        }
    }

    // Tons de fundo sutis para as "bolhas" de mensagem do RichTextBox — misturados a partir da
    // paleta do design system para não introduzir cores novas fora do tema Synapse Dark.
    private static readonly Color UserBubbleBackground = MixColor(SynapseTheme.Background, SynapseTheme.AccentSecondary, 0.22f);
    private static readonly Color AssistantBubbleBackground = SynapseTheme.Surface;

    private static Color MixColor(Color from, Color to, float amount)
    {
        int Lerp(int a, int b) => a + (int)((b - a) * amount);
        return Color.FromArgb(Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
    }

    private void AppendMessage(string sender, string message)
    {
        _pnlHistoryEmpty.Visible = false;

        var isUser = sender == "Você";
        var isSystem = sender == "Sistema";
        var accentColor = isUser ? SynapseTheme.AccentSecondary : isSystem ? SynapseTheme.TextSecondary : SynapseTheme.AccentPrimary;

        // Mensagens do usuário à direita, do assistente à esquerda e avisos de sistema
        // centralizados — para dar a sensação de conversa em vez de um log plano de texto.
        _txtHistory.SelectionAlignment = isUser ? HorizontalAlignment.Right : isSystem ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        var messageStart = _txtHistory.TextLength;

        _txtHistory.SelectionFont = SynapseTheme.FontBodyBold(9f);
        _txtHistory.SelectionColor = accentColor;
        _txtHistory.AppendText($"{sender} · {DateTime.Now:HH:mm}\n");

        _txtHistory.SelectionFont = isSystem ? SynapseTheme.FontCaptionItalic(9.5f) : SynapseTheme.FontBody(10f);
        _txtHistory.SelectionColor = isSystem ? SynapseTheme.TextSecondary : SynapseTheme.TextPrimary;
        _txtHistory.AppendText($"{message}\n");

        if (!isSystem)
        {
            // Pinta o fundo do bloco recém-escrito com um tom sutil, dando um efeito de
            // "bolha de chat" alinhada à direita (usuário) ou à esquerda (assistente).
            var messageEnd = _txtHistory.TextLength;
            _txtHistory.Select(messageStart, messageEnd - messageStart);
            _txtHistory.SelectionBackColor = isUser ? UserBubbleBackground : AssistantBubbleBackground;
        }

        // Volta a formatação padrão antes de escrever o espaçamento em branco entre mensagens,
        // senão o RichTextBox propaga o fundo colorido da bolha para a linha vazia seguinte.
        _txtHistory.Select(_txtHistory.TextLength, 0);
        _txtHistory.SelectionBackColor = _txtHistory.BackColor;
        _txtHistory.SelectionColor = SynapseTheme.TextPrimary;
        _txtHistory.SelectionFont = SynapseTheme.FontBody(10f);
        _txtHistory.AppendText("\n");

        // RichTextBox.AppendText/Select deixam o trecho recém-inserido "selecionado" internamente,
        // o que renderiza como um destaque azul permanente sobre o texto — precisa colapsar
        // a seleção de volta para o fim antes de repintar.
        _txtHistory.Select(_txtHistory.TextLength, 0);
        _txtHistory.ScrollToCaret();
    }

    private void UpdateSourcesList(IReadOnlyList<SemanticSearchResult> sources)
    {
        _lstSources.Items.Clear();
        foreach (var src in sources)
        {
            var lvi = new ListViewItem(src.Title);
            lvi.SubItems.Add($"{src.SimilarityScore * 100:F0}%");
            lvi.SubItems.Add(src.Excerpt.Length > 120 ? src.Excerpt[..120] + "..." : src.Excerpt);
            lvi.Tag = Path.Combine(_vaultPath, src.RelativePath);
            _lstSources.Items.Add(lvi);
        }

        _lblSourcesEmpty.Visible = _lstSources.Items.Count == 0;
        _lstSources.Visible = _lstSources.Items.Count > 0;
    }

    private void ClearSourcesList()
    {
        _lstSources.Items.Clear();
        _lstSources.Visible = false;
        _lblSourcesEmpty.Visible = true;
    }

    private void OpenSelectedSource()
    {
        if (_lstSources.SelectedItems.Count == 0) return;
        var fullPath = _lstSources.SelectedItems[0].Tag as string;
        if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
        }
    }
}
