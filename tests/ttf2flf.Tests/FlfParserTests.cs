using System.Text;
using flfview;

namespace ttf2flf.Tests;

public class FlfParserTests
{
    private static readonly int[] CharOrder =
        [.. Enumerable.Range(32, 95), 196, 214, 220, 228, 246, 252, 223];

    /// <summary>Builds FLF lines: every glyph is "c" + endmarks except overrides.</summary>
    private static List<string> BuildFlf(
        int height, string header, IReadOnlyDictionary<int, string[]>? overrides = null)
    {
        var lines = new List<string> { header, "test font" };
        foreach (var code in CharOrder)
        {
            if (overrides is not null && overrides.TryGetValue(code, out var rows))
            {
                lines.AddRange(rows);
                continue;
            }

            // The '@' glyph needs a different endmark, as in real FIGlet fonts.
            var endmark = code == '@' ? '#' : '@';
            for (var r = 0; r < height; r++)
                lines.Add($"{(char)code}{endmark}{(r == height - 1 ? endmark.ToString() : "")}");
        }

        return lines;
    }

    private const string FullWidthHeader = "flf2a$ 2 1 10 -1 1 0 0 0";

    [Fact]
    public void TrailingAtContent_IsKept_WhenFontUsesAnotherEndmark()
    {
        var lines = BuildFlf(2, FullWidthHeader, new Dictionary<int, string[]>
        {
            ['A'] = ["A@#", "@@##"],
        });

        var font = FlfFont.Parse(lines, "test");

        Assert.Equal(["A@", "@@"], font.Glyphs['A']);
    }

    [Theory]
    [InlineData("ab@", "ab@@", "ab")]
    [InlineData("ab@  ", "ab@@\t", "ab")]
    [InlineData("a$b@", "a$b@@", "a b")]
    [InlineData("  @", "  @@", "  ")]
    public void StandardEndmarks_AndHardblanks_AreStripped(string row, string lastRow, string expected)
    {
        var lines = BuildFlf(2, FullWidthHeader, new Dictionary<int, string[]>
        {
            ['A'] = [row, lastRow],
        });

        var font = FlfFont.Parse(lines, "test");

        Assert.Equal([expected, expected], font.Glyphs['A']);
    }

    [Theory]
    [InlineData("", "no endmark")]
    [InlineData("   ", "no endmark")]
    [InlineData("ab@@@", "at most 2")]
    public void MalformedEndmarks_ThrowFormatErrorWithLineNumber(string badRow, string message)
    {
        var lines = BuildFlf(2, FullWidthHeader, new Dictionary<int, string[]>
        {
            ['A'] = [badRow, "A@@"],
        });
        var expectedLine = 1 + 1 + ('A' - 32) * 2 + 1; // header + comment + preceding glyphs + 1-based

        var ex = Assert.Throws<FormatException>(() => FlfFont.Parse(lines, "test"));

        Assert.Contains($"Line {expectedLine}:", ex.Message);
        Assert.Contains(message, ex.Message);
    }

    [Theory]
    [InlineData(-1, null, HorizontalLayout.FullWidth)]
    [InlineData(0, null, HorizontalLayout.Kerning)]
    [InlineData(15, null, HorizontalLayout.Smushing)]
    [InlineData(0, 0, HorizontalLayout.FullWidth)]
    [InlineData(-1, 64, HorizontalLayout.Kerning)]
    [InlineData(-1, 128 + 15, HorizontalLayout.Smushing)]
    [InlineData(15, 0, HorizontalLayout.FullWidth)]
    [InlineData(-1, 8192, HorizontalLayout.FullWidth)] // vertical-only bits
    public void LayoutFromHeader_PrefersFullLayout(int oldLayout, int? fullLayout, HorizontalLayout expected)
    {
        Assert.Equal(expected, FlfFont.LayoutFromHeader(oldLayout, fullLayout));
    }

    [Theory]
    [InlineData("flf2a$ 2 1 10 0 1 0 64 0")]
    [InlineData("flf2a$ 2 1 10 15 1")]
    public void NonFullWidthFonts_AreRejectedExplicitly(string header)
    {
        var ex = Assert.Throws<NotSupportedException>(() => FlfFont.Parse(BuildFlf(2, header), "test"));

        Assert.Contains("only FullWidth", ex.Message);
    }

    [Fact]
    public void ParsedFont_RendersLikeFiglet()
    {
        // Visible trailing '@' via a '#' endmark, hardblanks, trailing whitespace after
        // endmarks, and varying glyph widths.
        using var temp = new TempDir();
        var path = temp.File("interop.flf");
        var lines = BuildFlf(2, FullWidthHeader, new Dictionary<int, string[]>
        {
            ['A'] = ["/\\@#", "@@@##"],
            ['B'] = ["|$)@  ", "|$)@@"],
            ['C'] = ["((@", "((@@"],
        });
        File.WriteAllText(path, string.Join('\n', lines) + '\n', new UTF8Encoding(false));

        var font = FlfFont.Load(path);
        var viewer = Renderer.Render(font, "ABC");
        var figlet = TestSupport.Figlet(path, "ABC");

        Assert.Equal(figlet, viewer);
        Assert.Equal(["/\\@| )((", "@@@| )(("], viewer);
    }
}
