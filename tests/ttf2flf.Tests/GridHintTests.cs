namespace ttf2flf.Tests;

public class GridHintTests
{
    [Theory]
    [InlineData("3270 Regular", null)]
    [InlineData("SuperMario256", null)]
    [InlineData("BigBlueTerm437 Nerd Font", null)]
    [InlineData("Press Start 2P", null)]
    [InlineData("3270 Pixel 8", 8)]
    [InlineData("Model 2000 Grid 12", 12)]
    [InlineData("Font 99999999999999 9", 9)]
    [InlineData("Gecko 16 Turkish", 16)]
    [InlineData("Dust 7 Clean", 7)]
    [InlineData("Jacquard12", 12)]
    [InlineData("Grid 64", 64)]
    [InlineData("Grid 65", null)]
    [InlineData("Grid 4", null)]
    public void GridHintFromName_UsesLastNumberInSupportedRange(string fontName, int? expected)
    {
        Assert.Equal(expected, FontGridDetector.GridHintFromName(fontName));
    }
}
