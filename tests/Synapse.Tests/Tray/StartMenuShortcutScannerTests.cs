using Shouldly;
using Synapse.Tray.RemoteApps;

namespace Synapse.Tests.Tray;

public class StartMenuShortcutScannerTests : IDisposable
{
    private readonly string _tempDir;

    public StartMenuShortcutScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"synapse-shortcuts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignora falhas de cleanup em testes
            }
        }
    }

    [Fact]
    public void Scan_DescobreAtalhosValidosEIgnoraDesinstaladores()
    {
        // Cria estrutura de mock com arquivos .lnk
        var subDir = Path.Combine(_tempDir, "Acessorios");
        Directory.CreateDirectory(subDir);

        var notepadPath = Path.Combine(_tempDir, "Bloco de Notas.lnk");
        var spotifyPath = Path.Combine(subDir, "Spotify.lnk");
        var uninstallerPath = Path.Combine(_tempDir, "Desinstalar Spotify.lnk");
        var uninstallToolPath = Path.Combine(subDir, "Tool Uninstall.lnk");
        var nonLnkPath = Path.Combine(_tempDir, "readme.txt");

        File.WriteAllText(notepadPath, "mock lnk content");
        File.WriteAllText(spotifyPath, "mock lnk content");
        File.WriteAllText(uninstallerPath, "mock lnk content");
        File.WriteAllText(uninstallToolPath, "mock lnk content");
        File.WriteAllText(nonLnkPath, "mock text content");

        var results = StartMenuShortcutScanner.Scan(new[] { _tempDir });

        results.Count.ShouldBe(2);

        var notepad = results.FirstOrDefault(r => r.Name == "Bloco de Notas");
        notepad.ShouldNotBeNull();
        notepad.SuggestedKey.ShouldBe("bloco de notas");
        notepad.ShortcutPath.ShouldBe(notepadPath);

        var spotify = results.FirstOrDefault(r => r.Name == "Spotify");
        spotify.ShouldNotBeNull();
        spotify.SuggestedKey.ShouldBe("spotify");
        spotify.ShortcutPath.ShouldBe(spotifyPath);
    }

    [Theory]
    [InlineData("Bloco de Notas", "bloco de notas")]
    [InlineData("Google Chrome (64-bit)", "google chrome 64 bit")]
    [InlineData("Visual Studio Code", "visual studio code")]
    [InlineData("Área de Trabalho", "area de trabalho")]
    [InlineData("Música & Vídeo!", "musica video")]
    public void GenerateSuggestedKey_NormalizaCorretamente(string rawName, string expectedKey)
    {
        var key = StartMenuShortcutScanner.GenerateSuggestedKey(rawName);
        key.ShouldBe(expectedKey);
    }
}
