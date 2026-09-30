using System.Text;
using flfview;

namespace ttf2flf.Tests;

public class ViewerTests
{
    private static FlfFont TinyFont() => new()
    {
        Name = "tiny",
        Height = 1,
        Glyphs = new Dictionary<int, string[]>
        {
            [' '] = ["  "],
            ['A'] = ["AA"],
            ['B'] = ["BBB"],
        },
    };

    [Fact]
    public void UnsupportedSupplementaryCharacter_RendersOneFallbackGlyph()
    {
        var font = TinyFont();

        Assert.Equal(["AA  BBB"], Renderer.Render(font, "A🙂B"));
    }

    [Fact]
    public void BmpText_RendersGlyphsSideBySide()
    {
        Assert.Equal(["AABBB"], Renderer.Render(TinyFont(), "AB"));
    }

    [Fact]
    public void Backspace_RemovesOneCompleteScalarValue()
    {
        var buffer = new WordBuffer();
        foreach (var c in "A🙂B")
            buffer.Append(c);

        buffer.Backspace();
        Assert.Equal("A🙂", buffer.Text);

        buffer.Backspace();
        Assert.Equal("A", buffer.Text);
    }

    // Key events as UTF-16 code units in hex (lone surrogates cannot travel through
    // test-case serialization as strings).
    [Theory]
    [InlineData("0041 D83D 0042", "AB")]   // high surrogate then BMP char
    [InlineData("0041 DE42 0042", "AB")]   // lone low surrogate
    [InlineData("D83D D83D DE42", "🙂")]   // repeated high surrogate
    [InlineData("0041 D83D", "A")]         // pending at end
    public void Input_NeverStoresUnpairedSurrogates(string keyUnits, string expected)
    {
        var buffer = new WordBuffer();
        foreach (var unit in keyUnits.Split(' '))
            buffer.Append((char)Convert.ToUInt16(unit, 16));

        Assert.Equal(expected, buffer.Text);
        Assert.All(buffer.Text.EnumerateRunes(), rune => Assert.NotEqual(Rune.ReplacementChar, rune));
    }

    [Fact]
    public void DefaultDirectories_ResolveUnderTheApplicationDirectory()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "somewhere", "flfview");

        var dirs = FontLibrary.DefaultDirectories(appDir);

        Assert.Equal(
            [(Path.Combine(appDir, "fonts", "verified"), "verified"), (Path.Combine(appDir, "fonts", "clean"), "clean")],
            dirs);
    }

    [Fact]
    public void BundledFonts_LoadFromBesideTheBinary_WithoutWarnings()
    {
        // The flfview project copies the corpus to fonts/ in every output that includes it,
        // including this test output directory.
        var warnings = new StringWriter();

        var fonts = FontLibrary.Load(FontLibrary.DefaultDirectories(AppContext.BaseDirectory), warnings);

        Assert.Equal("", warnings.ToString());
        Assert.Equal(
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fonts"), "*.flf", SearchOption.AllDirectories).Length,
            fonts.Count);
        Assert.NotEmpty(fonts);
        Assert.Contains(fonts, f => f.Tier == "verified");
        Assert.Contains(fonts, f => f.Tier == "clean");
    }

    [Fact]
    public void Load_SkipsNonFullWidthFontsWithAClearWarning()
    {
        using var temp = new TempDir();
        var lines = new List<string> { "flf2a$ 1 0 4 0 0 0 64 0" };
        lines.AddRange(Enumerable.Repeat("x@@", 102));
        File.WriteAllLines(temp.File("kerned.flf"), lines);
        var warnings = new StringWriter();

        var fonts = FontLibrary.Load([(temp.Path, "custom")], warnings);

        Assert.Empty(fonts);
        Assert.Contains("skipping kerned.flf", warnings.ToString());
        Assert.Contains("only FullWidth", warnings.ToString());
    }
}
