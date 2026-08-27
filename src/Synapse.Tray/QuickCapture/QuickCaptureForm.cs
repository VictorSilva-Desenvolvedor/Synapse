using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Sync.Config;

namespace Synapse.Tray.QuickCapture;

/// <summary>
/// Janela flutuante de Captura Rápida Inteligente para o Segundo Cérebro (Synapse.Brain).
/// </summary>
public sealed class QuickCaptureForm : Form
{
    private readonly RichTextBox _txtInput;
    private readonly ComboBox _cmbProvider;
    private readonly Label _lblStatus;
    private readonly Button _btnProcessAi;
    private readonly Button _btnSaveRaw;
    private readonly SynapseConfigManager _configManager;

    public QuickCaptureForm(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        Text = "Synapse — Captura Rápida (Segundo Cérebro)";
        Size = new Size(680, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(24, 24, 27)
        };

        var lblTitle = new Label
        {
            Text = "🧠 Captura Inteligente para o Segundo Cérebro",
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(16, 10),
            AutoSize = true
        };

        var lblSubtitle = new Label
        {
            Text = "Digite uma ideia, link ou anotação. A IA estrutura, adiciona tags e conecta com seu cofre.",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(16, 34),
            AutoSize = true
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);
        Controls.Add(pnlHeader);

        // Body Input Panel
        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12)
        };

        _txtInput = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            BackColor = Color.FromArgb(250, 250, 250)
        };
        pnlBody.Controls.Add(_txtInput);
        Controls.Add(pnlBody);

        // Footer Actions Panel
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 85,
            BackColor = Color.FromArgb(244, 244, 245),
            Padding = new Padding(16, 10, 16, 10)
        };

        var lblProvider = new Label
        {
            Text = "Provedor IA:",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        _cmbProvider = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, 10),
            Width = 190
        };
        _cmbProvider.Items.Add("Google Gemini (Free Tier)");
        _cmbProvider.Items.Add("Ollama (Local Offline)");
        _cmbProvider.SelectedIndex = 0;

        _lblStatus = new Label
        {
            Text = "Pronto.",
            ForeColor = Color.FromArgb(113, 113, 122),
            Location = new Point(16, 50),
            AutoSize = true
        };

        _btnSaveRaw = new Button
        {
            Text = "Salvar sem IA",
            Location = new Point(370, 10),
            Width = 110,
            Height = 35
        };
        _btnSaveRaw.Click += async (_, _) => await SaveRawNoteAsync();

        _btnProcessAi = new Button
        {
            Text = "Processar com Gemini & Salvar",
            Location = new Point(490, 10),
            Width = 180,
            Height = 35,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        _btnProcessAi.Click += async (_, _) => await ProcessWithAiAndSaveAsync();

        pnlFooter.Controls.Add(lblProvider);
        pnlFooter.Controls.Add(_cmbProvider);
        pnlFooter.Controls.Add(_lblStatus);
        pnlFooter.Controls.Add(_btnSaveRaw);
        pnlFooter.Controls.Add(_btnProcessAi);
        Controls.Add(pnlFooter);
    }

    private async Task ProcessWithAiAndSaveAsync()
    {
        var text = _txtInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("Digite algum conteúdo para capturar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var config = await _configManager.LoadAsync();
        if (string.IsNullOrEmpty(config.VaultPath))
        {
            MessageBox.Show("Cofre não configurado. Conclua o Onboarding primeiro.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetProcessingState(true, "Processando com a API do Gemini e conectando ao cofre...");

        try
        {
            var isGemini = _cmbProvider.SelectedIndex == 0;
            var brainConfig = new BrainConfig
            {
                ProviderType = isGemini ? AiProviderType.Gemini : AiProviderType.Ollama,
                GeminiApiKey = config.GeminiApiKey,
                GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-1.5-flash" : config.GeminiModel
            };

            IBrainAiProvider provider = isGemini
                ? new GeminiAiProvider(brainConfig)
                : new OllamaAiProvider(brainConfig);

            var captureService = new SmartCaptureService(provider, brainConfig);
            var relativePath = await captureService.ProcessAndSaveToVaultAsync(text, config.VaultPath);

            SetProcessingState(false, $"Salvo com sucesso em: {relativePath}");
            MessageBox.Show($"Nota criada e conectada com sucesso no cofre com a API do Gemini!\n\nArquivo: {relativePath}", "Synapse Brain", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetProcessingState(false, "Erro ao processar.");
            MessageBox.Show($"Falha ao processar captura com IA: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveRawNoteAsync()
    {
        var text = _txtInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var config = await _configManager.LoadAsync();
        if (string.IsNullOrEmpty(config.VaultPath)) return;

        try
        {
            var targetDir = Path.Combine(config.VaultPath, "Inbox");
            Directory.CreateDirectory(targetDir);

            var title = $"Captura-{DateTime.Now:yyyyMMdd-HHmmss}";
            var path = Path.Combine(targetDir, $"{title}.md");
            await File.WriteAllTextAsync(path, $"# {title}\n\n{text}");

            MessageBox.Show($"Salvo em Inbox/{title}.md", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetProcessingState(bool isProcessing, string message)
    {
        _btnProcessAi.Enabled = !isProcessing;
        _btnSaveRaw.Enabled = !isProcessing;
        _txtInput.ReadOnly = isProcessing;
        _lblStatus.Text = message;
    }
}
