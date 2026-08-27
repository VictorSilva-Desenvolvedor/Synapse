using YamlDotNet.Serialization;

namespace Synapse.Rules;

/// <summary>
/// Utilitário para injeção de tags em frontmatter YAML de notas Markdown (RF-RULES.3 / US-RULES.3).
/// Preserva campos existentes do frontmatter e todo o corpo Markdown da nota.
/// </summary>
public static class FrontmatterTagApplier
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    public static string ApplyTags(string rawContent, IReadOnlyList<string> tagsToAdd)
    {
        if (tagsToAdd == null || tagsToAdd.Count == 0)
        {
            return rawContent;
        }

        var (frontmatterYaml, body) = SplitFrontmatter(rawContent);

        var frontmatterDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(frontmatterYaml))
        {
            try
            {
                var parsed = Deserializer.Deserialize<Dictionary<string, object>>(frontmatterYaml);
                if (parsed != null)
                {
                    frontmatterDict = new Dictionary<string, object>(parsed, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Se o YAML estiver malformado, não sobrescreve
                return rawContent;
            }
        }

        var currentTags = new List<string>();

        if (frontmatterDict.TryGetValue("tags", out var existingTagsObj) && existingTagsObj != null)
        {
            if (existingTagsObj is IEnumerable<object> objList)
            {
                currentTags.AddRange(objList.Select(o => o?.ToString()?.Trim() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)));
            }
            else if (existingTagsObj is string singleTagStr)
            {
                currentTags.Add(singleTagStr.Trim());
            }
        }

        foreach (var tag in tagsToAdd)
        {
            var cleanTag = tag.TrimStart('#').Trim();
            if (!string.IsNullOrEmpty(cleanTag) && !currentTags.Contains(cleanTag, StringComparer.OrdinalIgnoreCase))
            {
                currentTags.Add(cleanTag);
            }
        }

        frontmatterDict["tags"] = currentTags;

        var updatedFrontmatter = Serializer.Serialize(frontmatterDict).Trim();
        return $"---\n{updatedFrontmatter}\n---\n{body}";
    }

    private static (string Frontmatter, string Body) SplitFrontmatter(string rawContent)
    {
        const string delimiter = "---";
        var normalized = rawContent.Replace("\r\n", "\n");

        if (!normalized.StartsWith(delimiter + "\n", StringComparison.Ordinal) && normalized != delimiter)
        {
            return (string.Empty, rawContent);
        }

        var closingIndex = normalized.IndexOf("\n" + delimiter, delimiter.Length + 1, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            return (string.Empty, rawContent);
        }

        var frontmatter = normalized[(delimiter.Length + 1)..closingIndex];
        var bodyStart = closingIndex + 1 + delimiter.Length;
        if (bodyStart < normalized.Length && normalized[bodyStart] == '\n')
        {
            bodyStart++;
        }

        var body = normalized[bodyStart..];
        return (frontmatter, body);
    }
}
