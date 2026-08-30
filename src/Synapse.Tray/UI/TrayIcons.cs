namespace Synapse.Tray.UI;

/// <summary>
/// Matrizes 16x16 dos icones dos ladrilhos do menu da bandeja.
/// '.' e vazio; 'X' pinta. Uma linha por string, separadas por '|'.
///
/// Desenhados para serem reconheciveis a 16px, que e o tamanho em que serao lidos:
/// silhueta unica, sem detalhe interno fino, sem diagonal de um pixel.
/// </summary>
public static class TrayIcons
{
    /// <summary>Raio — captura rapida.</summary>
    public const string Bolt =
        "................|" +
        "................|" +
        ".........XXXX...|" +
        "........XXXX....|" +
        ".......XXXX.....|" +
        "......XXXX......|" +
        ".....XXXXXXXX...|" +
        "....XXXXXXXX....|" +
        ".........XXX....|" +
        "........XXX.....|" +
        ".......XXX......|" +
        "......XXX.......|" +
        ".....XXX........|" +
        "....XXX.........|" +
        "................|" +
        "................";

    /// <summary>Balao de fala — chat com o cofre.</summary>
    public const string Bubble =
        "................|" +
        "...XXXXXXXXXX...|" +
        "..X..........X..|" +
        "..X.XXXXXXXX.X..|" +
        "..X..........X..|" +
        "..X.XXXXXX...X..|" +
        "..X..........X..|" +
        "..X.XXXXXXX..X..|" +
        "..X..........X..|" +
        "...XXXXXXXXXX...|" +
        ".....XX.........|" +
        "....XX..........|" +
        "...XX...........|" +
        "................|" +
        "................|" +
        "................";

    /// <summary>Duas cartas empilhadas — flashcards.</summary>
    public const string Cards =
        "................|" +
        "....XXXXXXXXX...|" +
        "....X.......X...|" +
        "..XXXXXXXXX.X...|" +
        "..X.......X.X...|" +
        "..X.......X.X...|" +
        "..X.......XXX...|" +
        "..X.......X.....|" +
        "..X.XXXXX.X.....|" +
        "..X.......X.....|" +
        "..X.XXXXX.X.....|" +
        "..X.......X.....|" +
        "..XXXXXXXXX.....|" +
        "................|" +
        "................|" +
        "................";

    /// <summary>Barras — estatisticas do cofre.</summary>
    public const string Bars =
        "................|" +
        "................|" +
        "..........XX....|" +
        "..........XX....|" +
        "......XX..XX....|" +
        "......XX..XX....|" +
        "......XX..XX....|" +
        "..XX..XX..XX....|" +
        "..XX..XX..XX....|" +
        "..XX..XX..XX....|" +
        "..XX..XX..XX....|" +
        "..XX..XX..XX....|" +
        "..XXXXXXXXXXXX..|" +
        "................|" +
        "................|" +
        "................";
}
