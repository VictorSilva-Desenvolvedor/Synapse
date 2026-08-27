namespace Synapse.Sync;

/// <summary>
/// Separa o frontmatter YAML (delimitado por "---") do corpo de uma nota Markdown, e recompõe os dois -
/// RF-CONFLICT.3 exige que o frontmatter seja parseado isoladamente do corpo antes do merge de texto.
/// </summary>
public static class NoteContentSplitter
{
    private const string Delimiter = "---";

    public static (string Frontmatter, string Body) Split(string rawContent)
    {
        var normalized = rawContent.Replace("\r\n", "\n");
        if (!normalized.StartsWith(Delimiter + "\n", StringComparison.Ordinal))
            return (string.Empty, rawContent);

        var closingIndex = normalized.IndexOf("\n" + Delimiter, Delimiter.Length + 1, StringComparison.Ordinal);
        if (closingIndex < 0)
            return (string.Empty, rawContent);

        var frontmatter = normalized[(Delimiter.Length + 1)..closingIndex];
        var bodyStart = closingIndex + 1 + Delimiter.Length;
        if (bodyStart < normalized.Length && normalized[bodyStart] == '\n')
            bodyStart++;

        var body = normalized[bodyStart..];
        return (frontmatter, body);
    }

    public static string Join(string frontmatter, string body) =>
        string.IsNullOrEmpty(frontmatter) ? body : $"{Delimiter}\n{frontmatter}\n{Delimiter}\n{body}";
}
