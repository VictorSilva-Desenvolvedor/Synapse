using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Synapse.Agent.Models;
using Synapse.Sync.Config;

namespace Synapse.Agent;

/// <summary>
/// Executor seguro de comandos remotos com validação de allowlist e proteção contra path traversal (Fase 1).
/// </summary>
public sealed class RemoteCommandExecutor
{
    private readonly Func<SynapseConfig> _configProvider;
    private readonly ILogger<RemoteCommandExecutor>? _logger;

    public RemoteCommandExecutor(
        SynapseConfig config,
        ILogger<RemoteCommandExecutor>? logger = null)
        : this(() => config, logger)
    {
    }

    public RemoteCommandExecutor(
        Func<SynapseConfig> configProvider,
        ILogger<RemoteCommandExecutor>? logger = null)
    {
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _logger = logger;
    }

    public Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var config = _configProvider();

        // 1. Verificação global do interruptor de segurança
        if (!config.RemoteControlEnabled)
        {
            _logger?.LogWarning("Comando remoto {CommandId} ({Type}) rejeitado: Controle remoto desativado nas configurações.", command.Id, command.Type);
            return Task.FromResult(new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Controle remoto desativado"));
        }

        try
        {
            var result = command.Type switch
            {
                RemoteCommandType.OpenApp => ExecuteOpenApp(command, config),
                RemoteCommandType.OpenNote => ExecuteOpenNote(command, config),
                RemoteCommandType.FocusWindow => ExecuteFocusWindow(command),
                _ => new RemoteCommandResult(
                    command.Id,
                    DateTimeOffset.UtcNow,
                    RemoteCommandStatus.Rejected,
                    $"Tipo de comando '{command.Type}' desconhecido ou não suportado.")
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro inesperado ao executar comando remoto {CommandId} ({Type})", command.Id, command.Type);
            return Task.FromResult(new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Erro interno na execução: {ex.Message}"));
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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
}
