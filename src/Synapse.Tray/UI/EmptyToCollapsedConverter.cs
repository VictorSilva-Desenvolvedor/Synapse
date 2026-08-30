using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Synapse.Tray.UI;

/// <summary>
/// Colapsa um elemento quando o texto vinculado esta vazio, para que a barra de topo
/// nao reserve espaco de subtitulo em telas que nao tem um.
/// </summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
