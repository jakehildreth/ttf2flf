

/// <summary>
/// Minimal FIGlet (.flf) font parser. Parses the header, skips comment lines,
/// then reads the 102 required characters (ASCII 32-126, then German
/// 196, 214, 220, 228, 246, 252, 223). End marks (@ / @@) are stripped and
/// hardblanks are replaced with spaces. Each glyph's rows are padded to the
/// glyph's own maximum row width so columns align when concatenated.
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
    public required IReadOnlyDictionary<char, string[]> Glyphs { get; init; }

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

    /// <summary>Gets the glyph for a char, or a blank glyph if missing.</summary>
    public string[] GetGlyph(char c)
    {
        if (Glyphs.TryGetValue(c, out var glyph))
            return glyph;

        var blankWidth = Math.Max(1, SpaceWidth);
        var blank = new string(' ', blankWidth);
        var rows = new string[Height];
        Array.Fill(rows, blank);
        return rows;
    }

    public static FlfFont Load(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
            throw new FormatException("Empty file.");

        // Header: flf2a$ height baseline maxlen oldlayout commentlines [printdir] [fulllayout] [codetag]
        var header = lines[0];
        if (header.Length < 6 || !header.StartsWith("flf2a", StringComparison.Ordinal))
            throw new FormatException("Bad signature.");

        char hardblank = header[5];
        var parts = header[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            throw new FormatException("Header has too few fields.");

        int height = int.Parse(parts[0]);
        int commentLines = int.Parse(parts[4]);

        int cursor = 1 + commentLines;
        var glyphs = new Dictionary<char, string[]>();

        foreach (int codePoint in CharOrder)
        {
            if (cursor + height > lines.Length)
                throw new FormatException(
                    $"Unexpected end of file reading char {codePoint} (line {cursor + 1}).");

            var rows = new string[height];
            for (var r = 0; r < height; r++)
            {
                var row = lines[cursor + r];
                // Last row ends with @@, all others end with @.
                row = row.TrimEnd('@');
                rows[r] = row.Replace(hardblank, ' ');
            }

            // Pad this glyph's rows to its own max width so columns align.
            int maxWidth = 0;
            foreach (var row in rows)
                maxWidth = Math.Max(maxWidth, row.Length);
            for (var r = 0; r < height; r++)
                rows[r] = rows[r].PadRight(maxWidth);

            glyphs[(char)codePoint] = rows;
            cursor += height;
        }

        return new FlfFont
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Height = height,
            Glyphs = glyphs,
        };
    }
}
