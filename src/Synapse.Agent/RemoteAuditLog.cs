using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Synapse.Agent.Models;

namespace Synapse.Agent;

/// <summary>
/// Trilha de auditoria append-only para comandos remotos do Synapse Agent (.synapse/remote-audit.log).
/// </summary>
public sealed class RemoteAuditLog
{
    private readonly string? _vaultRoot;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RemoteAuditLog(string? vaultRoot, ILogger? logger = null)
    {
        _vaultRoot = vaultRoot;
        _logger = logger;
    }

    public Task LogEntryAsync(
        RemoteCommand command,
        RemoteCommandResult result,
        CancellationToken ct = default)
    {
        return LogEntryAsync(command, result, confirmationStatus: null, ct);
    }

    public async Task LogEntryAsync(
        RemoteCommand command,
        RemoteCommandResult result,
        string? confirmationStatus = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        var logEntry = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            CommandId = command.Id,
            CreatedAt = command.CreatedAt,
            Type = command.Type.ToString(),
            RequestedBy = command.RequestedBy,
            Payload = command.Payload,
            Status = result.Status.ToString(),
            Confirmation = confirmationStatus,
            CompletedAt = result.CompletedAt,
            Message = result.Message
        };

        var jsonLine = JsonSerializer.Serialize(logEntry);

        // 1. Grava no logger da aplicação
        _logger?.LogInformation(
            "AUDIT REMOTE: [{Status}] Command={CommandId} Type={Type} Confirmation={Confirmation} By={RequestedBy} Msg={Message}",
            result.Status,
            command.Id,
            command.Type,
            confirmationStatus ?? "N/A",
            command.RequestedBy,
            result.Message);

        // 2. Grava de forma append-only no arquivo de log do cofre (.synapse/remote-audit.log)
        if (!string.IsNullOrWhiteSpace(_vaultRoot) && Directory.Exists(_vaultRoot))
        {
            var auditDir = Path.Combine(_vaultRoot, ".synapse");
            var auditFile = Path.Combine(auditDir, "remote-audit.log");

            await _lock.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(auditDir);
                await File.AppendAllTextAsync(auditFile, jsonLine + Environment.NewLine, Encoding.UTF8, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Falha ao gravar trilha de auditoria remota no arquivo '{Path}'", auditFile);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
