using System.Text.RegularExpressions;

namespace Synapse.Tray.Diagnostics;

/// <summary>
/// Uma linha do log com a severidade extraida, para que a cor possa distingui-la.
///
/// Antes o log inteiro era um TextBlock unico: um WRN de conflito ficava visualmente
/// identico a um INF de rotina, e a tela so e aberta justamente quando algo deu errado.
/// </summary>
public sealed record LogLine(string Text, string Level)
{
    // Serilog escreve "[HH:mm:ss NIVEL] mensagem". Se o formato mudar, a linha cai em
    // INF e continua legivel — a extracao degrada, nao quebra.
    private static readonly Regex LevelPattern =
        new(@"^\[\d{2}:\d{2}:\d{2}\s+(?<lvl>[A-Z]{3})\]", RegexOptions.Compiled);

    public static LogLine Parse(string raw)
    {
        var match = LevelPattern.Match(raw);
        var level = match.Success ? match.Groups["lvl"].Value : "INF";
        return new LogLine(raw, level);
    }

    public static IReadOnlyList<LogLine> ParseAll(IEnumerable<string> rawLines)
        => rawLines.Select(Parse).ToList();
}
