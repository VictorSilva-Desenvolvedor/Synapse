using System.Diagnostics;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Sync.Config;
using Synapse.Tray.UI;

namespace Synapse.Tray.Chat;

/// <summary>
/// Interface visual de Chat e Busca Semântica com o Cofre do Obsidian (RAG, V5.1).
/// </summary>
public sealed class ChatVaultForm : Form
{
    private readonly RichTextBox _txtHistory;
    private readonly TextBox _txtInput;
    private readonly SynapseButton _btnSend;
    private readonly SynapseButton _btnSaveNote;
    private readonly ListView _lstSources;
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
            "💬 Conversar com o Segundo Cérebro (RAG)",
            "Faça perguntas em linguagem natural sobre qualquer assunto anotado no seu cofre do Obsidian.",
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
            Padding = new Padding(16)
        };
        splitContainer.Panel1.Controls.Add(_txtHistory);
        splitContainer.Panel1.BackColor = SynapseTheme.Background;

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
            GridLines = false
        };
        SynapseTheme.StyleListView(_lstSources);
        _lstSources.Columns.Add("Nota Citada", 220);
        _lstSources.Columns.Add("Similaridade", 90);
        _lstSources.Columns.Add("Trecho", 520);
        _lstSources.DoubleClick += (_, _) => OpenSelectedSource();

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

        _txtInput = new TextBox
        {
            Location = new Point(16, 14),
            Width = 470,
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

        _btnSend = new SynapseButton
        {
            Text = "Perguntar ao Cofre",
            Location = new Point(496, 12),
            Width = 160,
            Height = 36,
            Variant = SynapseButtonVariant.Secondary
        };
        _btnSend.Click += async (_, _) => await SendQuestionAsync();

        _btnSaveNote = new SynapseButton
        {
            Text = "💾 Salvar como Nota",
            Location = new Point(666, 12),
            Width = 180,
            Height = 36,
            Variant = SynapseButtonVariant.Primary,
            Enabled = false
        };
        _btnSaveNote.Click += async (_, _) => await SaveAnswerAsNoteAsync();

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
            GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-flash-latest" : config.GeminiModel
        };

        var embeddingProvider = new GeminiEmbeddingProvider(brainConfig);
        var aiProvider = new GeminiAiProvider(brainConfig);

        _ragEngine = new VaultRagEngine(embeddingProvider, aiProvider);

        _lblStatus.Text = "Indexando notas do cofre...";
        await Task.Run(async () => await _ragEngine.IndexVaultAsync(_vaultPath));

        _lblStatus.Text = "Pronto para responder perguntas sobre o seu cofre.";
        AppendMessage("Synapse Brain", "Olá! Sou o assistente de inteligência do seu Segundo Cérebro. O que você gostaria de pesquisar ou relembrar hoje?");
    }

    private async Task SendQuestionAsync()
    {
        var question = _txtInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(question) || _ragEngine == null) return;

        _txtInput.Text = "";
        _btnSend.Enabled = false;
        _btnSaveNote.Enabled = false;
        _lblStatus.Text = "Pesquisando no cofre e gerando resposta...";

        AppendMessage("Você", question);

        try
        {
            var answer = await Task.Run(async () => await _ragEngine.AskVaultAsync(question, _vaultPath));

            _lastAnswer = answer;
            AppendMessage("Synapse Brain", answer.Answer);
            UpdateSourcesList(answer.Sources);
            _btnSaveNote.Enabled = true;
            _lblStatus.Text = "Pronto.";
        }
        catch (Exception ex)
        {
            AppendMessage("Sistema", $"Erro ao consultar cofre: {ex.Message}");
            _lblStatus.Text = "Erro na consulta.";
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

        try
        {
            var relativePath = await Task.Run(async () =>
                await _ragEngine.SaveAnswerAsNoteAsync(_lastAnswer, _vaultPath));

            AppendMessage("Sistema", $"💾 Resposta salva com sucesso no cofre: [[{relativePath}]]");
            _lblStatus.Text = $"Nota salva em {relativePath}";
        }
        catch (Exception ex)
        {
            AppendMessage("Sistema", $"⚠️ Erro ao salvar nota no cofre: {ex.Message}");
            _lblStatus.Text = "Erro ao salvar nota.";
        }
        finally
        {
            _btnSaveNote.Enabled = _lastAnswer != null;
        }
    }

    private void AppendMessage(string sender, string message)
    {
        _txtHistory.SelectionStart = _txtHistory.TextLength;
        _txtHistory.SelectionFont = SynapseTheme.FontBodyBold(10f);
        _txtHistory.SelectionColor = sender == "Você" ? SynapseTheme.AccentSecondary : SynapseTheme.AccentPrimary;
        _txtHistory.AppendText($"{sender} ({DateTime.Now:HH:mm}):\n");

        _txtHistory.SelectionFont = SynapseTheme.FontBody(10f);
        _txtHistory.SelectionColor = SynapseTheme.TextPrimary;
        _txtHistory.AppendText($"{message}\n\n");

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
