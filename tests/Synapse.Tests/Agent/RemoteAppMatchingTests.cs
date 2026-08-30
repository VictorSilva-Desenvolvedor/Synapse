using Shouldly;
using Synapse.Agent;

namespace Synapse.Tests.Agent;

public class RemoteAppMatchingTests
{
    private readonly Dictionary<string, string> _allowedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["notepad"] = @"C:\Windows\System32\notepad.exe",
        ["calc"] = @"C:\Windows\System32\calc.exe",
        ["spotify"] = @"C:\Users\User\AppData\Roaming\Spotify\Spotify.exe",
        ["bloco de notas"] = @"C:\Windows\System32\notepad.exe",
        ["visual studio code"] = @"C:\Users\User\AppData\Local\Programs\Microsoft VS Code\Code.exe",
        ["area de trabalho"] = @"explorer.exe"
    };

    [Fact]
    public void MatchExato_PreservaChaveOriginal()
    {
        var result = RemoteCommandExecutor.ResolveAllowedAppKey("notepad", _allowedApps);
        result.ShouldBe("notepad");

        var resultCalc = RemoteCommandExecutor.ResolveAllowedAppKey("CALC", _allowedApps);
        resultCalc.ShouldBe("calc");
    }

    [Fact]
    public void FrasesComPalavrasDePreenchimento_CasamCorretamente()
    {
        RemoteCommandExecutor.ResolveAllowedAppKey("abre o bloco de notas", _allowedApps).ShouldBe("bloco de notas");
        RemoteCommandExecutor.ResolveAllowedAppKey("abra o notepad por favor", _allowedApps).ShouldBe("notepad");
        RemoteCommandExecutor.ResolveAllowedAppKey("open spotify", _allowedApps).ShouldBe("spotify");
        RemoteCommandExecutor.ResolveAllowedAppKey("launch calc app", _allowedApps).ShouldBe("calc");
        RemoteCommandExecutor.ResolveAllowedAppKey("iniciar o programa visual studio code", _allowedApps).ShouldBe("visual studio code");
        RemoteCommandExecutor.ResolveAllowedAppKey("executa o aplicativo notepad", _allowedApps).ShouldBe("notepad");
    }

    [Fact]
    public void VariacaoDeAcentosEMaiusculas_CasaCorretamente()
    {
        RemoteCommandExecutor.ResolveAllowedAppKey("Ábre o Blóco de Nótas", _allowedApps).ShouldBe("bloco de notas");
        RemoteCommandExecutor.ResolveAllowedAppKey("ABRE A ÁREA DE TRABALHO", _allowedApps).ShouldBe("area de trabalho");
    }

    [Fact]
    public void MatchPorNomeDeExibicaoDoExecutavel_QuandoChaveNaoBateDireto()
    {
        var dict = new Dictionary<string, string>
        {
            ["editor"] = @"C:\Windows\System32\notepad.exe"
        };

        RemoteCommandExecutor.ResolveAllowedAppKey("abre o notepad", dict).ShouldBe("editor");
    }

    [Fact]
    public void SubstringComMinimoDeTresCaracteres_CasaCorretamente()
    {
        var dict = new Dictionary<string, string>
        {
            ["spotify"] = @"C:\Spotify.exe"
        };

        RemoteCommandExecutor.ResolveAllowedAppKey("abre o spot", dict).ShouldBe("spotify");
    }

    [Fact]
    public void LevenshteinDistance_ToleraPequenosErrosDeDigitacao()
    {
        // "spotfy" -> "spotify" (distância 1)
        RemoteCommandExecutor.ResolveAllowedAppKey("abre o spotfy", _allowedApps).ShouldBe("spotify");

        // "notepd" -> "notepad" (distância 1)
        RemoteCommandExecutor.ResolveAllowedAppKey("open notepd", _allowedApps).ShouldBe("notepad");
    }

    [Fact]
    public void AmbiguidadeNoMesmoNivel_RetornaNulo()
    {
        var ambiguousDict = new Dictionary<string, string>
        {
            ["notepad1"] = @"C:\notepad1.exe",
            ["notepad2"] = @"C:\notepad2.exe"
        };

        // Substring "notepad" casa igualmente com "notepad1" e "notepad2" no mesmo nível
        var result = RemoteCommandExecutor.ResolveAllowedAppKey("abre o notepad", ambiguousDict);
        result.ShouldBeNull();
    }

    [Fact]
    public void SemMatch_RetornaNulo()
    {
        RemoteCommandExecutor.ResolveAllowedAppKey("abre o photoshop", _allowedApps).ShouldBeNull();
        RemoteCommandExecutor.ResolveAllowedAppKey("powershell -Command Remove-Item", _allowedApps).ShouldBeNull();
        RemoteCommandExecutor.ResolveAllowedAppKey("", _allowedApps).ShouldBeNull();
        RemoteCommandExecutor.ResolveAllowedAppKey("   ", _allowedApps).ShouldBeNull();
    }

    [Fact]
    public void InvarianteDeSeguranca_NuncaRetornaChaveForaDoDicionario()
    {
        var inputs = new[]
        {
            "cmd",
            "calc",
            "abre calc",
            "executa malware.exe",
            "format c:",
            "open spotify",
            "bloco de notas",
            "abra qualquer coisa"
        };

        foreach (var input in inputs)
        {
            var match = RemoteCommandExecutor.ResolveAllowedAppKey(input, _allowedApps);
            if (match != null)
            {
                _allowedApps.ContainsKey(match).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void FraseCompostaApenasPorPalavrasDePreenchimento_FazFallbackParaEntradaNormalizada()
    {
        var dict = new Dictionary<string, string>
        {
            ["abre"] = @"C:\abre.exe",
            ["app"] = @"C:\app.exe"
        };

        RemoteCommandExecutor.ResolveAllowedAppKey("abre", dict).ShouldBe("abre");
        RemoteCommandExecutor.ResolveAllowedAppKey("app", dict).ShouldBe("app");
    }
}
