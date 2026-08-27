namespace Synapse.Agent.Models;

/// <summary>
/// Representação do resultado de um comando remoto (.synapse/remote/results/{id}.json).
/// </summary>
public sealed record RemoteCommandResult(
    Guid Id,
    DateTimeOffset CompletedAt,
    RemoteCommandStatus Status,
    string Message);
