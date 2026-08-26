using YamlDotNet.Serialization;

namespace Synapse.Rules;

/// <summary>
/// Parseia .synapse/regras.yaml (RF-RULES.1) nos três tipos de regra documentados no Apêndice B do SRS.
/// Falha rápido e explicitamente (FormatException) em vez de ignorar silenciosamente uma regra mal
/// formada ou de tipo desconhecido - um erro de digitação no YAML deve ser visível, não engolido.
/// </summary>
internal static class RulesYamlParser
{
    public static IReadOnlyList<RuleDefinition> Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return [];

        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<string, object>>(yaml);

        if (root is null || !root.TryGetValue("regras", out var regrasObj) || regrasObj is not List<object> lista)
            return [];

        return lista.Select(ParseRegra).ToList();
    }

    private static RuleDefinition ParseRegra(object item)
    {
        if (item is not Dictionary<object, object> mapa)
            throw new FormatException("Cada item de 'regras' deve ser um mapeamento (tipo, ...).");

        var tipo = GetString(mapa, "tipo") ?? throw new FormatException("Regra sem campo 'tipo'.");

        return tipo switch
        {
            "nota_diaria" => new RuleDefinition.NotaDiaria(
                RequireString(mapa, "caminho", tipo),
                RequireString(mapa, "template", tipo)),

            "auto_tag" => new RuleDefinition.AutoTag(
                RequireString(mapa, "pasta_origem", tipo),
                GetStringList(mapa, "tags")),

            "mover_por_status" => new RuleDefinition.MoverPorStatus(
                RequireString(mapa, "campo_frontmatter", tipo),
                RequireString(mapa, "valor", tipo),
                RequireString(mapa, "pasta_destino", tipo)),

            _ => throw new FormatException($"Tipo de regra desconhecido: '{tipo}'."),
        };
    }

    private static string? GetString(Dictionary<object, object> mapa, string chave) =>
        mapa.TryGetValue(chave, out var valor) ? valor?.ToString() : null;

    private static string RequireString(Dictionary<object, object> mapa, string chave, string tipoRegra) =>
        GetString(mapa, chave) ?? throw new FormatException($"Regra '{tipoRegra}' sem o campo obrigatório '{chave}'.");

    private static IReadOnlyList<string> GetStringList(Dictionary<object, object> mapa, string chave)
    {
        if (!mapa.TryGetValue(chave, out var valor) || valor is not List<object> lista)
            return [];

        return lista.Select(v => v?.ToString() ?? string.Empty).ToList();
    }
}
