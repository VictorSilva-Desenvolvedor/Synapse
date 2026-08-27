using System.Diagnostics;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;

namespace Synapse.Tray.Onboarding;

/// <summary>
/// Formulário de configuração inicial / Onboarding do Synapse (Fase 1, US-AUTH.1, US-AUTH.2).
/// Permite validar o GitHub PAT, escolher a pasta do cofre e criar/definir o repositório privado.
/// </summary>
public sealed class OnboardingForm : Form
{
    private readonly TextBox _txtToken;
    private readonly Button _btnValidateToken;
    private readonly Label _lblTokenStatus;
    private readonly TextBox _txtVaultPath;
    private readonly Button _btnBrowseVault;
    private readonly TextBox _txtOwner;
    private readonly TextBox _txtRepo;
    private readonly CheckBox _chkAutoCreate;
    private readonly TextBox _txtGeminiApiKey;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

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

        Text = "Synapse — Configuração Inicial";
        Size = new Size(620, 590);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            BackColor = Color.FromArgb(24, 24, 27)
        };

        var lblHeaderTitle = new Label
        {
            Text = "Configuração do Synapse",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 12),
            AutoSize = true
        };

        var lblHeaderSubtitle = new Label
        {
            Text = "Conecte seu cofre do Obsidian ao seu repositório privado no GitHub",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(20, 36),
            AutoSize = true
        };

        pnlHeader.Controls.Add(lblHeaderTitle);
        pnlHeader.Controls.Add(lblHeaderSubtitle);
        Controls.Add(pnlHeader);

        // Content Panel
        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            AutoScroll = true
        };

        var y = 80;

        // 1. GitHub Token
        var lblToken = new Label
        {
            Text = "1. GitHub Personal Access Token (PAT):",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblToken);
        y += 24;

        _txtToken = new TextBox
        {
            Location = new Point(24, y),
            Width = 430,
            UseSystemPasswordChar = true
        };
        pnlContent.Controls.Add(_txtToken);

        _btnValidateToken = new Button
        {
            Text = "Validar",
            Location = new Point(465, y - 1),
            Width = 110,
            Height = 27
        };
        _btnValidateToken.Click += async (_, _) => await ValidateTokenAsync();
        pnlContent.Controls.Add(_btnValidateToken);
        y += 30;

        _lblTokenStatus = new Label
        {
            Text = "Insira um token do GitHub com escopo 'repo'.",
            ForeColor = Color.DimGray,
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(_lblTokenStatus);
        y += 36;

        // 2. Vault Path
        var lblVault = new Label
        {
            Text = "2. Pasta do Cofre Local do Obsidian:",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblVault);
        y += 24;

        _txtVaultPath = new TextBox
        {
            Location = new Point(24, y),
            Width = 430
        };
        pnlContent.Controls.Add(_txtVaultPath);

        _btnBrowseVault = new Button
        {
            Text = "Procurar...",
            Location = new Point(465, y - 1),
            Width = 110,
            Height = 27
        };
        _btnBrowseVault.Click += (_, _) => BrowseVaultFolder();
        pnlContent.Controls.Add(_btnBrowseVault);
        y += 40;

        // 3. GitHub Repo Config
        var lblRepoConfig = new Label
        {
            Text = "3. Repositório Privado no GitHub:",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblRepoConfig);
        y += 24;

        var lblOwner = new Label
        {
            Text = "Usuário / Organização:",
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblOwner);

        _txtOwner = new TextBox
        {
            Location = new Point(24, y + 20),
            Width = 260
        };
        pnlContent.Controls.Add(_txtOwner);

        var lblRepoName = new Label
        {
            Text = "Nome do Repositório:",
            Location = new Point(310, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblRepoName);

        _txtRepo = new TextBox
        {
            Location = new Point(310, y + 20),
            Width = 265,
            Text = "Synapse-Vault"
        };
        pnlContent.Controls.Add(_txtRepo);
        y += 54;

        _chkAutoCreate = new CheckBox
        {
            Text = "Criar repositório privado automaticamente se não existir",
            Checked = true,
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(_chkAutoCreate);
        y += 34;

        // 4. Google Gemini API Key (Opcional)
        var lblGemini = new Label
        {
            Text = "4. Google Gemini API Key (Opcional - para IA e Segundo Cérebro):",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblGemini);
        y += 22;

        _txtGeminiApiKey = new TextBox
        {
            Location = new Point(24, y),
            Width = 550,
            UseSystemPasswordChar = true
        };
        pnlContent.Controls.Add(_txtGeminiApiKey);
        y += 24;

        var lblGeminiHelp = new Label
        {
            Text = "Obtenha uma chave gratuita em https://aistudio.google.com/",
            ForeColor = Color.DimGray,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            Location = new Point(24, y),
            AutoSize = true
        };
        pnlContent.Controls.Add(lblGeminiHelp);
        y += 34;

        // Footer buttons
        _btnSave = new Button
        {
            Text = "Salvar e Iniciar",
            Location = new Point(300, y),
            Width = 160,
            Height = 34,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _btnSave.Click += async (_, _) => await SaveAndFinishAsync();
        pnlContent.Controls.Add(_btnSave);

        _btnCancel = new Button
        {
            Text = "Cancelar",
            Location = new Point(470, y),
            Width = 105,
            Height = 34
        };
        _btnCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        pnlContent.Controls.Add(_btnCancel);

        Controls.Add(pnlContent);

        // Carrega configurações existentes se houver
        _ = LoadExistingConfigAsync();
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
            _lblTokenStatus.ForeColor = Color.Red;
            _isTokenValid = false;
            return false;
        }

        _btnValidateToken.Enabled = false;
        _lblTokenStatus.Text = "Validando token com a API do GitHub...";
        _lblTokenStatus.ForeColor = Color.DimGray;

        try
        {
            var config = new GitHubClientConfig();
            var authManager = new GitHubAuthManager(_tokenStore, config, _httpClient);
            var valid = await authManager.ValidateTokenAsync(token);

            if (valid)
            {
                _lblTokenStatus.Text = "✓ Token válido e autenticado com sucesso!";
                _lblTokenStatus.ForeColor = Color.FromArgb(16, 185, 129);
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
                _lblTokenStatus.ForeColor = Color.Red;
                _isTokenValid = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            _lblTokenStatus.Text = $"Erro ao validar: {ex.Message}";
            _lblTokenStatus.ForeColor = Color.Red;
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
