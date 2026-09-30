using System.Text;

namespace flfview;

/// <summary>
/// Renders text with a FullWidth <see cref="FlfFont"/> by concatenating glyph rows
/// side by side. Glyphs carry their own padding, so concatenating rows verbatim
/// matches FIGlet's FullWidth output.
/// </summary>
public static class Renderer
{
    /// <summary>
    /// Renders <paramref name="text"/> into <c>font.Height</c> lines. Text is read as
    /// Unicode scalar values: a supplementary character (e.g. an emoji) is one glyph,
    /// and a missing one gets a single fallback glyph.
    /// </summary>
    public static string[] Render(FlfFont font, string text)
    {
        var glyphs = new List<string[]>(text.Length);
        foreach (var rune in text.EnumerateRunes())
            glyphs.Add(font.GetGlyph(rune));

        var lines = new string[font.Height];
        for (var row = 0; row < font.Height; row++)
        {
            var sb = new StringBuilder();
            for (var g = 0; g < glyphs.Count; g++)
            {
                sb.Append(glyphs[g][row]);
            }
            lines[row] = sb.ToString().TrimEnd();
        }
        return lines;
    }

    /// <summary>Renders and returns the lines as a single newline-joined string.</summary>
    public static string RenderToString(FlfFont font, string text) =>
        string.Join('\n', Render(font, text));
}
