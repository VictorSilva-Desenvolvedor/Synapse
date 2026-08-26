namespace Synapse.Core.Ports;

/// <summary>
/// Abstrai o sistema de arquivos local - porta nova em relação à API original (mesma razão de
/// IVaultWatcher, ver API - Synapse.md seção 2.5): sem ela, SyncQueueProcessor dependeria diretamente
/// de System.IO, dificultando testar sem disco real (Plano de Testes seção 4, "IFileSystem em memória"
/// para os testes de sistema simulados).
/// </summary>
public interface IFileSystem
{
    Task<bool> ExistsAsync(string path, CancellationToken ct);
    Task<string> ReadAllTextAsync(string path, CancellationToken ct);
    Task WriteAllTextAsync(string path, string content, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}
