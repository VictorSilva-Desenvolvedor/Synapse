using System.Diagnostics;
using System.Drawing.Drawing2D;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Core.Logging;
using Synapse.Sync.Config;
using Synapse.Tray.UI;

namespace Synapse.Tray.QuickCapture;

/// <summary>
/// Janela flutuante de Captura Rápida Inteligente para o Segundo Cérebro (Synapse.Brain).
/// </summary>
public sealed class QuickCaptureForm : Form
{
    private readonly RichTextBox _txtInput;
    private readonly ComboBox _cmbProvider;
    private readonly Label _lblStatus;
    private readonly SynapseButton _btnProcessAi;
    private readonly SynapseButton _btnSaveRaw;
    private readonly SynapseConfigManager _configManager;

    public QuickCaptureForm(SynapseConfigManager? configManager = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();

        Text = "Synapse — Captura Rápida (Segundo Cérebro) [Pixel Edition]";
        Size = new Size(720, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            "► CAPTURA INTELIGENTE (BRAIN)",
            "Digite uma ideia, link ou anotação. A IA estrutura, adiciona tags e conecta com seu cofre.",
            70);

        // Body Input Panel
        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 14),
            BackColor = SynapseTheme.Background
        };

        var cardInput = SynapseTheme.CreateCard();
        cardInput.Dock = DockStyle.Fill;
        cardInput.Padding = new Padding(8);

        _txtInput = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = SynapseTheme.FontBody(9f),
            BackColor = SynapseTheme.SurfaceInput,
            ForeColor = SynapseTheme.TextPrimary,
            BorderStyle = BorderStyle.None
        };
        cardInput.Controls.Add(_txtInput);
        pnlBody.Controls.Add(cardInput);

        // Footer Actions Panel
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 90,
            BackColor = SynapseTheme.Surface,
            Padding = new Padding(16, 12, 16, 12)
        };
        pnlFooter.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var penDark = new Pen(SynapseTheme.Border, 2);
            e.Graphics.DrawLine(penDark, 0, 0, pnlFooter.Width, 0);
            using var penLight = new Pen(SynapseTheme.BorderLight, 1);
            e.Graphics.DrawLine(penLight, 0, 1, pnlFooter.Width, 1);
        };

        var lblProvider = new Label
        {
            Text = "Provedor IA:",
            Location = new Point(16, 16),
            AutoSize = true,
            ForeColor = SynapseTheme.TextSecondary,
            Font = SynapseTheme.FontHeadline(8f)
        };

        _cmbProvider = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(130, 12),
            Width = 200,
            FlatStyle = FlatStyle.Flat,
            BackColor = SynapseTheme.SurfaceInput,
            ForeColor = SynapseTheme.TextPrimary,
            Font = SynapseTheme.FontBody(8.5f)
        };
        _cmbProvider.Items.Add("Google Gemini (Free Tier)");
        _cmbProvider.Items.Add("Ollama (Local Offline)");
        _cmbProvider.SelectedIndex = 0;

        _lblStatus = new Label
        {
            Text = "● Pronto para capturar.",
            ForeColor = SynapseTheme.NeonGreen,
            Font = SynapseTheme.FontCaption(8f),
            Location = new Point(16, 54),
            AutoSize = true
        };

        _btnSaveRaw = new SynapseButton
        {
            Text = "Salvar sem IA",
            Location = new Point(345, 12),
            Width = 140,
            Height = 36,
            Variant = SynapseButtonVariant.Secondary
        };
        _btnSaveRaw.Click += async (_, _) => await SaveRawNoteAsync();

        _btnProcessAi = new SynapseButton
        {
            Text = "Processar com IA && Salvar",
            Location = new Point(495, 12),
            Width = 195,
            Height = 36,
            Variant = SynapseButtonVariant.Primary
        };
        _btnProcessAi.Click += async (_, _) => await ProcessWithAiAndSaveAsync();

        pnlFooter.Controls.Add(lblProvider);
        pnlFooter.Controls.Add(_cmbProvider);
        pnlFooter.Controls.Add(_lblStatus);
        pnlFooter.Controls.Add(_btnSaveRaw);
        pnlFooter.Controls.Add(_btnProcessAi);

        Controls.Add(pnlBody);
        Controls.Add(pnlHeader);
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

        _ = SynapseActivityLogger.Instance.LogClickAsync("QuickCapture", "BtnProcessAi", $"Length: {text.Length}");
        SetProcessingState(true, "Processando com a API do Gemini e conectando ao cofre...");
        var sw = Stopwatch.StartNew();

        try
        {
            var isGemini = _cmbProvider.SelectedIndex == 0;
            var brainConfig = new BrainConfig
            {
                ProviderType = isGemini ? AiProviderType.Gemini : AiProviderType.Ollama,
                GeminiApiKey = config.GeminiApiKey,
                GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.6-flash" : config.GeminiModel
            };

            IBrainAiProvider provider = isGemini
                ? new GeminiAiProvider(brainConfig)
                : new OllamaAiProvider(brainConfig);

            var captureService = new SmartCaptureService(provider, brainConfig);
            var relativePath = await captureService.ProcessAndSaveToVaultAsync(text, config.VaultPath);
            sw.Stop();

            SynapseActivityLogger.Instance.SetVaultPath(config.VaultPath);
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "QuickCapture",
                "ProcessAndSaveToVault",
                $"Provider: {(isGemini ? "Gemini" : "Ollama")} | Input: \"{(text.Length > 60 ? text[..57] + "..." : text)}\"",
                "Success",
                sw.ElapsedMilliseconds,
                affectedPath: relativePath);

            SetProcessingState(false, $"Salvo com sucesso em: {relativePath}");
            MessageBox.Show($"Nota criada e conectada com sucesso no cofre com a API do Gemini!\n\nArquivo: {relativePath}", "Synapse Brain", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "QuickCapture",
                "ProcessAndSaveToVault",
                $"Provider: {(_cmbProvider.SelectedIndex == 0 ? "Gemini" : "Ollama")}",
                "Failed",
                sw.ElapsedMilliseconds,
                errorMessage: ex.Message);

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
