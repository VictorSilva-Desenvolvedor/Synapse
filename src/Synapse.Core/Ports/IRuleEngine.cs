namespace Synapse.Core.Ports;

/// <summary>
/// Motor de automação (RF-RULES.1-5).
/// </summary>
public interface IRuleEngine
{
    Task LoadRulesAsync(string rulesFilePath, CancellationToken ct);
    Task<IReadOnlyList<RuleAction>> EvaluateAsync(NoteContext note, CancellationToken ct);
}

public sealed record NoteContext(
    string RelativePath,
    string FrontmatterYaml,
    DateTimeOffset CreatedAt);

/// <summary>
/// Contrato de segurança (RF-RULES.5): deliberadamente não tem um caso de exclusão de nota.
/// Nenhuma regra pode apagar conteúdo — restrição no próprio tipo, não apenas convenção documental.
/// </summary>
public abstract record RuleAction
{
    public sealed record CreateNote(string TargetPath, string TemplatePath) : RuleAction;
    public sealed record AddTags(string TargetPath, IReadOnlyList<string> Tags) : RuleAction;
    public sealed record MoveNote(string FromPath, string ToPath) : RuleAction;
    public sealed record AppendContent(string TargetPath, string Content) : RuleAction;
    public sealed record PrependContent(string TargetPath, string Content) : RuleAction;
    public sealed record ExtractTasks(string SourcePath, string TargetDailyNotePath) : RuleAction;
    public sealed record RenameNote(string FromPath, string Pattern) : RuleAction;

    private RuleAction() { }
}
