using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.Model;
using Synapse.Core.Ports;

namespace Synapse.Conflict;

/// <summary>
/// Merge de 3 vias do corpo da nota (RF-CONFLICT.1-2), via ADR-015: diff de linhas (DiffPlex) de
/// base-contra-local e de base-contra-remoto, ambos em coordenadas de linha da base. Hunks que não se
/// sobrepõem são aplicados juntos; hunks que tocam a mesma região da base viram conflito (RF-CONFLICT.4).
/// </summary>
public sealed class ThreeWayMerger
{
    private static readonly IChunker LineChunker = new LineChunker();

    private readonly IDiffer _differ;

    public ThreeWayMerger(IDiffer? differ = null) => _differ = differ ?? new Differ();

    public MergeResult Merge(string baseContent, string localContent, string remoteContent)
    {
        var baseLines = SplitLines(baseContent);

        var localHunks = ToHunks(_differ.CreateDiffs(baseContent, localContent, false, false, LineChunker), SplitLines(localContent));
        var remoteHunks = ToHunks(_differ.CreateDiffs(baseContent, remoteContent, false, false, LineChunker), SplitLines(remoteContent));

        if (HunksOverlap(localHunks, remoteHunks))
            return new MergeResult.Unresolvable(localContent, remoteContent);

        var mergedLines = ApplyHunks(baseLines, localHunks, remoteHunks);
        return new MergeResult.Resolved(string.Join('\n', mergedLines));
    }

    private static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n").Split('\n');

    private static List<Hunk> ToHunks(DiffResult diff, string[] newLines) =>
        diff.DiffBlocks
            .Select(block => new Hunk(
                block.DeleteStartA,
                block.DeleteCountA,
                newLines.Skip(block.InsertStartB).Take(block.InsertCountB).ToArray()))
            .ToList();

    private static bool HunksOverlap(IReadOnlyList<Hunk> local, IReadOnlyList<Hunk> remote) =>
        local.Any(a => remote.Any(b => RangesIntersect(a, b)));

    private static bool RangesIntersect(Hunk a, Hunk b)
    {
        var (aStart, aEnd) = AnchorRange(a);
        var (bStart, bEnd) = AnchorRange(b);
        return aStart <= bEnd && bStart <= aEnd;
    }

    // Uma inserção pura (DeleteCount == 0) ainda ocupa um ponto de ancoragem na base: duas inserções no
    // mesmo ponto são tratadas como conflito (mais conservador é mais seguro do que silenciosamente
    // escolher uma ordem arbitrária entre local/remoto - RNF-2).
    private static (int start, int end) AnchorRange(Hunk h) =>
        h.DeleteCount == 0 ? (h.Start, h.Start) : (h.Start, h.Start + h.DeleteCount - 1);

    private static List<string> ApplyHunks(string[] baseLines, List<Hunk> localHunks, List<Hunk> remoteHunks)
    {
        var allHunks = localHunks.Concat(remoteHunks).OrderBy(h => h.Start).ToList();
        var result = new List<string>();
        var cursor = 0;

        foreach (var hunk in allHunks)
        {
            while (cursor < hunk.Start)
            {
                result.Add(baseLines[cursor]);
                cursor++;
            }

            result.AddRange(hunk.InsertedLines);
            cursor = hunk.Start + hunk.DeleteCount;
        }

        while (cursor < baseLines.Length)
        {
            result.Add(baseLines[cursor]);
            cursor++;
        }

        return result;
    }

    private sealed record Hunk(int Start, int DeleteCount, string[] InsertedLines);
}
