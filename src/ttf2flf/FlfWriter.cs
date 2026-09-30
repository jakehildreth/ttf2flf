using System.Text;

namespace ttf2flf;

/// <summary>
/// Port of Private/New-FLFHeader.ps1 + New-FLFComment.ps1 + Format-FLFCharacter.ps1 and
/// the character-ordering/write logic of Public/ConvertTo-FLF.ps1.
/// </summary>
public static class FlfWriter
{
    public const string Signature = "flf2a";
    public const char DefaultEndmark = '@';

    /// <summary>ASCII 32-126, then German 196, 214, 220, 228, 246, 252, 223.</summary>
    public static readonly int[] RequiredCharacters =
        Enumerable.Range(32, 95).Concat([196, 214, 220, 228, 246, 252, 223]).ToArray();

    private static readonly Dictionary<LayoutMode, (int OldLayout, int FullLayout)> LayoutModes = new()
    {
        [LayoutMode.FullWidth] = (-1, 0),
        [LayoutMode.Kerned] = (0, 64),
        [LayoutMode.Smushed] = (63, 16191),
    };

    /// <summary>flf2a$ Height Baseline MaxLen OldLayout CommentLines PrintDir FullLayout CodetagCount</summary>
    public static string Header(
        char hardblank, int height, int baseline, int maxLength, LayoutMode layout,
        int commentLines, int printDirection = 0, int codetagCount = 0)
    {
        var (oldLayout, fullLayout) = LayoutModes[layout];
        return $"{Signature}{hardblank} {height} {baseline} {maxLength} {oldLayout} " +
               $"{commentLines} {printDirection} {fullLayout} {codetagCount}";
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
        LayoutMode layout,
        string fontName,
        string sourcePath,
        string moduleVersion)
    {
        var baseline = height - 1;

        // MaxLength = longest row including endmarks + 2 (PS parity: rows carry endmarks).
        var actualMaxLength = 0;
        foreach (var rows in flfCharacters.Values)
        {
            foreach (var row in rows)
            {
                if (row.Length > actualMaxLength)
                {
                    actualMaxLength = row.Length;
                }
            }
        }

        var comments = Comments(fontName, sourcePath, moduleVersion);
        var header = Header(
            hardblank, height, baseline, actualMaxLength + 2, layout, comments.Length);

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
        File.WriteAllText(outputFile, content.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return header;
    }
}
