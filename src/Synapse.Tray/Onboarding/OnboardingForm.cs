using System.Diagnostics;
using System.Drawing.Drawing2D;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;
using Synapse.Tray.UI;

namespace Synapse.Tray.Onboarding;

/// <summary>
/// Formulário de configuração inicial / Onboarding do Synapse (Fase 1, US-AUTH.1, US-AUTH.2).
/// Permite validar o GitHub PAT, escolher a pasta do cofre e criar/definir o repositório privado.
/// </summary>
public sealed class OnboardingForm : Form
{
    private readonly TextBox _txtToken;
    private readonly SynapseButton _btnValidateToken;
    private readonly Label _lblTokenStatus;
    private readonly TextBox _txtVaultPath;
    private readonly SynapseButton _btnBrowseVault;
    private readonly TextBox _txtOwner;
    private readonly TextBox _txtRepo;
    private readonly CheckBox _chkAutoCreate;
    private readonly TextBox _txtGeminiApiKey;
    private readonly SynapseButton _btnSave;
    private readonly SynapseButton _btnCancel;

    private readonly SynapseConfigManager _configManager;
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private bool _isTokenValid;

    public OnboardingForm(
        SynapseConfigManager? configManager = null,
        ITokenStore? tokenStore = null,
        HttpClient? httpClient = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();
        _tokenStore = tokenStore ?? new DpapiTokenStore();
        _httpClient = httpClient ?? new HttpClient();

        Text = "Synapse — Configuração Inicial [Pixel Edition]";
        Size = new Size(680, 720);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        SynapseTheme.ApplyFormChrome(this);

        Shown += (_, _) =>
        {
            TopMost = true;
            BringToFront();
            Activate();
            Focus();
            TopMost = false;
        };

        // Header
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            "► CONFIGURAÇÃO DO SYNAPSE",
            "Conecte seu cofre do Obsidian ao seu repositório privado no GitHub",
            72);

        // Content Panel
        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
            AutoScroll = true,
            BackColor = SynapseTheme.Background
        };

        var y = 0;

        // 1. GitHub Token
        var cardToken = SynapseTheme.CreateCard();
        cardToken.Location = new Point(20, y);
        cardToken.Size = new Size(620, 115);
        cardToken.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        AddSectionLabel(cardToken, "1. GITHUB PERSONAL ACCESS TOKEN (PAT)");

        _txtToken = new TextBox { Location = new Point(12, 32), Width = 450, UseSystemPasswordChar = true };
        SynapseTheme.StyleInput(_txtToken);
        cardToken.Controls.Add(_txtToken);

        _btnValidateToken = new SynapseButton { Text = "Validar", Location = new Point(470, 30), Width = 120, Height = 34, Variant = SynapseButtonVariant.Secondary };
        _btnValidateToken.Click += async (_, _) => await ValidateTokenAsync();
        cardToken.Controls.Add(_btnValidateToken);

        _lblTokenStatus = new Label
        {
            Text = "Insira um token do GitHub com escopo 'repo'.",
            ForeColor = SynapseTheme.TextSecondary,
            Font = SynapseTheme.FontCaption(8f),
            Location = new Point(12, 76),
            AutoSize = true
        };
        cardToken.Controls.Add(_lblTokenStatus);

        pnlContent.Controls.Add(cardToken);
        y += cardToken.Height + 14;

        // 2. Vault Path
        var cardVault = SynapseTheme.CreateCard();
        cardVault.Location = new Point(20, y);
        cardVault.Size = new Size(620, 85);
        cardVault.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        AddSectionLabel(cardVault, "2. PASTA DO COFRE LOCAL DO OBSIDIAN");

        _txtVaultPath = new TextBox { Location = new Point(12, 32), Width = 450 };
        SynapseTheme.StyleInput(_txtVaultPath);
        cardVault.Controls.Add(_txtVaultPath);

        _btnBrowseVault = new SynapseButton { Text = "Procurar...", Location = new Point(470, 30), Width = 120, Height = 34, Variant = SynapseButtonVariant.Secondary };
        _btnBrowseVault.Click += (_, _) => BrowseVaultFolder();
        cardVault.Controls.Add(_btnBrowseVault);

        pnlContent.Controls.Add(cardVault);
        y += cardVault.Height + 14;

        // 3. GitHub Repo Config
        var cardRepo = SynapseTheme.CreateCard();
        cardRepo.Location = new Point(20, y);
        cardRepo.Size = new Size(620, 135);
        cardRepo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        AddSectionLabel(cardRepo, "3. REPOSITÓRIO PRIVADO NO GITHUB");

        var lblOwner = new Label { Text = "Usuário / Org:", ForeColor = SynapseTheme.TextSecondary, Font = SynapseTheme.FontCaption(8f), Location = new Point(12, 30), AutoSize = true };
        cardRepo.Controls.Add(lblOwner);

        _txtOwner = new TextBox { Location = new Point(12, 50), Width = 285 };
        SynapseTheme.StyleInput(_txtOwner);
        cardRepo.Controls.Add(_txtOwner);

        var lblRepoName = new Label { Text = "Nome do Repositório:", ForeColor = SynapseTheme.TextSecondary, Font = SynapseTheme.FontCaption(8f), Location = new Point(310, 30), AutoSize = true };
        cardRepo.Controls.Add(lblRepoName);

        _txtRepo = new TextBox { Location = new Point(310, 50), Width = 285, Text = "Synapse-Vault" };
        SynapseTheme.StyleInput(_txtRepo);
        cardRepo.Controls.Add(_txtRepo);

        _chkAutoCreate = new CheckBox
        {
            Text = "Criar repositório privado automaticamente se não existir",
            Checked = true,
            ForeColor = SynapseTheme.TextPrimary,
            Font = SynapseTheme.FontBody(8.5f),
            Location = new Point(12, 92),
            AutoSize = true
        };
        cardRepo.Controls.Add(_chkAutoCreate);

        pnlContent.Controls.Add(cardRepo);
        y += cardRepo.Height + 14;

        // 4. Google Gemini API Key
        var cardGemini = SynapseTheme.CreateCard();
        cardGemini.Location = new Point(20, y);
        cardGemini.Size = new Size(620, 105);
        cardGemini.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        AddSectionLabel(cardGemini, "4. GOOGLE GEMINI API KEY (IA && SEGUNDO CÉREBRO)");

        _txtGeminiApiKey = new TextBox { Location = new Point(12, 32), Width = 585, UseSystemPasswordChar = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        SynapseTheme.StyleInput(_txtGeminiApiKey);
        cardGemini.Controls.Add(_txtGeminiApiKey);

        var lblGeminiHelp = new Label
        {
            Text = "Chave gratuita disponível em https://aistudio.google.com/",
            ForeColor = SynapseTheme.TextSecondary,
            Font = SynapseTheme.FontCaption(8f),
            Location = new Point(12, 68),
            AutoSize = true
        };
        cardGemini.Controls.Add(lblGeminiHelp);

        pnlContent.Controls.Add(cardGemini);

        // Footer
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = SynapseTheme.Surface,
            Padding = new Padding(20, 12, 20, 12)
        };
        pnlFooter.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var penDark = new Pen(SynapseTheme.Border, 2);
            e.Graphics.DrawLine(penDark, 0, 0, pnlFooter.Width, 0);
            using var penLight = new Pen(SynapseTheme.BorderLight, 1);
            e.Graphics.DrawLine(penLight, 0, 1, pnlFooter.Width, 1);
        };

        _btnCancel = new SynapseButton { Text = "Cancelar", Width = 110, Height = 36, Variant = SynapseButtonVariant.Ghost };
        _btnCancel.Location = new Point(pnlFooter.Width - _btnCancel.Width - 20, 16);
        _btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        pnlFooter.Controls.Add(_btnCancel);

        _btnSave = new SynapseButton { Text = "Salvar && Iniciar", Width = 170, Height = 36, Variant = SynapseButtonVariant.Primary };
        _btnSave.Location = new Point(_btnCancel.Left - _btnSave.Width - 12, 16);
        _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnSave.Click += async (_, _) => await SaveAndFinishAsync();
        pnlFooter.Controls.Add(_btnSave);

        Controls.Add(pnlContent);
        Controls.Add(pnlHeader);
        Controls.Add(pnlFooter);

        // Carrega configurações existentes se houver
        _ = LoadExistingConfigAsync();
    }

    private static void AddSectionLabel(Control card, string text)
    {
        card.Controls.Add(new Label
        {
            Text = text,
            Font = SynapseTheme.FontHeadline(8.5f),
            ForeColor = SynapseTheme.AccentPrimary,
            Location = new Point(12, 10),
            AutoSize = true
        });
    }

    private async Task LoadExistingConfigAsync()
    {
        var config = await _configManager.LoadAsync();
        _txtVaultPath.Text = config.VaultPath;
        _txtOwner.Text = config.Owner;
        _txtRepo.Text = string.IsNullOrWhiteSpace(config.Repository) ? "Synapse-Vault" : config.Repository;
        _txtGeminiApiKey.Text = config.GeminiApiKey;

        var token = await _tokenStore.LoadTokenAsync();
        if (token != null && !string.IsNullOrWhiteSpace(token.Token))
        {
            _txtToken.Text = token.Token;
            await ValidateTokenAsync();
        }
    }

    private void BrowseVaultFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Selecione a pasta do seu cofre do Obsidian",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _txtVaultPath.Text = dialog.SelectedPath;
        }
    }

    private async Task<bool> ValidateTokenAsync()
    {
        var token = _txtToken.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            _lblTokenStatus.Text = "Por favor, insira o token do GitHub.";
            _lblTokenStatus.ForeColor = SynapseTheme.Error;
            _isTokenValid = false;
            return false;
        }

        _btnValidateToken.Enabled = false;
        _lblTokenStatus.Text = "Validando token com a API do GitHub...";
        _lblTokenStatus.ForeColor = SynapseTheme.TextSecondary;

        try
        {
            var config = new GitHubClientConfig();
            var authManager = new GitHubAuthManager(_tokenStore, config, _httpClient);
            var valid = await authManager.ValidateTokenAsync(token);

            if (valid)
            {
                _lblTokenStatus.Text = "✓ Token válido e autenticado com sucesso!";
                _lblTokenStatus.ForeColor = SynapseTheme.Success;
                _isTokenValid = true;

                // Tenta preencher o Owner se estiver vazio
                if (string.IsNullOrWhiteSpace(_txtOwner.Text))
                {
                    var userLogin = await GetUserLoginAsync(token);
                    if (!string.IsNullOrWhiteSpace(userLogin))
                    {
                        _txtOwner.Text = userLogin;
                    }
                }

                return true;
            }
            else
            {
                _lblTokenStatus.Text = "Token inválido ou não autorizado.";
                _lblTokenStatus.ForeColor = SynapseTheme.Error;
                _isTokenValid = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            _lblTokenStatus.Text = $"Erro ao validar: {ex.Message}";
            _lblTokenStatus.ForeColor = SynapseTheme.Error;
            _isTokenValid = false;
            return false;
        }
        finally
        {
            _btnValidateToken.Enabled = true;
        }
    }

    private async Task<string?> GetUserLoginAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.UserAgent.ParseAdd("Synapse-Onboarding/1.0");

            using var resp = await _httpClient.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("login", out var loginProp))
                {
                    return loginProp.GetString();
                }
            }
        }
        catch { }

        return null;
    }

    private async Task SaveAndFinishAsync()
    {
        var token = _txtToken.Text.Trim();
        var vaultPath = _txtVaultPath.Text.Trim();
        var owner = _txtOwner.Text.Trim();
        var repo = _txtRepo.Text.Trim();

        var config = new SynapseConfig
        {
            VaultPath = vaultPath,
            Owner = owner,
            Repository = repo,
            Branch = "main",
            GeminiApiKey = _txtGeminiApiKey.Text.Trim()
        };

        if (!SynapseConfigManager.Validate(config, out var errors))
        {
            MessageBox.Show(string.Join("\n", errors), "Validação da Configuração", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_isTokenValid)
        {
            var valid = await ValidateTokenAsync();
            if (!valid)
            {
                MessageBox.Show("Valide seu token do GitHub antes de continuar.", "Token Inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _btnSave.Enabled = false;

        try
        {
            // 1. Salva token com DPAPI
            await _tokenStore.SaveTokenAsync(new GitHubToken(token));

            // 2. Salva arquivo de configuração
            await _configManager.SaveAsync(config);

            // 3. Se solicitado, cria o repositório no GitHub se inexistente
            if (_chkAutoCreate.Checked)
            {
                var gitHubConfig = new GitHubClientConfig
                {
                    Owner = owner,
                    Repository = repo,
                    Branch = "main"
                };

                var authManager = new GitHubAuthManager(_tokenStore, gitHubConfig, _httpClient);
                using var provider = new GitHubProvider(authManager, gitHubConfig, _httpClient);
                await provider.EnsureRepositoryAsync();
            }

            MessageBox.Show("Configuração salva com sucesso!\nO Synapse está pronto para sincronizar.", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao salvar configuração: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSave.Enabled = true;
        }
    }
}
