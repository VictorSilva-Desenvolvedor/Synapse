using Shouldly;
using Synapse.Conflict.Diff;

namespace Synapse.Tests.Conflict;

public class ThreeWayDiffCalculatorTests
{
    [Fact]
    public void Calculate_WhenLocalAndRemoteAreIdentical_ShouldReturnUnchangedBlock()
    {
        var calculator = new ThreeWayDiffCalculator();
        var baseContent = "Linha 1\nLinha 2";
        var localContent = "Linha 1\nLinha 2";
        var remoteContent = "Linha 1\nLinha 2";

        var blocks = calculator.Calculate(baseContent, localContent, remoteContent);

        blocks.Count.ShouldBe(1);
        blocks[0].Type.ShouldBe(DiffBlockType.Unchanged);
    }

    [Fact]
    public void Calculate_WhenOnlyLocalChanged_ShouldReturnLocalChangeBlock()
    {
        var calculator = new ThreeWayDiffCalculator();
        var baseContent = "Base";
        var localContent = "Modificacao Local";
        var remoteContent = "Base";

        var blocks = calculator.Calculate(baseContent, localContent, remoteContent);

        blocks.Any(b => b.Type == DiffBlockType.LocalChange).ShouldBeTrue();
    }

    [Fact]
    public void Calculate_WhenOnlyRemoteChanged_ShouldReturnRemoteChangeBlock()
    {
        var calculator = new ThreeWayDiffCalculator();
        var baseContent = "Base";
        var localContent = "Base";
        var remoteContent = "Modificacao Remota";

        var blocks = calculator.Calculate(baseContent, localContent, remoteContent);

        blocks.Any(b => b.Type == DiffBlockType.RemoteChange).ShouldBeTrue();
    }

    [Fact]
    public void Calculate_WhenBothChangedDifferently_ShouldReturnConflictBlock()
    {
        var calculator = new ThreeWayDiffCalculator();
        var baseContent = "Linha Base";
        var localContent = "Alterado Localmente";
        var remoteContent = "Alterado Remotamente";

        var blocks = calculator.Calculate(baseContent, localContent, remoteContent);

        blocks.Any(b => b.Type == DiffBlockType.Conflict).ShouldBeTrue();
    }

    [Fact]
    public void BuildMergedResult_WithChoices_ShouldAssembleCorrectly()
    {
        var blocks = new List<DiffBlock>
        {
            new() { Type = DiffBlockType.Unchanged, LocalText = "# Titulo", Choice = BlockResolutionChoice.Local },
            new() { Type = DiffBlockType.Conflict, LocalText = "Opcao Local", RemoteText = "Opcao Remota", Choice = BlockResolutionChoice.Remote }
        };

        var result = ThreeWayDiffCalculator.BuildMergedResult(blocks);

        result.ShouldBe("# Titulo\nOpcao Remota");
    }
}
