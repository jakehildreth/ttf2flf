using System.Text;
using flfview;

namespace ttf2flf.Tests;

public class FlfFormatTests
{
    private const string Sample = "Hello, World! 0123 @$#";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderMaxLength_EqualsLongestSerializedGlyphLineInBytes(bool antiAliased)
    {
        using var temp = new TempDir();
        var target = temp.File("font.flf");
        string[] args = antiAliased
            ? [TestSupport.FixtureFont, "--aa", "-o", target]
            : [TestSupport.FixtureFont, "-o", target];
        var (exitCode, _, error) = TestSupport.RunConverter(args);
        Assert.True(exitCode == 0, error);

        var longest = TestSupport.GlyphLines(target).Max(line => Encoding.UTF8.GetByteCount(line));

        Assert.Equal(longest, TestSupport.HeaderFields(target)[2]);
    }

    [Fact]
    public void GeneratedFont_DeclaresFullWidth_AndFigletRendersItLikeTheViewer()
    {
        // FullWidth header + glyph geometry built for FullWidth: FIGlet (honoring the
        // header) must produce exactly the side-by-side concatenation of glyph rows,
        // with every row's endmarks removed and visible content intact.
        using var temp = new TempDir();
        var target = temp.File("font.flf");
        var (exitCode, _, error) = TestSupport.RunConverter(TestSupport.FixtureFont, "-o", target);
        Assert.True(exitCode == 0, error);

        var header = TestSupport.HeaderFields(target);
        Assert.Equal(-1, header[3]);
        Assert.Equal(0, header[6]);

        var viewer = Renderer.Render(FlfFont.Load(target), Sample);
        var figlet = TestSupport.Figlet(target, Sample);

        Assert.Equal(figlet, viewer);
        Assert.Contains(viewer, row => row.Contains('█'));
    }

    [Fact]
    public void AntiAliasedOutput_MatchesGolden_AndInteroperatesWithFiglet()
    {
        using var temp = new TempDir();
        var target = temp.File("font.flf");
        var golden = TestSupport.FixturePath("PressStart2P-aa6.golden.flf");
        var (exitCode, _, error) = TestSupport.RunConverter(
            TestSupport.FixtureFont, "--aa", "--height", "6", "-o", target);
        Assert.True(exitCode == 0, error);

        // Comment lines carry a timestamp and version; header and glyph data must match.
        Assert.Equal(File.ReadLines(golden).First(), File.ReadLines(target).First());
        Assert.Equal(TestSupport.GlyphLines(golden), TestSupport.GlyphLines(target));

        var viewer = Renderer.Render(FlfFont.Load(golden), Sample);
        var figlet = TestSupport.Figlet(golden, Sample);
        Assert.Equal(figlet, viewer);
        Assert.Contains(viewer, row => row.IndexOfAny(['░', '▒', '▓']) >= 0);
    }
}
