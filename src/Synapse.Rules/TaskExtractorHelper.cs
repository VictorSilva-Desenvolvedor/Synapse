using System.Text.RegularExpressions;

namespace Synapse.Rules;

/// <summary>
/// Utilitário para extração de checkboxes de tarefas abertas (- [ ]) de notas Markdown.
/// </summary>
public static class TaskExtractorHelper
{
    private static readonly Regex TaskRegex = new(@"^\s*-\s*\[ \]\s+(.+)$", RegexOptions.Multiline);

    public static IReadOnlyList<string> ExtractOpenTasks(string markdownContent)
    {
        if (string.IsNullOrWhiteSpace(markdownContent)) return [];

        var tasks = new List<string>();
        var matches = TaskRegex.Matches(markdownContent);

        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                tasks.Add(match.Groups[1].Value.Trim());
            }
        }

        return tasks;
    }
}
