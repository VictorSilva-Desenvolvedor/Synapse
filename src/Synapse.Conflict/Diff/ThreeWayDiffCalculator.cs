using DiffPlex;
using DiffPlex.Chunkers;

namespace Synapse.Conflict.Diff;

public enum DiffBlockType
{
    Unchanged,
    LocalChange,
    RemoteChange,
    Conflict
}

public enum BlockResolutionChoice
{
    Local,
    Remote,
    Both,
    Base
}

public sealed class DiffBlock
{
    public int Index { get; set; }
    public DiffBlockType Type { get; set; }
    public string BaseText { get; set; } = string.Empty;
    public string LocalText { get; set; } = string.Empty;
    public string RemoteText { get; set; } = string.Empty;
    public BlockResolutionChoice Choice { get; set; } = BlockResolutionChoice.Local;
    public string? CustomText { get; set; }
}

public sealed class ThreeWayDiffCalculator
{
    private static readonly IChunker LineChunker = new LineChunker();
    private readonly IDiffer _differ;

    public ThreeWayDiffCalculator(IDiffer? differ = null)
    {
        _differ = differ ?? new Differ();
    }

    public IReadOnlyList<DiffBlock> Calculate(string baseContent, string localContent, string remoteContent)
    {
        var baseLines = SplitLines(baseContent);
        var localLines = SplitLines(localContent);
        var remoteLines = SplitLines(remoteContent);

        var localDiff = _differ.CreateDiffs(baseContent, localContent, false, false, LineChunker);
        var remoteDiff = _differ.CreateDiffs(baseContent, remoteContent, false, false, LineChunker);

        var blocks = new List<DiffBlock>();
        var blockIdx = 0;

        // Se local e remoto forem idênticos
        if (string.Equals(localContent, remoteContent, StringComparison.Ordinal))
        {
            blocks.Add(new DiffBlock
            {
                Index = 0,
                Type = DiffBlockType.Unchanged,
                BaseText = baseContent,
                LocalText = localContent,
                RemoteText = remoteContent,
                Choice = BlockResolutionChoice.Local
            });
            return blocks;
        }

        // Construção simples e robusta dos blocos baseando-se em hunks ou seções
        var maxLines = Math.Max(baseLines.Length, Math.Max(localLines.Length, remoteLines.Length));

        // Segmentação por blocos de conflito
        var bIdx = 0;
        var lIdx = 0;
        var rIdx = 0;

        while (bIdx < baseLines.Length || lIdx < localLines.Length || rIdx < remoteLines.Length)
        {
            var bLine = bIdx < baseLines.Length ? baseLines[bIdx] : null;
            var lLine = lIdx < localLines.Length ? localLines[lIdx] : null;
            var rLine = rIdx < remoteLines.Length ? remoteLines[rIdx] : null;

            if (lLine == rLine && (bLine == lLine || bLine == null))
            {
                // Ambos iguais
                blocks.Add(new DiffBlock
                {
                    Index = blockIdx++,
                    Type = DiffBlockType.Unchanged,
                    BaseText = bLine ?? string.Empty,
                    LocalText = lLine ?? string.Empty,
                    RemoteText = rLine ?? string.Empty,
                    Choice = BlockResolutionChoice.Local
                });
                if (bIdx < baseLines.Length) bIdx++;
                if (lIdx < localLines.Length) lIdx++;
                if (rIdx < remoteLines.Length) rIdx++;
            }
            else if (lLine == bLine && rLine != bLine)
            {
                // Apenas remoto alterou
                blocks.Add(new DiffBlock
                {
                    Index = blockIdx++,
                    Type = DiffBlockType.RemoteChange,
                    BaseText = bLine ?? string.Empty,
                    LocalText = lLine ?? string.Empty,
                    RemoteText = rLine ?? string.Empty,
                    Choice = BlockResolutionChoice.Remote
                });
                if (bIdx < baseLines.Length) bIdx++;
                if (lIdx < localLines.Length) lIdx++;
                if (rIdx < remoteLines.Length) rIdx++;
            }
            else if (rLine == bLine && lLine != bLine)
            {
                // Apenas local alterou
                blocks.Add(new DiffBlock
                {
                    Index = blockIdx++,
                    Type = DiffBlockType.LocalChange,
                    BaseText = bLine ?? string.Empty,
                    LocalText = lLine ?? string.Empty,
                    RemoteText = rLine ?? string.Empty,
                    Choice = BlockResolutionChoice.Local
                });
                if (bIdx < baseLines.Length) bIdx++;
                if (lIdx < localLines.Length) lIdx++;
                if (rIdx < remoteLines.Length) rIdx++;
            }
            else
            {
                // Conflito direto
                blocks.Add(new DiffBlock
                {
                    Index = blockIdx++,
                    Type = DiffBlockType.Conflict,
                    BaseText = bLine ?? string.Empty,
                    LocalText = lLine ?? string.Empty,
                    RemoteText = rLine ?? string.Empty,
                    Choice = BlockResolutionChoice.Local
                });
                if (bIdx < baseLines.Length) bIdx++;
                if (lIdx < localLines.Length) lIdx++;
                if (rIdx < remoteLines.Length) rIdx++;
            }
        }

        return blocks;
    }

    public static string BuildMergedResult(IReadOnlyList<DiffBlock> blocks)
    {
        var lines = new List<string>();

        foreach (var block in blocks)
        {
            if (block.CustomText != null)
            {
                lines.Add(block.CustomText);
                continue;
            }

            switch (block.Choice)
            {
                case BlockResolutionChoice.Local:
                    if (!string.IsNullOrEmpty(block.LocalText)) lines.Add(block.LocalText);
                    break;
                case BlockResolutionChoice.Remote:
                    if (!string.IsNullOrEmpty(block.RemoteText)) lines.Add(block.RemoteText);
                    break;
                case BlockResolutionChoice.Base:
                    if (!string.IsNullOrEmpty(block.BaseText)) lines.Add(block.BaseText);
                    break;
                case BlockResolutionChoice.Both:
                    if (!string.IsNullOrEmpty(block.LocalText)) lines.Add(block.LocalText);
                    if (!string.IsNullOrEmpty(block.RemoteText) && block.RemoteText != block.LocalText) lines.Add(block.RemoteText);
                    break;
            }
        }

        return string.Join("\n", lines);
    }

    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Split('\n');
}
