using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace ttf2flf;

/// <summary>Detects symbol/emoji fonts unlikely to convert cleanly (warnings only).</summary>
public static class FontCompatibility
{
    private static readonly string[] SymbolPatterns =
        ["wingding", "webding", "symbol", "dingbat", "icon", "emoji"];

    public static (bool IsCompatible, List<string> Warnings, string FontName) Check(
        FontFamily fontFamily,
        string path)
    {
        _ = path;
        var warnings = new List<string>();
        var isCompatible = true;
        var fontName = fontFamily.Name;

        foreach (var pattern in SymbolPatterns)
        {
            if (fontName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"[!] Font '{fontName}' appears to be a symbol/icon font. " +
                    "Output may not represent text correctly.");
                break;
            }
        }

        try
        {
            var font = fontFamily.CreateFont(12);
            var testChars = new[] { (int)'A', (int)'a', (int)'0' };
            var missingGlyphs = 0;

            foreach (var charCode in testChars)
            {
                var codePoint = new CodePoint(charCode);
                if (!font.FontMetrics.TryGetGlyphMetrics(
                        codePoint,
                        TextAttributes.None,
                        TextDecorations.None,
                        SixLabors.Fonts.LayoutMode.HorizontalTopBottom,
                        ColorFontSupport.None,
                        out _))
                {
                    missingGlyphs++;
                }
            }

            if (missingGlyphs == testChars.Length)
            {
                warnings.Add(
                    $"[!] Font '{fontName}' appears to have no standard ASCII glyphs. " +
                    "This may be a symbol font.");
                isCompatible = false;
            }
            else if (missingGlyphs > 0)
            {
                warnings.Add($"[i] Font '{fontName}' is missing some basic ASCII glyphs.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"[!] Could not fully analyze font '{fontName}': {ex.Message}");
        }

        return (isCompatible, warnings, fontName);
    }
}
