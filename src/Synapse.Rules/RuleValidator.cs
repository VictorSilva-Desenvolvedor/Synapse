using YamlDotNet.RepresentationModel;

namespace Synapse.Rules;

public sealed record RuleValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Validador de integridade e linter para arquivos de regras YAML (.synapse/regras.yaml) (US-RULES.8).
/// </summary>
public static class RuleValidator
{
    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "criar_nota", "create_note",
        "adicionar_tags", "add_tags",
        "mover_nota", "move_note",
        "adicionar_ao_fim", "append_content",
        "adicionar_ao_inicio", "prepend_content",
        "extrair_tarefas", "extract_tasks",
        "renomear_nota", "rename_note"
    };

    public static RuleValidationResult Validate(string yamlContent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            return new RuleValidationResult(true, [], ["Arquivo de regras vazio."]);
        }

        try
        {
            using var reader = new StringReader(yamlContent);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                errors.Add("O arquivo de regras deve conter um mapeamento YAML raiz válido.");
                return new RuleValidationResult(false, errors, warnings);
            }

            var regrasKey = new YamlScalarNode("regras");
            if (root.Children.TryGetValue(regrasKey, out var regrasNode) && regrasNode is YamlSequenceNode regrasSeq)
            {
                var ruleIndex = 1;
                foreach (var item in regrasSeq)
                {
                    if (item is YamlMappingNode ruleMap)
                    {
                        ValidateSingleRule(ruleMap, ruleIndex, errors, warnings);
                    }
                    else
                    {
                        errors.Add($"Regra #{ruleIndex} precisa ser um mapeamento YAML.");
                    }
                    ruleIndex++;
                }
            }
            else
            {
                warnings.Add("Nenhuma sequência de 'regras' encontrada no arquivo YAML.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Erro de sintaxe YAML: {ex.Message}");
        }

        return new RuleValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void ValidateSingleRule(
        YamlMappingNode rule,
        int index,
        List<string> errors,
        List<string> warnings)
    {
        var nomeKey = new YamlScalarNode("nome");
        if (!rule.Children.ContainsKey(nomeKey))
        {
            warnings.Add($"Regra #{index} não possui um campo 'nome' descritivo.");
        }

        var acoesKey = new YamlScalarNode("acoes");
        if (rule.Children.TryGetValue(acoesKey, out var acoesNode) && acoesNode is YamlSequenceNode acoesSeq)
        {
            if (acoesSeq.Children.Count == 0)
            {
                warnings.Add($"Regra #{index} não possui nenhuma ação definida.");
            }

            foreach (var acaoNode in acoesSeq)
            {
                if (acaoNode is YamlMappingNode acaoMap)
                {
                    var tipoKey = new YamlScalarNode("tipo");
                    if (acaoMap.Children.TryGetValue(tipoKey, out var tipoVal) && tipoVal is YamlScalarNode tipoScalar)
                    {
                        var tipoStr = tipoScalar.Value ?? "";
                        if (!ValidActions.Contains(tipoStr))
                        {
                            errors.Add($"Regra #{index}: Ação desconhecida '{tipoStr}'. Ações suportadas: {string.Join(", ", ValidActions.Take(7))}");
                        }
                    }
                    else
                    {
                        errors.Add($"Regra #{index}: Ação sem o campo obrigatório 'tipo'.");
                    }
                }
            }
        }
        else
        {
            errors.Add($"Regra #{index} não possui o campo 'acoes'.");
        }
    }
}
