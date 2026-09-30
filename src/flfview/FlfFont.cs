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
/// spec and FIGlet itself do it, and hardblanks are replaced with spaces. Glyph rows
/// are kept as written; FIGlet does not pad them. Only FullWidth fonts are accepted:
/// flfview does not implement kerning or smushing, so other layouts would not match
/// FIGlet's output.
/// </summary>
public sealed class FlfFont
{
    // FIGlet mandated character order: ASCII 32..126, then the 7 German chars.
    private static readonly int[] CharOrder = BuildCharOrder();

    /// <summary>Index in <see cref="CharOrder"/> of the first German character (Ä).</summary>
    private const int FirstGermanIndex = 95;

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
        Parse(DecodeLines(File.ReadAllBytes(path)), Path.GetFileNameWithoutExtension(path));

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Splits font bytes into lines (LF, CRLF, or CR) and decodes each line as UTF-8, as
    /// FIGlet does. A line that is not valid UTF-8 becomes null; <see cref="Parse"/> then
    /// treats its glyph as undefined, matching FIGlet, which drops such glyphs. The header
    /// line is decoded leniently (an invalid hardblank byte, as in FIGlet's pyramid.flf,
    /// only affects glyphs that FIGlet drops anyway). A UTF-8 BOM is ignored.
    /// </summary>
    public static List<string?> DecodeLines(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            data = data[3..];

        var lines = new List<string?>();
        while (!data.IsEmpty)
        {
            var end = data.IndexOfAny((byte)'\r', (byte)'\n');
            var line = end < 0 ? data : data[..end];
            lines.Add(lines.Count == 0 ? Encoding.UTF8.GetString(line) : DecodeStrict(line));
            if (end < 0)
                break;

            var separatorLength = data[end] == '\r' && end + 1 < data.Length && data[end + 1] == '\n' ? 2 : 1;
            data = data[(end + separatorLength)..];
        }

        return lines;
    }

    private static string? DecodeStrict(ReadOnlySpan<byte> line)
    {
        try
        {
            return StrictUtf8.GetString(line);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

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

    /// <summary>
    /// Parses FLF text lines. <paramref name="name"/> becomes <see cref="Name"/>. A null
    /// glyph line (not valid UTF-8, see <see cref="DecodeLines"/>) leaves that glyph undefined.
    /// </summary>
    public static FlfFont Parse(IReadOnlyList<string?> lines, string name)
    {
        if (lines.Count == 0)
            throw new FormatException("Empty file.");

        // Header: flf2a$ height baseline maxlen oldlayout commentlines [printdir] [fulllayout] [codetag]
        var header = lines[0] ?? "";
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

        for (var index = 0; index < CharOrder.Length; index++)
        {
            var codePoint = CharOrder[index];

            // Like FIGlet, a font may end (optionally with blank lines) before the German
            // block; those characters stay undefined. Ending anywhere else is malformed.
            if (index >= FirstGermanIndex && RestIsBlank(lines, cursor))
                break;

            if (cursor + height > lines.Count)
                throw new FormatException(
                    $"Unexpected end of file reading char {codePoint} (line {cursor + 1}).");

            if (Enumerable.Range(cursor, height).Any(i => lines[i] is null))
            {
                // Not valid UTF-8: FIGlet drops the glyph, so the character stays undefined.
                cursor += height;
                continue;
            }

            var rows = new string[height];
            for (var r = 0; r < height; r++)
            {
                var row = StripEndmarks(lines[cursor + r]!, cursor + r + 1);
                rows[r] = row.Replace(hardblank, ' ');
            }

            // Rows are kept as written. FIGlet does not pad: a glyph whose rows differ
            // in width shifts later glyphs on the shorter rows.

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

    private static bool RestIsBlank(IReadOnlyList<string?> lines, int start)
    {
        for (var i = start; i < lines.Count; i++)
        {
            if (lines[i] is not { } line || !string.IsNullOrWhiteSpace(line))
                return false;
        }

        return true;
    }

    private static int HeaderInt(string[] parts, int index, string field) =>
        int.TryParse(parts[index], out var value)
            ? value
            : throw new FormatException($"Header field {field} is not an integer: '{parts[index]}'.");
}
