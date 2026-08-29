using System.Diagnostics;
using Synapse.Agent;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;
using Synapse.Brain.Services;
using Synapse.Core.Logging;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;
using Synapse.Tray.Agent;
using Synapse.Tray.Ipc;
using Synapse.Tray.UI;
using Timer = System.Windows.Forms.Timer;

namespace Synapse.Tray;

/// <summary>
/// Contexto da aplicação da bandeja do sistema (RF-UX.1, RF-UX.2).
/// Gerencia o ícone de notificação, menu de contexto, atualizações de status via IPC e notificações do SO.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IpcClient _ipcClient;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusHeaderItem;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ToolStripMenuItem _reconnectItem;
    private readonly ToolStripMenuItem _remoteControlItem;
    private readonly ToolStripMenuItem _openLogsItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Timer _pollTimer;
    private readonly CancellationTokenSource _remoteAgentCts = new();

    private readonly SynchronizationContext _syncContext;
    private IpcStatusPayload? _currentStatus;
    private bool _isDisposed;

    public TrayApplicationContext(IpcClient? ipcClient = null)
    {
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _ipcClient = ipcClient ?? new IpcClient();

        var contextMenu = new ContextMenuStrip
        {
            Renderer = new SynapseMenuRenderer(),
            BackColor = SynapseTheme.Surface,
            ForeColor = SynapseTheme.TextPrimary,
            Font = SynapseTheme.FontBody(8.5f),
            ShowImageMargin = false,
            Padding = new Padding(4)
        };

        _statusHeaderItem = new ToolStripMenuItem("● STATUS: CONECTANDO...")
        {
            Enabled = false,
            ForeColor = SynapseTheme.AccentPrimary,
            Font = SynapseTheme.FontHeadline(8.5f)
        };

        _pauseResumeItem = new ToolStripMenuItem("Pausar Sincronização", null, async (_, _) => await TogglePauseAsync());
        _reconnectItem = new ToolStripMenuItem("Reconectar GitHub", null, async (_, _) => await ReconnectAsync());
        _remoteControlItem = new ToolStripMenuItem("Controle Remoto: Desativado", null, async (_, _) => await ToggleRemoteControlAsync());
        var quickCaptureItem = new ToolStripMenuItem("► Captura Rápida (Brain)...", null, (_, _) => OpenQuickCapture());
        var chatVaultItem = new ToolStripMenuItem("► Chat com Cofre (RAG)...", null, (_, _) => OpenChatVault());
        var reviewItem = new ToolStripMenuItem("► Flashcards (SM-2)...", null, (_, _) => OpenFlashcardReview());
        var statsItem = new ToolStripMenuItem("► Estatísticas && Backup...", null, (_, _) => OpenVaultStats());
        var diagnosticsItem = new ToolStripMenuItem("► Diagnóstico && Conflitos...", null, (_, _) => OpenDiagnostics());
        var settingsItem = new ToolStripMenuItem("► Configurações...", null, (_, _) => OpenSettings());
        _openLogsItem = new ToolStripMenuItem("► Abrir Pasta de Logs", null, async (_, _) => await OpenLogsFolderAsync());
        _exitItem = new ToolStripMenuItem("✖ Sair da Bandeja", null, (_, _) => ExitThread());

        contextMenu.Items.Add(_statusHeaderItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(quickCaptureItem);
        contextMenu.Items.Add(chatVaultItem);
        contextMenu.Items.Add(reviewItem);
        contextMenu.Items.Add(statsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_pauseResumeItem);
        contextMenu.Items.Add(_reconnectItem);
        contextMenu.Items.Add(_remoteControlItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(diagnosticsItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(_openLogsItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = IconGenerator.GetIconForState("Desconectado", false),
            ContextMenuStrip = contextMenu,
            Text = "Synapse (Pixel Edition)",
            Visible = true
        };

        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenQuickCapture();
            }
        };

        _pollTimer = new Timer { Interval = 2500 };
        _pollTimer.Tick += async (_, _) => await PollStatusAsync();
        _pollTimer.Start();

        // Abre a interface visual diretamente na tela do usuário
        OpenSettings();

        // Dispara a verificação inicial após inicialização do loop de mensagens
        _syncContext.Post(async _ => await CheckInitialOnboardingAsync(), null);
    }

    private async Task PollStatusAsync()
    {
        try
        {
            var status = await _ipcClient.GetStatusAsync();
            if (status != null)
            {
                UpdateUI(status);
            }
            else
            {
                UpdateUI(new IpcStatusPayload { Estado = "Desconectado" });
            }
        }
        catch
        {
            UpdateUI(new IpcStatusPayload { Estado = "Desconectado" });
        }
    }

    private void UpdateUI(IpcStatusPayload status)
    {
        _currentStatus = status;

        var lastSyncText = status.UltimaSincronizacaoEm.HasValue
            ? $" (Último: {status.UltimaSincronizacaoEm.Value.ToLocalTime():HH:mm:ss})"
            : string.Empty;

        var pendingText = status.ItensPendentes > 0 ? $" | {status.ItensPendentes} pendentes" : string.Empty;

        _statusHeaderItem.Text = status.Pausado
            ? "Status: Pausado"
            : $"Status: {status.Estado}{pendingText}";

        _pauseResumeItem.Text = status.Pausado ? "Retomar Sincronização" : "Pausar Sincronização";

        _notifyIcon.Icon = IconGenerator.GetIconForState(status.Estado, status.Pausado);
        _notifyIcon.Text = TruncateText($"Synapse: {status.Estado}{lastSyncText}", 63);

        if (status.Estado == "AuthRequired")
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                "Synapse — Autenticação Necessária",
                "O token do GitHub expirou ou é inválido. Clique com o botão direito para reconectar.",
                ToolTipIcon.Warning);
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_currentStatus == null) return;

        try
        {
            var newStatus = _currentStatus.Pausado
                ? await _ipcClient.ResumeAsync()
                : await _ipcClient.PauseAsync();

            if (newStatus != null)
            {
                UpdateUI(newStatus);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível alterar o estado da sincronização: {ex.Message}", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ReconnectAsync()
    {
        try
        {
            var newStatus = await _ipcClient.ReconnectAsync();
            if (newStatus != null)
            {
                UpdateUI(newStatus);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao reconectar: {ex.Message}", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OpenLogsFolderAsync()
    {
        try
        {
            var logPath = await _ipcClient.GetLogPathAsync();
            if (!string.IsNullOrEmpty(logPath) && Directory.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("A pasta de logs ainda não foi criada ou não está acessível.", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao abrir pasta de logs: {ex.Message}", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ToggleRemoteControlAsync()
    {
        try
        {
            var configManager = new Synapse.Sync.Config.SynapseConfigManager();
            var config = await configManager.LoadAsync();
            config.RemoteControlEnabled = !config.RemoteControlEnabled;
            await configManager.SaveAsync(config);

            _remoteControlItem.Text = config.RemoteControlEnabled ? "Controle Remoto: Ativado" : "Controle Remoto: Desativado";
            _remoteControlItem.Checked = config.RemoteControlEnabled;

            var statusMsg = config.RemoteControlEnabled ? "ativado" : "desativado";
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "ToggleRemoteControl", $"NovoEstado: {statusMsg}");

            _notifyIcon.ShowBalloonTip(
                3000,
                "Synapse Remote",
                $"Controle Remoto {statusMsg} com sucesso.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao alterar estado do controle remoto: {ex.Message}", "Synapse Remote", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task UpdateRemoteControlMenuAsync()
    {
        try
        {
            var configManager = new Synapse.Sync.Config.SynapseConfigManager();
            var config = await configManager.LoadAsync();
            _remoteControlItem.Text = config.RemoteControlEnabled ? "Controle Remoto: Ativado" : "Controle Remoto: Desativado";
            _remoteControlItem.Checked = config.RemoteControlEnabled;
        }
        catch { }
    }

    private async Task CheckInitialOnboardingAsync()
    {
        await PollStatusAsync();
        await UpdateRemoteControlMenuAsync();

        var configManager = new SynapseConfigManager();
        var config = await configManager.LoadAsync();

        // Abre a janela do Synapse diretamente na tela do usuário
        OpenSettings();

        if (config.IsConfigured)
        {
            _ = StartRemoteAgentAsync(config);
        }
    }

    private async Task StartRemoteAgentAsync(SynapseConfig config)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dedicatedTokenPath = Path.Combine(appData, "Synapse", "remote_agent_token.dat");
            var dedicatedStore = new DpapiTokenStore(dedicatedTokenPath);

            var clientConfig = new GitHubClientConfig
            {
                Owner = config.Owner,
                Repository = config.Repository,
                Branch = string.IsNullOrWhiteSpace(config.Branch) ? "main" : config.Branch
            };

            var authManager = new GitHubAuthManager(dedicatedStore, clientConfig);
            var tokenReady = await RemoteAgentTokenResolver.EnsureTokenAsync(authManager, dedicatedStore, _remoteAgentCts.Token);
            if (!tokenReady)
            {
                return; // Sem token dedicado configurado ainda
            }

            var gitHubProvider = new GitHubProvider(authManager, clientConfig);
            var configManager = new SynapseConfigManager();

            var auditLog = new RemoteAuditLog(config.VaultPath);
            var confirmationPrompt = new WinFormsConfirmationPrompt();
            var uiAutomation = new WindowsUiAutomationAdapter();

            IVaultBrainQuery? brainQuery = null;
            if (!string.IsNullOrWhiteSpace(config.GeminiApiKey))
            {
                var brainConfig = new BrainConfig
                {
                    GeminiApiKey = config.GeminiApiKey,
                    GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.6-flash" : config.GeminiModel
                };
                var embeddingProvider = new GeminiEmbeddingProvider(brainConfig);
                var aiProvider = new GeminiAiProvider(brainConfig);
                brainQuery = new VaultRagEngine(embeddingProvider, aiProvider);
            }

            var executor = new RemoteCommandExecutor(
                () => configManager.LoadAsync().GetAwaiter().GetResult(),
                confirmationPrompt: confirmationPrompt,
                uiAutomation: uiAutomation,
                brainQuery: brainQuery,
                auditLog: auditLog);

            var pollingInterval = TimeSpan.FromSeconds(
                config.RemoteControlPollingIntervalSeconds > 0
                    ? config.RemoteControlPollingIntervalSeconds
                    : 10);

            var poller = new RemoteCommandPoller(
                cloudProvider: gitHubProvider,
                executor: executor,
                auditLog: auditLog,
                interval: pollingInterval);

            _ = poller.RunAsync(_remoteAgentCts.Token);
        }
        catch
        {
            // Execução silenciosa em background
        }
    }

    private Onboarding.OnboardingForm? _settingsForm;

    private void OpenSettings()
    {
        _syncContext.Post(_ =>
        {
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenSettings");
            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new Onboarding.OnboardingForm();
                _settingsForm.FormClosed += (_, _) =>
                {
                    _ = _ipcClient.ReconnectAsync();
                    _ = PollStatusAsync();
                    _settingsForm = null;
                };
                _settingsForm.Show();
            }
            else
            {
                if (_settingsForm.WindowState == FormWindowState.Minimized)
                {
                    _settingsForm.WindowState = FormWindowState.Normal;
                }
                _settingsForm.Show();
                _settingsForm.BringToFront();
                _settingsForm.Activate();
            }
        }, null);
    }

    private void OpenDiagnostics()
    {
        _syncContext.Post(_ =>
        {
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenDiagnostics");
            var form = new Diagnostics.DiagnosticsForm();
            form.Show();
            form.BringToFront();
            form.Activate();
        }, null);
    }

    private void OpenQuickCapture()
    {
        _syncContext.Post(_ =>
        {
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenQuickCapture");
            var form = new QuickCapture.QuickCaptureForm();
            form.Show();
            form.BringToFront();
            form.Activate();
        }, null);
    }

    private void OpenChatVault()
    {
        _syncContext.Post(_ =>
        {
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenChatVault");
            var form = new Chat.ChatVaultForm();
            form.Show();
            form.BringToFront();
            form.Activate();
        }, null);
    }

    private void OpenFlashcardReview()
    {
        _syncContext.Post(_ =>
        {
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenFlashcardReview");
            var form = new Review.FlashcardReviewForm();
            form.Show();
            form.BringToFront();
            form.Activate();
        }, null);
    }

    private void OpenVaultStats()
    {
        _syncContext.Post(_ =>
        {
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenVaultStats");
            var form = new Metrics.VaultStatsForm();
            form.Show();
            form.BringToFront();
            form.Activate();
        }, null);
    }

    private static string TruncateText(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;

        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _remoteAgentCts.Cancel();
            _remoteAgentCts.Dispose();
            _ = _ipcClient.DisposeAsync();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
