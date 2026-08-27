using Shouldly;
using Synapse.Rules;

namespace Synapse.Tests.Rules;

public class RuleValidatorTests
{
    [Fact]
    public void Validate_WhenValidYaml_ShouldReturnIsValidTrue()
    {
        var yaml = @"
regras:
  - nome: Criar nota diaria
    gatilho:
      evento: inicio_do_dia
    acoes:
      - tipo: criar_nota
        destino: Diario/{{date}}.md
  - nome: Auto-tagging
    gatilho:
      pasta: Leituras
    acoes:
      - tipo: adicionar_tags
        tags: [livro, leitura]
";

        var result = RuleValidator.Validate(yaml);

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WhenUnknownActionType_ShouldReturnErrors()
    {
        var yaml = @"
regras:
  - nome: Regra Inválida
    acoes:
      - tipo: deletar_tudo_destrutivo
";

        var result = RuleValidator.Validate(yaml);

        result.IsValid.ShouldBeFalse();
        result.Errors.Any(e => e.Contains("Ação desconhecida")).ShouldBeTrue();
    }

    [Fact]
    public void Validate_WhenInvalidYamlSyntax_ShouldReturnErrors()
    {
        var yaml = @"
regras:
  - nome: Quebrado
    acoes: [invalido: : {
";

        var result = RuleValidator.Validate(yaml);

        result.IsValid.ShouldBeFalse();
        result.Errors.Any(e => e.Contains("Erro de sintaxe YAML")).ShouldBeTrue();
    }
}
