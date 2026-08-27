using Shouldly;
using Synapse.Sync.Ignore;

namespace Synapse.Tests.Ignore;

public class SynapseIgnoreMatcherTests
{
    [Theory]
    [InlineData(".obsidian/workspace.json")]
    [InlineData(".obsidian/workspace-mobile.json")]
    [InlineData(".obsidian/cache/indexed.bin")]
    [InlineData(".trash/nota-antiga.md")]
    [InlineData("_conflitos/Nota.md")]
    [InlineData(".synapse/regras.yaml")]
    [InlineData(".git/config")]
    [InlineData("temp.tmp")]
    [InlineData("subpasta/arquivo.crswap")]
    [InlineData("Thumbs.db")]
    public void ShouldIgnore_DefaultPatterns_ShouldReturnTrue(string relativePath)
    {
        var matcher = new SynapseIgnoreMatcher();
        matcher.ShouldIgnore(relativePath).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Notas/MinhaNota.md")]
    [InlineData("Diario/2026-08-27.md")]
    [InlineData("Imagens/diagrama.png")]
    [InlineData("Projetos/Sprint1/tarefas.md")]
    public void ShouldIgnore_RegularAllowedFiles_ShouldReturnFalse(string relativePath)
    {
        var matcher = new SynapseIgnoreMatcher();
        matcher.ShouldIgnore(relativePath).ShouldBeFalse();
    }

    [Fact]
    public void ShouldIgnore_CustomWildcardsAndFolderPatterns_ShouldMatchCorrectly()
    {
        var ignoreContent = @"
# Comentário que deve ser ignorado
*.secret.md
Privado/**
anexos/*.mp4
";
        var matcher = new SynapseIgnoreMatcher(initialIgnoreContent: ignoreContent);

        matcher.ShouldIgnore("Notas/senha.secret.md").ShouldBeTrue();
        matcher.ShouldIgnore("Privado/documento.md").ShouldBeTrue();
        matcher.ShouldIgnore("Privado/Sub/foto.jpg").ShouldBeTrue();
        matcher.ShouldIgnore("anexos/video.mp4").ShouldBeTrue();

        // Não ignorados
        matcher.ShouldIgnore("Notas/publico.md").ShouldBeFalse();
        matcher.ShouldIgnore("anexos/video.png").ShouldBeFalse();
    }

    [Fact]
    public void ShouldIgnore_FileSizeLimit_ShouldIgnoreFilesExceedingLimit()
    {
        var limit50Mb = 50 * 1024 * 1024;
        var matcher = new SynapseIgnoreMatcher(maxFileSizeBytes: limit50Mb);

        var normalSize = 10 * 1024 * 1024; // 10MB
        var exceededSize = 51 * 1024 * 1024; // 51MB

        matcher.ShouldIgnore("Anexos/audio.mp3", normalSize).ShouldBeFalse();
        matcher.ShouldIgnore("Anexos/video-pesado.mp4", exceededSize).ShouldBeTrue();
    }
}
