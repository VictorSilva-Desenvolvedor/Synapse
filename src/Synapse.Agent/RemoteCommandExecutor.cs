using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Synapse.Agent.Models;
using Synapse.Brain.Ports;
using Synapse.Sync.Config;

namespace Synapse.Agent;

/// <summary>
/// Executor seguro de comandos remotos com validação de allowlist, proteção contra path traversal, confirmação interativa para ações sensíveis e RAG contra o cofre (Fases 1, 2 e 4).
/// </summary>
public sealed class RemoteCommandExecutor
{
    private readonly Func<SynapseConfig> _configProvider;
    private readonly IRemoteConfirmationPrompt? _confirmationPrompt;
    private readonly IUiAutomationAdapter? _uiAutomation;
    private readonly IVaultBrainQuery? _brainQuery;
    private readonly RemoteAuditLog? _auditLog;
    private readonly ILogger<RemoteCommandExecutor>? _logger;

    public RemoteCommandExecutor(
        SynapseConfig config,
        IRemoteConfirmationPrompt? confirmationPrompt = null,
        IUiAutomationAdapter? uiAutomation = null,
        IVaultBrainQuery? brainQuery = null,
        RemoteAuditLog? auditLog = null,
        ILogger<RemoteCommandExecutor>? logger = null)
        : this(() => config, confirmationPrompt, uiAutomation, brainQuery, auditLog, logger)
    {
    }

    public RemoteCommandExecutor(
        Func<SynapseConfig> configProvider,
        IRemoteConfirmationPrompt? confirmationPrompt = null,
        IUiAutomationAdapter? uiAutomation = null,
        IVaultBrainQuery? brainQuery = null,
        RemoteAuditLog? auditLog = null,
        ILogger<RemoteCommandExecutor>? logger = null)
    {
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _confirmationPrompt = confirmationPrompt;
        _uiAutomation = uiAutomation;
        _brainQuery = brainQuery;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var config = _configProvider();

        // 1. Verificação global do interruptor de segurança
        if (!config.RemoteControlEnabled)
        {
            _logger?.LogWarning("Comando remoto {CommandId} ({Type}) rejeitado: Controle remoto desativado nas configurações.", command.Id, command.Type);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Controle remoto desativado");
        }

        try
        {
            return command.Type switch
            {
                RemoteCommandType.OpenApp => ExecuteOpenApp(command, config),
                RemoteCommandType.OpenNote => ExecuteOpenNote(command, config),
                RemoteCommandType.FocusWindow => ExecuteFocusWindow(command),
                RemoteCommandType.TypeText => await ExecuteTypeTextAsync(command, config, ct),
                RemoteCommandType.ClickElement => await ExecuteClickElementAsync(command, config, ct),
                RemoteCommandType.AskVault => await ExecuteAskVaultAsync(command, config, ct),
                _ => new RemoteCommandResult(
                    command.Id,
                    DateTimeOffset.UtcNow,
                    RemoteCommandStatus.Rejected,
                    $"Tipo de comando '{command.Type}' desconhecido ou não suportado.")
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro inesperado ao executar comando remoto {CommandId} ({Type})", command.Id, command.Type);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Erro interno na execução: {ex.Message}");
        }
    }

    private RemoteCommandResult ExecuteOpenApp(RemoteCommand command, SynapseConfig config)
    {
        if (command.Payload == null || !command.Payload.TryGetValue("app", out var appKey) || string.IsNullOrWhiteSpace(appKey))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'app' não informado no payload.");
        }

        // Valida chave simbólica contra a Allowlist configurada (case-insensitive)
        var allowedMatch = config.RemoteAllowedApps
            .FirstOrDefault(kvp => string.Equals(kvp.Key, appKey.Trim(), StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(allowedMatch.Key) || string.IsNullOrWhiteSpace(allowedMatch.Value))
        {
            _logger?.LogWarning("Tentativa de abrir aplicativo não permitido: '{AppKey}'", appKey);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                $"Aplicativo '{appKey}' não está na lista de aplicativos permitidos (RemoteAllowedApps).");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = allowedMatch.Value,
                UseShellExecute = true
            };

            Process.Start(psi);
            _logger?.LogInformation("Aplicativo '{AppKey}' ({Path}) iniciado com sucesso via comando remoto {CommandId}.", appKey, allowedMatch.Value, command.Id);

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Aplicativo '{appKey}' iniciado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Falha ao iniciar processo '{Path}' para o app '{AppKey}'", allowedMatch.Value, appKey);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Falha ao iniciar aplicativo '{appKey}': {ex.Message}");
        }
    }

    private RemoteCommandResult ExecuteOpenNote(RemoteCommand command, SynapseConfig config)
    {
        if (command.Payload == null || !command.Payload.TryGetValue("relativePath", out var relativePath) || string.IsNullOrWhiteSpace(relativePath))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'relativePath' não informado no payload.");
        }

        if (string.IsNullOrWhiteSpace(config.VaultPath))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Cofre local não configurado.");
        }

        var vaultRootFull = Path.GetFullPath(config.VaultPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFullPath = Path.GetFullPath(Path.Combine(vaultRootFull, relativePath));

        // Proteção estrita contra Path Traversal
        var vaultPrefix = vaultRootFull + Path.DirectorySeparatorChar;
        if (!targetFullPath.StartsWith(vaultPrefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetFullPath, vaultRootFull, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Bloqueio de Path Traversal no comando {CommandId}: '{Target}' fora de '{Vault}'", command.Id, targetFullPath, vaultRootFull);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Acesso negado: o caminho da nota ultrapassa o diretório raiz do cofre (Path Traversal bloqueado).");
        }

        if (!File.Exists(targetFullPath))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"A nota '{relativePath}' não foi encontrada no cofre.");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetFullPath,
                UseShellExecute = true
            };

            Process.Start(psi);
            _logger?.LogInformation("Nota '{Path}' aberta com sucesso via comando remoto {CommandId}.", relativePath, command.Id);

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Nota '{relativePath}' aberta com sucesso.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Falha ao abrir nota '{Path}'", targetFullPath);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Falha ao abrir nota '{relativePath}': {ex.Message}");
        }
    }

    private RemoteCommandResult ExecuteFocusWindow(RemoteCommand command)
    {
        if (command.Payload == null || !command.Payload.TryGetValue("processName", out var processName) || string.IsNullOrWhiteSpace(processName))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'processName' não informado no payload.");
        }

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        try
        {
            var processes = Process.GetProcessesByName(cleanName);
            if (processes.Length == 0)
            {
                return new RemoteCommandResult(
                    command.Id,
                    DateTimeOffset.UtcNow,
                    RemoteCommandStatus.Success,
                    $"Processo '{cleanName}' não está em execução no momento.");
            }

            var focused = false;
            foreach (var proc in processes)
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                    }
                    focused = true;
                    break;
                }
            }

            var msg = focused
                ? $"Janela do processo '{cleanName}' trazida para primeiro plano."
                : $"Processo '{cleanName}' encontrado, mas sem janela visível para focar.";

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                msg);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Falha ao focar janela do processo '{ProcessName}'", cleanName);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Não foi possível focar a janela do processo '{cleanName}': {ex.Message}");
        }
    }

    private async Task<RemoteCommandResult> ExecuteTypeTextAsync(
        RemoteCommand command,
        SynapseConfig config,
        CancellationToken ct)
    {
        if (command.Payload == null ||
            !command.Payload.TryGetValue("processName", out var processName) ||
            string.IsNullOrWhiteSpace(processName) ||
            !command.Payload.TryGetValue("text", out var text) ||
            text is null)
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetros 'processName' e 'text' são obrigatórios para o comando TypeText.");
        }

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        // 1. Validação de Allowlist (reaproveitada de RemoteAllowedApps)
        if (!IsProcessAllowed(processName, config))
        {
            _logger?.LogWarning("Tentativa de digitação em processo não permitido: '{ProcessName}'", processName);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                $"Processo '{processName}' não está na lista de aplicativos permitidos (RemoteAllowedApps).");
        }

        // 2. Confirmação Humana Obrigatória
        if (_confirmationPrompt == null)
        {
            _logger?.LogWarning("Comando sensível TypeText rejeitado: IRemoteConfirmationPrompt não configurado.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada: nenhum mecanismo de confirmação interativa configurado.");
        }

        var timeout = TimeSpan.FromSeconds(
            config.RemoteConfirmationTimeoutSeconds > 0
                ? config.RemoteConfirmationTimeoutSeconds
                : 30);

        var confirmed = await _confirmationPrompt.ConfirmAsync(command, timeout, ct);
        if (!confirmed)
        {
            _logger?.LogInformation("Comando sensível TypeText ({CommandId}) negado ou expirado pelo usuário.", command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada ou não confirmada pelo usuário.");
        }

        // 3. Execução via UI Automation Adapter
        if (_uiAutomation == null)
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                "Adaptador de UI Automation não configurado.");
        }

        var success = _uiAutomation.TrySendText(cleanName, text);
        if (success)
        {
            _logger?.LogInformation("Texto digitado com sucesso no processo '{ProcessName}' via comando {CommandId}.", cleanName, command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Texto digitado com sucesso no processo '{cleanName}'.");
        }

        return new RemoteCommandResult(
            command.Id,
            DateTimeOffset.UtcNow,
            RemoteCommandStatus.Failed,
            $"Falha ao digitar texto no processo '{cleanName}' (janela não encontrada ou erro no envio).");
    }

    private async Task<RemoteCommandResult> ExecuteClickElementAsync(
        RemoteCommand command,
        SynapseConfig config,
        CancellationToken ct)
    {
        if (command.Payload == null ||
            !command.Payload.TryGetValue("processName", out var processName) ||
            string.IsNullOrWhiteSpace(processName) ||
            !command.Payload.TryGetValue("elementName", out var elementName) ||
            string.IsNullOrWhiteSpace(elementName))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetros 'processName' e 'elementName' são obrigatórios para o comando ClickElement.");
        }

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        // 1. Validação de Allowlist (reaproveitada de RemoteAllowedApps)
        if (!IsProcessAllowed(processName, config))
        {
            _logger?.LogWarning("Tentativa de clique em processo não permitido: '{ProcessName}'", processName);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                $"Processo '{processName}' não está na lista de aplicativos permitidos (RemoteAllowedApps).");
        }

        // 2. Confirmação Humana Obrigatória
        if (_confirmationPrompt == null)
        {
            _logger?.LogWarning("Comando sensível ClickElement rejeitado: IRemoteConfirmationPrompt não configurado.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada: nenhum mecanismo de confirmação interativa configurado.");
        }

        var timeout = TimeSpan.FromSeconds(
            config.RemoteConfirmationTimeoutSeconds > 0
                ? config.RemoteConfirmationTimeoutSeconds
                : 30);

        var confirmed = await _confirmationPrompt.ConfirmAsync(command, timeout, ct);
        if (!confirmed)
        {
            _logger?.LogInformation("Comando sensível ClickElement ({CommandId}) negado ou expirado pelo usuário.", command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada ou não confirmada pelo usuário.");
        }

        // 3. Execução via UI Automation Adapter
        if (_uiAutomation == null)
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                "Adaptador de UI Automation não configurado.");
        }

        var success = _uiAutomation.TryClickElement(cleanName, elementName);
        if (success)
        {
            _logger?.LogInformation("Elemento '{ElementName}' clicado com sucesso no processo '{ProcessName}' via comando {CommandId}.", elementName, cleanName, command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Elemento '{elementName}' clicado com sucesso no processo '{cleanName}'.");
        }

        return new RemoteCommandResult(
            command.Id,
            DateTimeOffset.UtcNow,
            RemoteCommandStatus.Failed,
            $"Elemento '{elementName}' não foi encontrado ou não pôde ser clicado no processo '{cleanName}'.");
    }

    private async Task<RemoteCommandResult> ExecuteAskVaultAsync(
        RemoteCommand command,
        SynapseConfig config,
        CancellationToken ct)
    {
        if (command.Payload == null ||
            !command.Payload.TryGetValue("question", out var question) ||
            string.IsNullOrWhiteSpace(question))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'question' não informado no payload.");
        }

        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
        {
            _logger?.LogWarning("Comando AskVault rejeitado: Chave da API Gemini não configurada.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Chave da API Gemini não configurada nas configurações do Synapse.");
        }

        if (_brainQuery == null)
        {
            _logger?.LogWarning("Comando AskVault rejeitado: IVaultBrainQuery não configurado.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Consulta ao cofre não configurada nesta sessão.");
        }

        if (string.IsNullOrWhiteSpace(config.VaultPath) || !Directory.Exists(config.VaultPath))
        {
            _logger?.LogWarning("Comando AskVault rejeitado: Cofre local não configurado ou inexistente em '{VaultPath}'", config.VaultPath);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Cofre local não configurado ou diretório não encontrado.");
        }

        try
        {
            _logger?.LogInformation("Executando consulta RAG no cofre para a pergunta: \"{Question}\"", question);
            var answer = await _brainQuery.AskVaultAsync(question.Trim(), config.VaultPath, ct);

            var answerMessage = answer.Answer;
            if (answer.Sources != null && answer.Sources.Count > 0)
            {
                var sourcesFormatted = string.Join(", ", answer.Sources.Select(s => string.IsNullOrWhiteSpace(s.Title) ? $"[[{Path.GetFileNameWithoutExtension(s.RelativePath)}]]" : $"[[{s.Title}]]"));
                answerMessage += $"\n\nFontes: {sourcesFormatted}";
            }

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                answerMessage);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao processar consulta AskVault para o comando {CommandId}", command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Falha ao consultar o cofre com a IA: {ex.Message}");
        }
    }

    private static bool IsProcessAllowed(string processName, SynapseConfig config)
    {
        if (config.RemoteAllowedApps == null || config.RemoteAllowedApps.Count == 0)
        {
            return false;
        }

        var cleanProcess = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        foreach (var kvp in config.RemoteAllowedApps)
        {
            if (string.Equals(kvp.Key.Trim(), cleanProcess, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var valClean = Path.GetFileNameWithoutExtension(kvp.Value?.Trim() ?? string.Empty);
            if (string.Equals(valClean, cleanProcess, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Value?.Trim(), processName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
}
