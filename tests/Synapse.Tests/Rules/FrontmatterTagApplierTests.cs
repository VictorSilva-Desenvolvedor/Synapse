using Shouldly;
using Synapse.Rules;

namespace Synapse.Tests.Rules;

public class FrontmatterTagApplierTests
{
    [Fact]
    public void ApplyTags_WhenNoteHasNoFrontmatter_ShouldAddFrontmatterWithTags()
    {
        var rawContent = "# Minha Nota\nEste é o corpo da nota.";
        var tags = new[] { "inbox", "projeto" };

        var result = FrontmatterTagApplier.ApplyTags(rawContent, tags);

        result.ShouldStartWith("---\n");
        result.ShouldContain("inbox");
        result.ShouldContain("projeto");
        result.ShouldEndWith("# Minha Nota\nEste é o corpo da nota.");
    }

    [Fact]
    public void ApplyTags_WhenNoteHasExistingTags_ShouldAppendWithoutDuplicates()
    {
        var rawContent = "---\ntags:\n  - inbox\nstatus: ativo\n---\n# Nota com tags existentes";
        var tags = new[] { "inbox", "urgente" };

        var result = FrontmatterTagApplier.ApplyTags(rawContent, tags);

        result.ShouldContain("inbox");
        result.ShouldContain("urgente");
        result.ShouldContain("status: ativo");
        result.ShouldContain("# Nota com tags existentes");
    }

    [Fact]
    public void ApplyTags_WhenTagsToAddIsEmpty_ShouldReturnOriginalContent()
    {
        var rawContent = "# Nota simples";
        var result = FrontmatterTagApplier.ApplyTags(rawContent, []);

        result.ShouldBe(rawContent);
    }
}
