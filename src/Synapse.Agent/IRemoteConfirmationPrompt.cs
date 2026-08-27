using Synapse.Agent.Models;

namespace Synapse.Agent;

/// <summary>
/// Mecanismo de confirmação interativa para ações sensíveis de controle remoto.
/// </summary>
public interface IRemoteConfirmationPrompt
{
    Task<bool> ConfirmAsync(RemoteCommand command, TimeSpan timeout, CancellationToken ct = default);
}
