namespace Synapse.Rules;

/// <summary>Representação interna de uma regra carregada de .synapse/regras.yaml (RF-RULES.1, SRS Apêndice B).</summary>
internal abstract record RuleDefinition
{
    public sealed record NotaDiaria(string Caminho, string Template) : RuleDefinition;
    public sealed record AutoTag(string PastaOrigem, IReadOnlyList<string> Tags) : RuleDefinition;
    public sealed record MoverPorStatus(string CampoFrontmatter, string Valor, string PastaDestino) : RuleDefinition;

    private RuleDefinition() { }
}
