using System.Reflection;
using Shouldly;
using Synapse.Core.Ports;

namespace Synapse.Tests.Core.Ports;

public class RuleActionTests
{
    [Fact]
    public void CreateNote_CarregaCaminhoAlvoETemplate()
    {
        var action = new RuleAction.CreateNote("Diario/2026-08-26.md", "Templates/diario.md");

        action.TargetPath.ShouldBe("Diario/2026-08-26.md");
        action.TemplatePath.ShouldBe("Templates/diario.md");
    }

    [Fact]
    public void AddTags_CarregaCaminhoAlvoEListaDeTags()
    {
        var action = new RuleAction.AddTags("Notas/exemplo.md", ["projeto", "urgente"]);

        action.TargetPath.ShouldBe("Notas/exemplo.md");
        action.Tags.ShouldBe(["projeto", "urgente"]);
    }

    [Fact]
    public void MoveNote_CarregaCaminhoOrigemEDestino()
    {
        var action = new RuleAction.MoveNote("Notas/exemplo.md", "Arquivo/exemplo.md");

        action.FromPath.ShouldBe("Notas/exemplo.md");
        action.ToPath.ShouldBe("Arquivo/exemplo.md");
    }

    // RF-RULES.5 / TC-08 (Plano de Testes): nenhuma regra pode apagar conteúdo. A garantia é no próprio
    // tipo (construtor privado impede subtipos externos); este teste falha se algum dia um caso de exclusão
    // for adicionado sem que essa decisão seja consciente e revisada.
    [Fact]
    public void RuleAction_NaoTemNenhumCasoDeExclusao()
    {
        var casosConhecidos = typeof(RuleAction)
            .GetNestedTypes(BindingFlags.Public)
            .Where(t => t.IsSubclassOf(typeof(RuleAction)))
            .Select(t => t.Name)
            .ToArray();

        casosConhecidos.ShouldBe(["CreateNote", "AddTags", "MoveNote", "AppendContent", "PrependContent", "ExtractTasks", "RenameNote"], ignoreOrder: true);
        casosConhecidos.ShouldNotContain(nome => nome.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        casosConhecidos.ShouldNotContain(nome => nome.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
