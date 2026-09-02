namespace Synapse.Search;

/// <summary>
/// Contrato para o monitor de alterações do sistema de arquivos do cofre,
/// responsável por manter o índice de busca SQLite FTS5 sincronizado em tempo real.
/// </summary>
public interface IVaultIndexWatcher : IDisposable
{
    /// <summary>
    /// Inicia o monitoramento de arquivos *.md no cofre especificado.
    /// </summary>
    /// <param name="vaultRootPath">Caminho absoluto da raiz do cofre.</param>
    void Start(string vaultRootPath);

    /// <summary>
    /// Interrompe o monitoramento e cancela tarefas de debounce pendentes.
    /// </summary>
    void Stop();

    /// <summary>
    /// Indica se o monitor está atualmente ativo.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Indica se o monitor sofreu uma falha crítica (ex: estouro de buffer do FileSystemWatcher)
    /// e não pode mais garantir a integridade em tempo real do índice.
    /// </summary>
    bool HasFailed { get; }

    /// <summary>
    /// Evento disparado quando o FileSystemWatcher sofre um erro ou falha no sistema de arquivos.
    /// </summary>
    event EventHandler<Exception>? ErrorOccurred;
}
