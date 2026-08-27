using System.Diagnostics;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Sync.Config;

namespace Synapse.Tray.Chat;

/// <summary>
/// Interface visual de Chat e Busca Semântica com o Cofre do Obsidian (RAG, V5.1).
/// </summary>
public sealed class ChatVaultForm : Form
{
    private readonly RichTextBox _txtHistory;
    private readonly TextBox _txtInput;
    private readonly Button _btnSend;
    private readonly Button _btnSaveNote;
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
            Text = "💬 Conversar com o Segundo Cérebro (RAG)",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 10),
            AutoSize = true
        };

        var lblSubtitle = new Label
        {
            Text = "Faça perguntas em linguagem natural sobre qualquer assunto anotado no seu cofre do Obsidian.",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(20, 34),
            AutoSize = true
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);
        Controls.Add(pnlHeader);

        // Split Container (Top: Chat History, Bottom: Sources)
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 420
        };

        // Chat History
        _txtHistory = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(250, 250, 250),
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            Padding = new Padding(12)
        };
        splitContainer.Panel1.Controls.Add(_txtHistory);

        // Sources Panel
        var pnlSources = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var lblSourcesHeader = new Label
        {
            Text = "Notas de Referência Citadas (Duplo clique para abrir):",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 22
        };

        _lstSources = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };
        _lstSources.Columns.Add("Nota Citada", 220);
        _lstSources.Columns.Add("Similaridade", 90);
        _lstSources.Columns.Add("Trecho", 520);
        _lstSources.DoubleClick += (_, _) => OpenSelectedSource();

        pnlSources.Controls.Add(_lstSources);
        pnlSources.Controls.Add(lblSourcesHeader);
        splitContainer.Panel2.Controls.Add(pnlSources);

        Controls.Add(splitContainer);

        // Footer Input Panel
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = Color.FromArgb(244, 244, 245),
            Padding = new Padding(16, 12, 16, 12)
        };

        _txtInput = new TextBox
        {
            Location = new Point(16, 14),
            Width = 470,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular)
        };
        _txtInput.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendQuestionAsync();
            }
        };

        _btnSend = new Button
        {
            Text = "Perguntar ao Cofre",
            Location = new Point(496, 12),
            Width = 160,
            Height = 35,
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _btnSend.Click += async (_, _) => await SendQuestionAsync();

        _btnSaveNote = new Button
        {
            Text = "💾 Salvar como Nota",
            Location = new Point(666, 12),
            Width = 180,
            Height = 35,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Enabled = false
        };
        _btnSaveNote.Click += async (_, _) => await SaveAnswerAsNoteAsync();

        _lblStatus = new Label
        {
            Text = "Inicializando índice semântico...",
            ForeColor = Color.DimGray,
            Location = new Point(16, 48),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
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
            GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-1.5-flash" : config.GeminiModel
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
        _txtHistory.SelectionFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        _txtHistory.SelectionColor = sender == "Você" ? Color.FromArgb(37, 99, 235) : Color.FromArgb(16, 185, 129);
        _txtHistory.AppendText($"{sender} ({DateTime.Now:HH:mm}):\n");

        _txtHistory.SelectionFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        _txtHistory.SelectionColor = Color.FromArgb(39, 39, 42);
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
