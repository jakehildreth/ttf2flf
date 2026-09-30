using System.Text;

namespace flfview;

/// <summary>Horizontal fitting mode declared by a FIGlet font header.</summary>
public enum HorizontalLayout
{
    FullWidth,
    Kerning,
    Smushing,
}

/// <summary>
/// Minimal FIGlet (.flf) font parser. Parses the header, skips comment lines,
/// then reads the 102 required characters (ASCII 32-126, then German
/// 196, 214, 220, 228, 246, 252, 223). Endmarks are stripped the way the FIGlet
/// spec and FIGlet itself do it, and hardblanks are replaced with spaces. Each
/// glyph's rows are padded to the glyph's own maximum row width so columns align
/// when concatenated. Only FullWidth fonts are accepted: flfview does not implement
/// kerning or smushing, so other layouts would not match FIGlet's output.
/// </summary>
public sealed class FlfFont
{
    // FIGlet mandated character order: ASCII 32..126, then the 7 German chars.
    private static readonly int[] CharOrder = BuildCharOrder();

    private static int[] BuildCharOrder()
    {
        var order = new int[102];
        for (var i = 0; i < 95; i++)
            order[i] = 32 + i;
        order[95] = 196; // Ä
        order[96] = 214; // Ö
        order[97] = 220; // Ü
        order[98] = 228; // ä
        order[99] = 246; // ö
        order[100] = 252; // ü
        order[101] = 223; // ß
        return order;
    }

    public required string Name { get; init; }
    public required int Height { get; init; }

    /// <summary>Glyph rows keyed by Unicode code point.</summary>
    public required IReadOnlyDictionary<int, string[]> Glyphs { get; init; }

    /// <summary>
    /// Quality tier. "verified" = PNG-bitmap-verified corpus font;
    /// "clean" = Font Book font that converted cleanly;
    /// "approximate" = Font Book font that is decorative/stroked (faithful but
    /// not a clean 1:1 pixel grid). Default "clean".
    /// </summary>
    public string Tier { get; set; } = "clean";

    /// <summary>Fonts known to be approximate (decorative / stroked / dithered).</summary>
    public static readonly IReadOnlySet<string> ApproximateFonts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "8-bit Arcade Out", "Funk", "submarine_vs_whale",
            "DepartureMono-Regular",
        };

    public int SpaceWidth => Glyphs.TryGetValue(' ', out var sp) && sp.Length > 0
        ? sp[0].Length
        : 1;

    /// <summary>Gets the glyph for a code point, or one blank space-width glyph if missing.</summary>
    public string[] GetGlyph(Rune rune)
    {
        if (Glyphs.TryGetValue(rune.Value, out var glyph))
            return glyph;

        var blankWidth = Math.Max(1, SpaceWidth);
        var blank = new string(' ', blankWidth);
        var rows = new string[Height];
        Array.Fill(rows, blank);
        return rows;
    }

    public static FlfFont Load(string path) =>
        Parse(File.ReadAllLines(path), Path.GetFileNameWithoutExtension(path));

    /// <summary>
    /// Returns the horizontal layout from the header's old_layout and optional
    /// full_layout fields. full_layout wins when present (bit 128 = smushing,
    /// bit 64 = kerning); otherwise old_layout -1 = full width, 0 = kerning,
    /// positive = smushing.
    /// </summary>
    public static HorizontalLayout LayoutFromHeader(int oldLayout, int? fullLayout)
    {
        if (fullLayout is { } full)
        {
            if ((full & 128) != 0) return HorizontalLayout.Smushing;
            if ((full & 64) != 0) return HorizontalLayout.Kerning;
            return HorizontalLayout.FullWidth;
        }

        return oldLayout switch
        {
            < 0 => HorizontalLayout.FullWidth,
            0 => HorizontalLayout.Kerning,
            _ => HorizontalLayout.Smushing,
        };
    }

    /// <summary>
    /// Removes the endmark run from one glyph line, as FIGlet does: trailing whitespace
    /// is ignored, the last remaining character is the endmark, and the whole run of
    /// that character is removed. A line with no endmark, or with more than two (the
    /// FIGlet spec maximum), is malformed. A glyph that must end in its own endmark
    /// character (e.g. a visible trailing @) must use a different endmark (e.g. #).
    /// </summary>
    public static string StripEndmarks(string line, int lineNumber)
    {
        var end = line.Length;
        while (end > 0 && char.IsWhiteSpace(line[end - 1]))
            end--;
        if (end == 0)
            throw new FormatException($"Line {lineNumber}: glyph row has no endmark.");

        var endmark = line[end - 1];
        var start = end;
        while (start > 0 && line[start - 1] == endmark)
            start--;

        var count = end - start;
        if (count > 2)
            throw new FormatException(
                $"Line {lineNumber}: glyph row ends with {count} '{endmark}' endmarks; at most 2 are allowed.");

        return line[..start];
    }

    /// <summary>Parses FLF text lines. <paramref name="name"/> becomes <see cref="Name"/>.</summary>
    public static FlfFont Parse(IReadOnlyList<string> lines, string name)
    {
        if (lines.Count == 0)
            throw new FormatException("Empty file.");

        // Header: flf2a$ height baseline maxlen oldlayout commentlines [printdir] [fulllayout] [codetag]
        var header = lines[0];
        if (header.Length < 6 || !header.StartsWith("flf2a", StringComparison.Ordinal))
            throw new FormatException("Bad signature.");

        char hardblank = header[5];
        var parts = header[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            throw new FormatException("Header has too few fields.");

        int height = HeaderInt(parts, 0, "height");
        int oldLayout = HeaderInt(parts, 3, "old_layout");
        int commentLines = HeaderInt(parts, 4, "comment_lines");
        int? fullLayout = parts.Length > 6 ? HeaderInt(parts, 6, "full_layout") : null;
        if (height < 1)
            throw new FormatException($"Header height must be positive, got {height}.");
        if (commentLines < 0)
            throw new FormatException($"Header comment_lines must not be negative, got {commentLines}.");

        var layout = LayoutFromHeader(oldLayout, fullLayout);
        if (layout != HorizontalLayout.FullWidth)
            throw new NotSupportedException(
                $"font uses {layout} layout; flfview renders only FullWidth fonts " +
                $"(old_layout -1, or full_layout without bits 64/128).");

        int cursor = 1 + commentLines;
        var glyphs = new Dictionary<int, string[]>();

        foreach (int codePoint in CharOrder)
        {
            if (cursor + height > lines.Count)
                throw new FormatException(
                    $"Unexpected end of file reading char {codePoint} (line {cursor + 1}).");

            var rows = new string[height];
            for (var r = 0; r < height; r++)
            {
                var row = StripEndmarks(lines[cursor + r], cursor + r + 1);
                rows[r] = row.Replace(hardblank, ' ');
            }

            // Pad this glyph's rows to its own max width so columns align.
            int maxWidth = 0;
            foreach (var row in rows)
                maxWidth = Math.Max(maxWidth, row.Length);
            for (var r = 0; r < height; r++)
                rows[r] = rows[r].PadRight(maxWidth);

            glyphs[codePoint] = rows;
            cursor += height;
        }

        return new FlfFont
        {
            Name = name,
            Height = height,
            Glyphs = glyphs,
        };
    }

    private static int HeaderInt(string[] parts, int index, string field) =>
        int.TryParse(parts[index], out var value)
            ? value
            : throw new FormatException($"Header field {field} is not an integer: '{parts[index]}'.");
}
