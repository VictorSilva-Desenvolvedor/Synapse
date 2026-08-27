namespace Synapse.Agent.Models;

/// <summary>
/// Representação de um comando recebido via GitHub Relay (.synapse/remote/commands/{id}.json).
/// </summary>
public sealed record RemoteCommand(
    Guid Id,
    DateTimeOffset CreatedAt,
    RemoteCommandType Type,
    Dictionary<string, string> Payload,
    string RequestedBy);
