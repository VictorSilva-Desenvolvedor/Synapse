using System.Drawing;
using Shouldly;
using Synapse.Tray;

namespace Synapse.Tests.Tray;

public class IconGeneratorTests
{
    [Fact]
    public void CreateStatusIcon_ShouldGenerateValidIcon()
    {
        using var icon = IconGenerator.CreateStatusIcon(Color.Green, Color.LightGreen);

        icon.ShouldNotBeNull();
        icon.Width.ShouldBe(32);
        icon.Height.ShouldBe(32);
    }

    [Theory]
    [InlineData("Sincronizado", false)]
    [InlineData("Sincronizando", false)]
    [InlineData("Offline", false)]
    [InlineData("AuthRequired", false)]
    [InlineData("Erro", false)]
    [InlineData("Sincronizado", true)] // Pausado
    [InlineData("Desconhecido", false)]
    public void GetIconForState_ShouldReturnValidIconForAllStates(string estado, bool pausado)
    {
        using var icon = IconGenerator.GetIconForState(estado, pausado);

        icon.ShouldNotBeNull();
        icon.Width.ShouldBeGreaterThan(0);
        icon.Height.ShouldBeGreaterThan(0);
    }
}
