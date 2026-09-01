using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
using Synapse.Tray.Chat;
using Synapse.Tray.Diagnostics;
using Synapse.Tray.Ipc;
using Synapse.Tray.Metrics;
using Synapse.Tray.Onboarding;
using Synapse.Tray.QuickCapture;
using Synapse.Tray.RemoteApps;
using Synapse.Tray.Review;
using Synapse.Tray.UI;
using WinFormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using WinFormsMouseButtons = System.Windows.Forms.MouseButtons;
using WinFormsToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace Synapse.Tray;

/// <summary>
/// Host da bandeja do sistema (RF-UX.1, RF-UX.2). Substitui TrayApplicationContext.
///
/// O NotifyIcon continua vindo do WinForms de proposito: a API de bandeja do Windows
/// exige um System.Drawing.Icon e o WPF nao tem equivalente nativo. Tudo o mais - o
/// menu de contexto e todas as janelas - e WPF, para que o pixel art valha tambem aqui.
/// </summary>
public sealed class SynapseTrayApp : IDisposable
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IpcClient _ipcClient;
    private readonly WinFormsNotifyIcon _notifyIcon;
    private readonly ContextMenu _menu;
    private readonly TrayMenuPanel _panel;
    private readonly MenuItem _pauseResumeItem;
    private readonly MenuItem _remoteControlItem;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _iconReaffirmTimer;
    private readonly CancellationTokenSource _remoteAgentCts = new();
    private readonly Dispatcher _dispatcher;
    private readonly SynapseConfigManager _configManager;
    private readonly Func<BrainConfig, VaultRagEngine> _ragEngineFactory;

    private IpcStatusPayload? _currentStatus;
    private OnboardingWindow? _settingsWindow;
    private bool _isDisposed;
    private (string Estado, bool Pausado)? _currentIconState;

    public SynapseTrayApp(
        IpcClient? ipcClient = null,
        SynapseConfigManager? configManager = null,
        Func<BrainConfig, VaultRagEngine>? ragEngineFactory = null,
        bool autoStartOnboarding = true)
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _ipcClient = ipcClient ?? new IpcClient();
        _configManager = configManager ?? new SynapseConfigManager();
        _ragEngineFactory = ragEngineFactory ?? (cfg =>
        {
            var brainLogger = BrainProviderFactory.GetLogger("Brain");
            return new VaultRagEngine(
                BrainProviderFactory.CreateEmbeddingProvider(cfg, brainLogger),
                BrainProviderFactory.CreateAiProvider(cfg, brainLogger),
                cfg);
        });

        PixelWindow.EnsureTheme();

        // As quatro acoes diarias viraram ladrilhos no topo; o resto continua lista.
        _panel = new TrayMenuPanel();
        _panel.QuickCaptureRequested += () => RunFromTile(OpenQuickCapture);
        _panel.ChatRequested += () => RunFromTile(OpenChatVault);
        _panel.FlashcardsRequested += () => RunFromTile(OpenFlashcardReview);
        _panel.StatsRequested += () => RunFromTile(OpenVaultStats);

        _pauseResumeItem = NewItem("Pausar Sincronizacao", async () => await TogglePauseAsync());
        _remoteControlItem = NewItem("Controle Remoto: Desativado", async () => await ToggleRemoteControlAsync());

        _menu = new ContextMenu
        {
            Items =
            {
                _panel.AsMenuItem(),
                new Separator(),
                _pauseResumeItem,
                NewItem("Reconectar GitHub", async () => await ReconnectAsync()),
                _remoteControlItem,
                new Separator(),
                NewItem("Diagnostico e Conflitos...", OpenDiagnostics),
                NewItem("Apps Permitidos (Controle Remoto)...", OpenAllowedApps),
                NewItem("Configuracoes...", OpenSettings),
                NewItem("Abrir Pasta de Logs", async () => await OpenLogsFolderAsync()),
                new Separator(),
                NewItem("Sair da Bandeja", Shutdown)
            }
        };

        // Teclas 1-4 disparam os ladrilhos. Preview para chegar antes da navegacao
        // propria do ContextMenu, que so conhece MenuItem.
        _menu.PreviewKeyDown += (_, e) =>
        {
            // RunFromTile ja fecha o menu; aqui so marcamos a tecla como tratada.
            if (_panel.TryHandleAccelerator(e.Key))
            {
                e.Handled = true;
            }
        };

        // Deixa o Tab circular pelos ladrilhos, ja que as setas nao passam por eles.
        KeyboardNavigation.SetTabNavigation(_menu, KeyboardNavigationMode.Continue);

        _currentIconState = ("Desconectado", false);
        _notifyIcon = new WinFormsNotifyIcon
        {
            Icon = IconGenerator.GetIconForState("Desconectado", false),
            Text = "Synapse (Pixel Edition)",
            Visible = true
        };

        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == WinFormsMouseButtons.Right)
            {
                _dispatcher.BeginInvoke(ShowTrayMenu);
            }
        };

        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WinFormsMouseButtons.Left)
            {
                _dispatcher.BeginInvoke(OpenQuickCapture);
            }
        };

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _pollTimer.Tick += async (_, _) => await PollStatusAsync();
        _pollTimer.Start();

        // Apps iniciados via autostart do login as vezes correm contra a inicializacao da
        // bandeja do Explorer, e o NotifyIcon.Visible=true acima silenciosamente nao "pega"
        // (o setter do WinForms so chama o Shell_NotifyIcon de verdade quando o valor MUDA,
        // entao reatribuir "true" de novo nao adianta). Isso e uma corrida de startup, que
        // acontece no maximo uma vez - por isso o toggle roda uma unica vez aqui, nao a cada
        // poll (repetir a cada 2.5s faria um DELETE+ADD real do icone pra sempre, causando
        // flicker visivel e cortando balloon tips antes da hora). Reinicio do Explorer ja e
        // tratado internamente pelo NotifyIcon do WinForms (mensagem TaskbarCreated).
        _iconReaffirmTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _iconReaffirmTimer.Tick += (sender, _) =>
        {
            ((DispatcherTimer)sender!).Stop();
            if (_isDisposed) return;
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;
            }
            catch { }
        };
        _iconReaffirmTimer.Start();

        if (autoStartOnboarding)
        {
            _dispatcher.BeginInvoke(async () => await CheckInitialOnboardingAsync());
        }
    }

    private static MenuItem NewItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// Um Button dentro de um ContextMenu nao fecha o menu ao ser clicado — so MenuItem
    /// tem esse comportamento. Os ladrilhos precisam fechar explicitamente, senao o menu
    /// fica aberto atras da janela que acabou de abrir.
    /// </summary>
    private void RunFromTile(Action action)
    {
        _menu.IsOpen = false;
        action();
    }

    /// <summary>
    /// Um ContextMenu WPF aberto a partir de um NotifyIcon do WinForms nao recebe foco
    /// sozinho, e sem foco ele nao fecha quando o usuario clica fora. Trazer a janela do
    /// popup para o primeiro plano resolve; e o mesmo truque que as bibliotecas de bandeja
    /// para WPF usam.
    /// </summary>
    private void ShowTrayMenu()
    {
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;

        if (PresentationSource.FromVisual(_menu) is HwndSource source)
        {
            SetForegroundWindow(source.Handle);
        }
    }

    private async Task PollStatusAsync()
    {
        try
        {
            var status = await _ipcClient.GetStatusAsync();
            UpdateUI(status ?? new IpcStatusPayload { Estado = "Desconectado" });
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
            ? $" (Ultimo: {status.UltimaSincronizacaoEm.Value.ToLocalTime():HH:mm:ss})"
            : string.Empty;

        var pendingText = status.ItensPendentes > 0
            ? $"{status.ItensPendentes} pendentes"
            : "0 pendentes";

        var lastSync = status.UltimaSincronizacaoEm.HasValue
            ? $"ultimo sync {status.UltimaSincronizacaoEm.Value.ToLocalTime():HH:mm}"
            : "sem sync ainda";

        var kind = status.Pausado
            ? TrayStatusKind.Idle
            : status.Estado switch
            {
                "Sincronizado" => TrayStatusKind.Ok,
                "Sincronizando" => TrayStatusKind.Working,
                "Offline" => TrayStatusKind.Warning,
                "AuthRequired" => TrayStatusKind.Error,
                "Erro" => TrayStatusKind.Error,
                _ => TrayStatusKind.Idle
            };

        _panel.SetStatus(
            kind,
            status.Pausado ? "Pausado" : status.Estado,
            $"{lastSync} · {pendingText}");

        _pauseResumeItem.Header = status.Pausado ? "Retomar Sincronizacao" : "Pausar Sincronizacao";

        // So recria o icone quando o estado visivel realmente muda: cada icone gerado
        // vaza um handle nativo (ver IconGenerator.ReleaseIcon), entao recriar a cada
        // poll de 2.5s incondicionalmente esgotava a cota de handles do processo em
        // poucas horas e derrubava a bandeja sem excecao nem crash report.
        var newIconState = (status.Estado, status.Pausado);
        if (_currentIconState != newIconState)
        {
            var oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = IconGenerator.GetIconForState(status.Estado, status.Pausado);
            IconGenerator.ReleaseIcon(oldIcon);
            _currentIconState = newIconState;
        }

        _notifyIcon.Text = TruncateText($"Synapse: {status.Estado}{lastSyncText}", 63);

        if (status.Estado == "AuthRequired")
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                "Synapse - Autenticacao Necessaria",
                "O token do GitHub expirou ou e invalido. Clique com o botao direito para reconectar.",
                WinFormsToolTipIcon.Warning);
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_currentStatus is null)
        {
            return;
        }

        try
        {
            var newStatus = _currentStatus.Pausado
                ? await _ipcClient.ResumeAsync()
                : await _ipcClient.PauseAsync();

            if (newStatus is not null)
            {
                UpdateUI(newStatus);
            }
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Nao foi possivel alterar o estado da sincronizacao: {ex.Message}", "SYNAPSE", PixelMessageKind.Error);
        }
    }

    private async Task ReconnectAsync()
    {
        try
        {
            var newStatus = await _ipcClient.ReconnectAsync();
            if (newStatus is not null)
            {
                UpdateUI(newStatus);
            }
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Falha ao reconectar: {ex.Message}", "SYNAPSE", PixelMessageKind.Error);
        }
    }

    private async Task OpenLogsFolderAsync()
    {
        try
        {
            var logPath = await _ipcClient.GetLogPathAsync();
            if (!string.IsNullOrEmpty(logPath) && Directory.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo { FileName = logPath, UseShellExecute = true });
            }
            else
            {
                PixelMessageBox.Show("A pasta de logs ainda nao foi criada ou nao esta acessivel.", "SYNAPSE", PixelMessageKind.Info);
            }
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Erro ao abrir pasta de logs: {ex.Message}", "SYNAPSE", PixelMessageKind.Error);
        }
    }

    private async Task ToggleRemoteControlAsync()
    {
        try
        {
            var configManager = new SynapseConfigManager();
            var config = await configManager.LoadAsync();
            config.RemoteControlEnabled = !config.RemoteControlEnabled;
            await configManager.SaveAsync(config);

            ApplyRemoteControlState(config.RemoteControlEnabled);

            var statusMsg = config.RemoteControlEnabled ? "ativado" : "desativado";
            _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "ToggleRemoteControl", $"NovoEstado: {statusMsg}");

            _notifyIcon.ShowBalloonTip(3000, "Synapse Remote", $"Controle Remoto {statusMsg}.", WinFormsToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Falha ao alterar o controle remoto: {ex.Message}", "SYNAPSE REMOTE", PixelMessageKind.Error);
        }
    }

    private void ApplyRemoteControlState(bool enabled)
    {
        _remoteControlItem.Header = enabled ? "Controle Remoto: Ativado" : "Controle Remoto: Desativado";
        _remoteControlItem.IsChecked = enabled;
    }

    internal async Task CheckInitialOnboardingAsync()
    {
        await PollStatusAsync();

        var config = await _configManager.LoadAsync();

        ApplyRemoteControlState(config.RemoteControlEnabled);

        if (!config.IsConfigured)
        {
            OpenSettings();
            return;
        }

        // Se o cofre estiver configurado, cria o motor de RAG e dispara a indexação proativa em background
        VaultRagEngine? sharedRagEngine = null;
        if (!string.IsNullOrWhiteSpace(config.VaultPath) && Directory.Exists(config.VaultPath))
        {
            var brainConfig = BrainProviderFactory.BuildBrainConfig(config);
            sharedRagEngine = _ragEngineFactory(brainConfig);

            _ = Task.Run(async () =>
            {
                try
                {
                    _ = SynapseActivityLogger.Instance.LogActionAsync(
                        "Brain",
                        "StartIndexing",
                        $"Iniciando indexacao proativa do cofre em background: {config.VaultPath}");

                    await sharedRagEngine.IndexVaultAsync(config.VaultPath, _remoteAgentCts.Token);

                    _ = SynapseActivityLogger.Instance.LogActionAsync(
                        "Brain",
                        "IndexCompleted",
                        $"Indexacao proativa em background concluida para: {config.VaultPath}");
                }
                catch (Exception ex)
                {
                    _ = SynapseActivityLogger.Instance.LogActionAsync(
                        "Brain",
                        "IndexFailed",
                        status: "Failed",
                        errorMessage: ex.Message);
                }
            }, _remoteAgentCts.Token);
        }

        _ = StartRemoteAgentAsync(config, sharedRagEngine);
    }

    private async Task StartRemoteAgentAsync(SynapseConfig config, VaultRagEngine? sharedRagEngine = null)
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
                _ = SynapseActivityLogger.Instance.LogActionAsync(
                    "RemoteAgent",
                    "StartupError",
                    status: "Failed",
                    errorMessage: "Token dedicado do agente remoto nao disponivel (nem no arquivo DPAPI, nem em SYNAPSE_REMOTE_TOKEN). O poller de comandos remotos nao foi iniciado.");
                return;
            }

            var gitHubProvider = new GitHubProvider(authManager, clientConfig);
            var configManager = new SynapseConfigManager();
            var auditLog = new RemoteAuditLog(config.VaultPath);

            // Confirmacao humana em WPF; o padrao continua sendo NEGAR.
            var confirmationPrompt = new WpfConfirmationPrompt(_dispatcher);
            var uiAutomation = new WindowsUiAutomationAdapter();

            // Reutiliza o motor de RAG já instanciado no startup (evita duplicar instâncias em memória)
            // ou constrói um novo se não tiver sido fornecido
            var brainConfig = BrainProviderFactory.BuildBrainConfig(config);
            IVaultBrainQuery brainQuery = sharedRagEngine ?? _ragEngineFactory(brainConfig);

            var executor = new RemoteCommandExecutor(
                () => configManager.LoadAsync(),
                confirmationPrompt: confirmationPrompt,
                uiAutomation: uiAutomation,
                brainQuery: brainQuery,
                auditLog: auditLog);

            var pollingInterval = TimeSpan.FromSeconds(
                config.RemoteControlPollingIntervalSeconds > 0
                    ? config.RemoteControlPollingIntervalSeconds
                    : 10);

            // interval e nomeado de proposito: o 4o parametro posicional e cursorFilePath.
            var poller = new RemoteCommandPoller(gitHubProvider, executor, auditLog, interval: pollingInterval);
            _ = poller.RunAsync(_remoteAgentCts.Token);
        }
        catch (Exception ex)
        {
            // Falha ao CONFIGURAR o agente remoto (antes mesmo do poller comecar a rodar) -
            // registra pra nao ficar invisivel, mas nao interrompe o resto da bandeja.
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "RemoteAgent",
                "StartupError",
                status: "Failed",
                errorMessage: ex.Message);
        }
    }

    private void OpenSettings()
    {
        _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", "OpenSettings");

        if (_settingsWindow is null)
        {
            _settingsWindow = new OnboardingWindow();
            _settingsWindow.Closed += (_, _) =>
            {
                _ = _ipcClient.ReconnectAsync();
                _ = PollStatusAsync();
                _settingsWindow = null;
            };
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
    }

    private void OpenQuickCapture() => ShowWindow("OpenQuickCapture", () => new QuickCaptureWindow());

    private void OpenChatVault() => ShowWindow("OpenChatVault", () => new ChatVaultWindow());

    private void OpenFlashcardReview() => ShowWindow("OpenFlashcardReview", () => new FlashcardReviewWindow());

    private void OpenVaultStats() => ShowWindow("OpenVaultStats", () => new VaultStatsWindow());

    private void OpenDiagnostics() => ShowWindow("OpenDiagnostics", () => new DiagnosticsWindow());
    private void OpenAllowedApps() => ShowWindow("OpenAllowedApps", () => new AllowedAppsWindow());

    private void ShowWindow(string logAction, Func<Window> factory)
    {
        _ = SynapseActivityLogger.Instance.LogClickAsync("TrayMenu", logAction);

        var window = factory();
        window.Show();
        window.Activate();
    }

    private void Shutdown()
    {
        Dispose();
        Application.Current?.Shutdown();
    }

    private static string TruncateText(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _pollTimer.Stop();
        _iconReaffirmTimer.Stop();
        _notifyIcon.Visible = false;
        var lastIcon = _notifyIcon.Icon;
        _notifyIcon.Dispose();
        IconGenerator.ReleaseIcon(lastIcon);
        try
        {
            _remoteAgentCts.Cancel();
        }
        catch { }
        _ = _ipcClient.DisposeAsync();
    }
}
