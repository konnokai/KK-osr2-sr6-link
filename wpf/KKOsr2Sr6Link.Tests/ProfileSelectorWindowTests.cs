using KKOsr2Sr6Link.Wpf;

namespace KKOsr2Sr6Link.Tests;

public class ProfileSelectorWindowTests
{
    [Theory]
    [InlineData(2560, 1440, 1920, 1080)]
    [InlineData(1920, 1080, 1600, 900)]
    [InlineData(3440, 1440, 2560, 1440)]
    public void DefaultSize_UsesNextLowerCommonResolution(
        double screenWidth, double screenHeight, double expectedWidth, double expectedHeight)
    {
        var size = ProfileSelectorWindow.SelectDefaultPixelSize(screenWidth, screenHeight);

        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
    }
}
