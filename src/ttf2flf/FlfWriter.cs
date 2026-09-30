using System.Text;

namespace ttf2flf;

/// <summary>
/// Writes the FLF file: header line, comment block, and the 102 required characters
/// (ASCII 32-126 then German 196, 214, 220, 228, 246, 252, 223) with endmarks.
/// Output always declares FIGlet FullWidth layout: glyph geometry (including the
/// 1px right spacer) is built for side-by-side placement, and FIGlet smushing works
/// on bytes, which would corrupt the multibyte block characters.
/// </summary>
public static class FlfWriter
{
    public const string Signature = "flf2a";
    public const char DefaultEndmark = '@';

    /// <summary>ASCII 32-126, then German 196, 214, 220, 228, 246, 252, 223.</summary>
    public static readonly int[] RequiredCharacters =
        Enumerable.Range(32, 95).Concat([196, 214, 220, 228, 246, 252, 223]).ToArray();

    /// <summary>FIGlet FullWidth: old layout -1, full layout 0 (no kerning, no smushing).</summary>
    public const int OldLayoutFullWidth = -1;
    public const int FullLayoutFullWidth = 0;

    /// <summary>flf2a$ Height Baseline MaxLen OldLayout CommentLines PrintDir FullLayout CodetagCount</summary>
    public static string Header(
        char hardblank, int height, int baseline, int maxLength,
        int commentLines, int printDirection = 0, int codetagCount = 0)
    {
        return $"{Signature}{hardblank} {height} {baseline} {maxLength} {OldLayoutFullWidth} " +
               $"{commentLines} {printDirection} {FullLayoutFullWidth} {codetagCount}";
    }

    /// <summary>Comment lines after the header (font name, source, generator).</summary>
    public static string[] Comments(string fontName, string sourcePath, string moduleVersion)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        return
        [
            $"{fontName} FIGlet font",
            $"Converted from: {sourceFileName}",
            $"Generated: {timestamp}",
            $"Generator: ttf2flf v{moduleVersion} (https://github.com/jakehildreth/ttf2flf)",
        ];
    }

    /// <summary>Pads rows to targetWidth and appends endmarks (@ per row, @@ on last).</summary>
    public static string[] FormatCharacter(
        string[] rows, int targetWidth = 0, char endmark = DefaultEndmark)
    {
        var formatted = new string[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (targetWidth > 0 && row.Length < targetWidth)
            {
                row = row.PadRight(targetWidth);
            }

            formatted[i] = i == rows.Length - 1
                ? $"{row}{endmark}{endmark}"
                : $"{row}{endmark}";
        }

        return formatted;
    }

    /// <summary>
    /// Builds and writes the complete FLF file (UTF-8 no BOM, LF endings).
    /// Returns the header line for reporting.
    /// </summary>
    public static string WriteFile(
        string outputFile,
        IReadOnlyDictionary<int, string[]> flfCharacters,
        int height,
        char hardblank,
        string fontName,
        string sourcePath,
        string moduleVersion)
    {
        var baseline = height - 1;

        // MaxLength = longest serialized glyph line (endmarks included), in UTF-8 bytes:
        // byte-oriented readers such as FIGlet size their line buffers from it, and the
        // block characters are 3 bytes each.
        var maxLength = 0;
        foreach (var rows in flfCharacters.Values)
        {
            foreach (var row in rows)
            {
                maxLength = Math.Max(maxLength, Utf8NoBom.GetByteCount(row));
            }
        }

        var comments = Comments(fontName, sourcePath, moduleVersion);
        var header = Header(hardblank, height, baseline, maxLength, comments.Length);

        var content = new StringBuilder();

        content.Append(header);
        content.Append('\n');

        foreach (var comment in comments)
        {
            content.Append(comment);
            content.Append('\n');
        }

        foreach (var charCode in RequiredCharacters)
        {
            foreach (var row in flfCharacters[charCode])
            {
                content.Append(row);
                content.Append('\n');
            }
        }

        // UTF-8, no BOM. LF endings by construction.
        File.WriteAllText(outputFile, content.ToString(), Utf8NoBom);

        return header;
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
}
