using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;
using Synapse.Tray.UI;

namespace Synapse.Tray.Onboarding;

/// <summary>
/// Configuracao inicial do Synapse: valida o PAT do GitHub, escolhe a pasta do cofre
/// e cria/define o repositorio privado. Porte WPF de OnboardingForm.
/// </summary>
public partial class OnboardingWindow : PixelWindow
{
    private readonly SynapseConfigManager _configManager;
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private bool _isTokenValid;

    public OnboardingWindow(
        SynapseConfigManager? configManager = null,
        ITokenStore? tokenStore = null,
        HttpClient? httpClient = null)
    {
        _configManager = configManager ?? new SynapseConfigManager();
        _tokenStore = tokenStore ?? new DpapiTokenStore();
        _httpClient = httpClient ?? new HttpClient();

        InitializeComponent();

        Loaded += async (_, _) =>
        {
            Activate();
            await LoadExistingConfigAsync();
        };
    }

    /// <summary>Preenche a tela com dados de exemplo. Usado pelo harness de captura.</summary>
    public void SetSampleData(string owner, string repo, string vaultPath, string tokenStatus, bool tokenOk)
    {
        OwnerBox.Text = owner;
        RepoBox.Text = repo;
        VaultPathBox.Text = vaultPath;
        TokenBox.Password = "ghp_exemplo_para_captura_de_tela";
        GeminiKeyBox.Password = "AIza_exemplo_para_captura";
        SetTokenStatus(tokenStatus, tokenOk ? "SuccessBrush" : "TextSecondaryBrush");
    }

    private void SetTokenStatus(string message, string brushKey)
    {
        TokenStatusText.Text = message;
        TokenStatusText.Foreground = (Brush)FindResource(brushKey);
    }

    private async Task LoadExistingConfigAsync()
    {
        var config = await _configManager.LoadAsync();
        VaultPathBox.Text = config.VaultPath;
        OwnerBox.Text = config.Owner;
        RepoBox.Text = string.IsNullOrWhiteSpace(config.Repository) ? "Synapse-Vault" : config.Repository;
        GeminiKeyBox.Password = config.GeminiApiKey ?? string.Empty;

        var token = await _tokenStore.LoadTokenAsync();
        if (token is not null && !string.IsNullOrWhiteSpace(token.Token))
        {
            TokenBox.Password = token.Token;
            await ValidateTokenAsync();
        }
    }

    private void OnBrowseVault(object sender, RoutedEventArgs e)
    {
        // OpenFolderDialog e a substituicao WPF do FolderBrowserDialog do WinForms,
        // disponivel a partir do .NET 8.
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta do seu cofre do Obsidian",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            VaultPathBox.Text = dialog.FolderName;
        }
    }

    private async void OnValidateToken(object sender, RoutedEventArgs e) => await ValidateTokenAsync();

    private async Task<bool> ValidateTokenAsync()
    {
        var token = TokenBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            SetTokenStatus("Por favor, insira o token do GitHub.", "ErrorBrush");
            _isTokenValid = false;
            return false;
        }

        ValidateButton.IsEnabled = false;
        SetTokenStatus("Validando token com a API do GitHub...", "TextSecondaryBrush");

        try
        {
            var authManager = new GitHubAuthManager(_tokenStore, new GitHubClientConfig(), _httpClient);
            var valid = await authManager.ValidateTokenAsync(token);

            if (!valid)
            {
                SetTokenStatus("Token invalido ou nao autorizado.", "ErrorBrush");
                _isTokenValid = false;
                return false;
            }

            SetTokenStatus("Token valido e autenticado.", "SuccessBrush");
            _isTokenValid = true;

            if (string.IsNullOrWhiteSpace(OwnerBox.Text))
            {
                var userLogin = await GetUserLoginAsync(token);
                if (!string.IsNullOrWhiteSpace(userLogin))
                {
                    OwnerBox.Text = userLogin;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            SetTokenStatus($"Erro ao validar: {ex.Message}", "ErrorBrush");
            _isTokenValid = false;
            return false;
        }
        finally
        {
            ValidateButton.IsEnabled = true;
        }
    }

    private async Task<string?> GetUserLoginAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.UserAgent.ParseAdd("Synapse-Onboarding/1.0");

            using var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("login", out var loginProp) ? loginProp.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        var owner = OwnerBox.Text.Trim();
        var repo = RepoBox.Text.Trim();

        var config = new SynapseConfig
        {
            VaultPath = VaultPathBox.Text.Trim(),
            Owner = owner,
            Repository = repo,
            Branch = "main",
            GeminiApiKey = GeminiKeyBox.Password.Trim()
        };

        if (!SynapseConfigManager.Validate(config, out var errors))
        {
            PixelMessageBox.Show(string.Join("\n", errors), "VALIDACAO", PixelMessageKind.Warning, this);
            return;
        }

        if (!_isTokenValid && !await ValidateTokenAsync())
        {
            PixelMessageBox.Show("Valide seu token do GitHub antes de continuar.", "TOKEN INVALIDO", PixelMessageKind.Warning, this);
            return;
        }

        SaveButton.IsEnabled = false;

        try
        {
            await _tokenStore.SaveTokenAsync(new GitHubToken(token));
            await _configManager.SaveAsync(config);

            if (AutoCreateCheck.IsChecked == true)
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

            PixelMessageBox.Show(
                "Configuracao salva com sucesso.\nO Synapse esta pronto para sincronizar.",
                "SYNAPSE",
                PixelMessageKind.Success,
                this);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Falha ao salvar configuracao: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
