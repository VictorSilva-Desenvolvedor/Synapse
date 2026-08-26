namespace Synapse.Core.Ports;

/// <summary>
/// Algoritmo de merge de 3 vias (RF-CONFLICT.1-4), puro e sem I/O — testável com strings em memória.
/// </summary>
public interface IConflictResolver
{
    /// <summary>
    /// Síncrono de propósito: função pura (string para resultado), sem I/O. Simplifica os testes
    /// unitários exigidos pela DoD do Backlog (nenhum await necessário no teste).
    /// </summary>
    MergeResult TryMergeBody(string baseContent, string localContent, string remoteContent);

    MergeResult TryMergeFrontmatter(string baseYaml, string localYaml, string remoteYaml);
}

public abstract record MergeResult
{
    public sealed record Resolved(string MergedContent) : MergeResult;
    public sealed record Unresolvable(string LocalContent, string RemoteContent) : MergeResult;

    private MergeResult() { }
}
