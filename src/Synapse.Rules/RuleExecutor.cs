using System.Globalization;
using System.Text.RegularExpressions;
using Synapse.Core.Ports;

namespace Synapse.Rules;

/// <summary>
/// Executor de ações geradas pelo motor de regras (RF-RULES.2-5, V6.1).
/// Garante que nenhuma operação apague conteúdo ou corrompa notas (US-RULES.5).
/// </summary>
public sealed class RuleExecutor
{
    private readonly IFileSystem _fileSystem;
    private readonly string _vaultRootPath;
    private readonly TimeProvider _timeProvider;

    public RuleExecutor(IFileSystem fileSystem, string vaultRootPath, TimeProvider? timeProvider = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _vaultRootPath = vaultRootPath ?? throw new ArgumentNullException(nameof(vaultRootPath));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ExecuteActionAsync(RuleAction action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action)
        {
            case RuleAction.CreateNote createNote:
                await ExecuteCreateNoteAsync(createNote, ct);
                break;

            case RuleAction.AddTags addTags:
                await ExecuteAddTagsAsync(addTags, ct);
                break;

            case RuleAction.MoveNote moveNote:
                await ExecuteMoveNoteAsync(moveNote, ct);
                break;

            case RuleAction.AppendContent appendContent:
                await ExecuteAppendContentAsync(appendContent, ct);
                break;

            case RuleAction.PrependContent prependContent:
                await ExecutePrependContentAsync(prependContent, ct);
                break;

            case RuleAction.ExtractTasks extractTasks:
                await ExecuteExtractTasksAsync(extractTasks, ct);
                break;

            case RuleAction.RenameNote renameNote:
                await ExecuteRenameNoteAsync(renameNote, ct);
                break;
        }
    }

    private async Task ExecuteCreateNoteAsync(RuleAction.CreateNote action, CancellationToken ct)
    {
        var fullPath = Path.Combine(_vaultRootPath, action.TargetPath);

        if (await _fileSystem.ExistsAsync(fullPath, ct))
        {
            return;
        }

        var agora = _timeProvider.GetUtcNow();
        var content = ResolvePlaceholders(action.TemplatePath, agora);

        await _fileSystem.WriteAllTextAsync(fullPath, content, ct);
    }

    private async Task ExecuteAddTagsAsync(RuleAction.AddTags action, CancellationToken ct)
    {
        var fullPath = Path.Combine(_vaultRootPath, action.TargetPath);

        if (!await _fileSystem.ExistsAsync(fullPath, ct))
        {
            return;
        }

        var content = await _fileSystem.ReadAllTextAsync(fullPath, ct);
        var updated = FrontmatterTagApplier.ApplyTags(content, action.Tags);

        if (updated != content)
        {
            await _fileSystem.WriteAllTextAsync(fullPath, updated, ct);
        }
    }

    private async Task ExecuteMoveNoteAsync(RuleAction.MoveNote action, CancellationToken ct)
    {
        var fromFullPath = Path.Combine(_vaultRootPath, action.FromPath);
        var toFullPath = Path.Combine(_vaultRootPath, action.ToPath);

        if (!await _fileSystem.ExistsAsync(fromFullPath, ct))
        {
            return;
        }

        if (await _fileSystem.ExistsAsync(toFullPath, ct))
        {
            return; // Destino já existe; não sobrescreve (RNF-2)
        }

        var content = await _fileSystem.ReadAllTextAsync(fromFullPath, ct);
        await _fileSystem.WriteAllTextAsync(toFullPath, content, ct);
        await _fileSystem.DeleteAsync(fromFullPath, ct);
    }

    private async Task ExecuteAppendContentAsync(RuleAction.AppendContent action, CancellationToken ct)
    {
        var fullPath = Path.Combine(_vaultRootPath, action.TargetPath);
        if (!await _fileSystem.ExistsAsync(fullPath, ct)) return;

        var content = await _fileSystem.ReadAllTextAsync(fullPath, ct);
        var textToAppend = ResolvePlaceholders(action.Content, _timeProvider.GetUtcNow());

        if (content.Contains(textToAppend.Trim())) return; // Evita duplicação exata

        var updated = content.TrimEnd() + "\n\n" + textToAppend.Trim() + "\n";
        await _fileSystem.WriteAllTextAsync(fullPath, updated, ct);
    }

    private async Task ExecutePrependContentAsync(RuleAction.PrependContent action, CancellationToken ct)
    {
        var fullPath = Path.Combine(_vaultRootPath, action.TargetPath);
        if (!await _fileSystem.ExistsAsync(fullPath, ct)) return;

        var content = await _fileSystem.ReadAllTextAsync(fullPath, ct);
        var textToPrepend = ResolvePlaceholders(action.Content, _timeProvider.GetUtcNow());

        string updated;
        if (content.StartsWith("---"))
        {
            var closingIdx = content.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (closingIdx >= 0)
            {
                var afterFrontmatter = closingIdx + 4;
                updated = content[..afterFrontmatter] + "\n\n" + textToPrepend.Trim() + "\n" + content[afterFrontmatter..].TrimStart();
            }
            else
            {
                updated = textToPrepend.Trim() + "\n\n" + content;
            }
        }
        else
        {
            updated = textToPrepend.Trim() + "\n\n" + content;
        }

        await _fileSystem.WriteAllTextAsync(fullPath, updated, ct);
    }

    private async Task ExecuteExtractTasksAsync(RuleAction.ExtractTasks action, CancellationToken ct)
    {
        var sourceFullPath = Path.Combine(_vaultRootPath, action.SourcePath);
        if (!await _fileSystem.ExistsAsync(sourceFullPath, ct)) return;

        var sourceContent = await _fileSystem.ReadAllTextAsync(sourceFullPath, ct);
        var tasks = TaskExtractorHelper.ExtractOpenTasks(sourceContent);

        if (tasks.Count == 0) return;

        var targetFullPath = Path.Combine(_vaultRootPath, action.TargetDailyNotePath);
        var targetContent = await _fileSystem.ExistsAsync(targetFullPath, ct)
            ? await _fileSystem.ReadAllTextAsync(targetFullPath, ct)
            : $"# Nota Diária\n\n";

        var sb = new System.Text.StringBuilder(targetContent.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"## Tarefas Coletadas de [[{Path.GetFileNameWithoutExtension(action.SourcePath)}]]");
        foreach (var task in tasks)
        {
            sb.AppendLine($"- [ ] {task}");
        }

        await _fileSystem.WriteAllTextAsync(targetFullPath, sb.ToString(), ct);
    }

    private async Task ExecuteRenameNoteAsync(RuleAction.RenameNote action, CancellationToken ct)
    {
        var fromFullPath = Path.Combine(_vaultRootPath, action.FromPath);
        if (!await _fileSystem.ExistsAsync(fromFullPath, ct)) return;

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(action.FromPath);
        var newName = ResolvePlaceholders(action.Pattern, _timeProvider.GetUtcNow())
            .Replace("{{title}}", fileNameWithoutExt)
            .Replace("{{filename}}", fileNameWithoutExt);

        if (!newName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            newName += ".md";
        }

        var dir = Path.GetDirectoryName(fromFullPath) ?? _vaultRootPath;
        var toFullPath = Path.Combine(dir, newName);

        if (fromFullPath.Equals(toFullPath, StringComparison.OrdinalIgnoreCase) || await _fileSystem.ExistsAsync(toFullPath, ct))
        {
            return;
        }

        var content = await _fileSystem.ReadAllTextAsync(fromFullPath, ct);
        await _fileSystem.WriteAllTextAsync(toFullPath, content, ct);
        await _fileSystem.DeleteAsync(fromFullPath, ct);
    }

    public static string ResolvePlaceholders(string template, DateTimeOffset date)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var resolved = template
            .Replace("{{date}}", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{{time}}", date.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            .Replace("{{datetime}}", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        resolved = Regex.Replace(
            resolved,
            @"\{\{data:([^}]+)\}\}",
            match =>
            {
                var format = match.Groups[1].Value;
                try
                {
                    return date.ToString(format, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    return match.Value;
                }
            });

        return resolved;
    }
}
