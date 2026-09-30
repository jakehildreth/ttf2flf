using System.Text;

namespace flfview;

/// <summary>
/// Renders text with an <see cref="FlfFont"/> by concatenating glyph rows
/// side by side (FullWidth layout, no smushing), separated by one space.
/// </summary>
public static class Renderer
{
    // No separator: glyphs carry their own padding, so concatenating rows verbatim
    // matches figlet's FullWidth output byte-for-byte.

    /// <summary>Renders <paramref name="text"/> into <c>font.Height</c> lines.</summary>
    public static string[] Render(FlfFont font, string text)
    {
        var glyphs = new List<string[]>(text.Length);
        foreach (char c in text)
            glyphs.Add(font.GetGlyph(c));

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
