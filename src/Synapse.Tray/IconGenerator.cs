using System.Drawing.Drawing2D;

namespace Synapse.Tray;

/// <summary>
/// Gera ícones de bandeja programaticamente com cores de status dinâmicas sem depender de assets externos (ADR-009).
/// </summary>
public static class IconGenerator
{
    public static Icon CreateStatusIcon(Color mainColor, Color? pulseColor = null)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Anel ou pulso externo se fornecido
        if (pulseColor.HasValue)
        {
            using var pulseBrush = new SolidBrush(Color.FromArgb(80, pulseColor.Value));
            g.FillEllipse(pulseBrush, 2, 2, size - 4, size - 4);
        }

        // Círculo principal
        using var brush = new SolidBrush(mainColor);
        g.FillEllipse(brush, 6, 6, size - 12, size - 12);

        // Brilho central
        using var shineBrush = new SolidBrush(Color.FromArgb(120, Color.White));
        g.FillEllipse(shineBrush, 10, 10, 6, 6);

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public static Icon GetIconForState(string estado, bool pausado)
    {
        if (pausado)
        {
            return CreateStatusIcon(Color.FromArgb(245, 158, 11)); // Amber / Pausado
        }

        return estado switch
        {
            "Sincronizado" => CreateStatusIcon(Color.FromArgb(16, 185, 129)), // Emerald
            "Sincronizando" => CreateStatusIcon(Color.FromArgb(99, 102, 241), Color.FromArgb(199, 200, 250)), // Indigo pulse (acento secundário)
            "Offline" => CreateStatusIcon(Color.FromArgb(245, 158, 11)), // Amber
            "AuthRequired" => CreateStatusIcon(Color.FromArgb(239, 68, 68), Color.FromArgb(254, 202, 202)), // Red pulse
            "Erro" => CreateStatusIcon(Color.FromArgb(239, 68, 68)), // Red
            _ => CreateStatusIcon(Color.FromArgb(156, 163, 175)) // Gray / Desconectado
        };
    }
}
