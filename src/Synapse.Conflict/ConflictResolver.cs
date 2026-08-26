using Synapse.Core.Ports;

namespace Synapse.Conflict;

/// <summary>
/// Implementação de IConflictResolver (Synapse.Core.Ports), composta pelos dois mergers especializados
/// da Visão Lógica do SAD: ThreeWayMerger (corpo) e FrontmatterMerger (frontmatter).
/// </summary>
public sealed class ConflictResolver : IConflictResolver
{
    private readonly ThreeWayMerger _bodyMerger;
    private readonly FrontmatterMerger _frontmatterMerger;

    public ConflictResolver() : this(new ThreeWayMerger(), new FrontmatterMerger()) { }

    public ConflictResolver(ThreeWayMerger bodyMerger, FrontmatterMerger frontmatterMerger)
    {
        _bodyMerger = bodyMerger;
        _frontmatterMerger = frontmatterMerger;
    }

    public MergeResult TryMergeBody(string baseContent, string localContent, string remoteContent) =>
        _bodyMerger.Merge(baseContent, localContent, remoteContent);

    public MergeResult TryMergeFrontmatter(string baseYaml, string localYaml, string remoteYaml) =>
        _frontmatterMerger.Merge(baseYaml, localYaml, remoteYaml);
}
