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

    [Fact]
    public void RaggedGlyphRows_AreNotPadded_LikeFiglet()
    {
        // A's second row is shorter than its first. FIGlet appends rows as written, so B
        // shifts left on the short row.
        using var temp = new TempDir();
        var path = temp.File("ragged.flf");
        var lines = BuildFlf(2, FullWidthHeader, new Dictionary<int, string[]>
        {
            ['A'] = ["AAAA@", "A@@"],
            ['B'] = ["BB@", "BB@@"],
        });
        File.WriteAllText(path, string.Join('\n', lines) + '\n', new UTF8Encoding(false));

        var font = FlfFont.Load(path);
        var viewer = Renderer.Render(font, "ABA");

        Assert.Equal(["AAAABBAAAA", "ABBA"], viewer);
        Assert.Equal(TestSupport.Figlet(path, "ABA"), viewer);
    }

    [Fact]
    public void GlyphWithInvalidUtf8_IsUndefined_LikeFiglet()
    {
        // 0x81 hardblank bytes, as in FIGlet's pyramid.flf. FIGlet decodes font lines as
        // UTF-8 and drops a glyph whose bytes are not valid UTF-8.
        using var temp = new TempDir();
        var path = temp.File("invalid.flf");
        var lines = BuildFlf(2, "flf2a\u0081 2 1 10 -1 1 0 0 0", new Dictionary<int, string[]>
        {
            ['B'] = ["|\u0081)@", "|\u0081)@@"],
            ['C'] = ["é@", "é@@"],
        });
        // Lines with U+0081 are written as Latin-1 (the single byte 0x81); the rest as UTF-8.
        File.WriteAllBytes(path, [.. lines.SelectMany(line =>
            (line.Contains('\u0081') ? Encoding.Latin1 : Encoding.UTF8).GetBytes(line + "\n"))]);

        var font = FlfFont.Load(path);

        Assert.False(font.Glyphs.ContainsKey('B'));
        Assert.Equal(["é", "é"], font.Glyphs['C']);
        Assert.Equal(TestSupport.Figlet(path, "ACA"), Renderer.Render(font, "ACA"));
        Assert.Equal(TestSupport.Figlet(path, "ABA"), Renderer.Render(font, "AA"));
    }

    [Fact]
    public void Utf8FontWithBomAndCrlf_ParsesAsUtf8()
    {
        using var temp = new TempDir();
        var path = temp.File("bom.flf");
        var lines = BuildFlf(2, FullWidthHeader, new Dictionary<int, string[]>
        {
            ['A'] = ["█▀@", "▄█@@"],
        });
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var font = FlfFont.Load(path);

        Assert.Equal(["█▀", "▄█"], font.Glyphs['A']);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FontEndingBeforeGermanBlock_LoadsWithGermanUndefined(int trailingBlankLines)
    {
        using var temp = new TempDir();
        var path = temp.File("ascii-only.flf");
        var lines = BuildFlf(2, FullWidthHeader).Take(2 + 95 * 2).Concat(Enumerable.Repeat("", trailingBlankLines));
        File.WriteAllText(path, string.Join('\n', lines) + '\n');

        var font = FlfFont.Load(path);

        Assert.Equal(95, font.Glyphs.Count);
        Assert.False(font.Glyphs.ContainsKey('Ä'));
        Assert.Equal(TestSupport.Figlet(path, "Hi~"), Renderer.Render(font, "Hi~"));
    }

    [Theory]
    [InlineData(2 + 50 * 2)]     // ends at a glyph boundary inside ASCII
    [InlineData(2 + 95 * 2 + 1)] // ends inside the first German glyph
    public void FontEndingElsewhere_IsAFormatError(int keptLines)
    {
        var lines = BuildFlf(2, FullWidthHeader).Take(keptLines).ToList();

        var ex = Assert.Throws<FormatException>(() => FlfFont.Parse(lines, "test"));

        Assert.Contains("Unexpected end of file", ex.Message);
    }
}
