using System.Text;

namespace ttf2flf;

/// <summary>
/// Encodes pixels as half-block characters (▀▄█). Two pixel rows become one terminal
/// row (█ ▀ ▄ space). Odd pixel counts round UP: the dangling bottom pixel is off.
/// </summary>
public static class HalfBlockEncoder
{
    public const double DefaultThreshold = 0.5;

    /// <summary>Non-space characters <see cref="Encode"/> can emit.</summary>
    public const string GlyphCharacters = "█▀▄";

    public static string[] Encode(double[][] pixels, double threshold = DefaultThreshold)
    {
        var rows = new List<string>();
        var pixelHeight = pixels.Length;
        var pixelWidth = pixelHeight > 0 ? pixels[0].Length : 0;

        for (var y = 0; y < pixelHeight; y += 2)
        {
            var rowBuilder = new StringBuilder(pixelWidth);

            for (var x = 0; x < pixelWidth; x++)
            {
                var topOn = pixels[y][x] >= threshold;
                var bottomOn = (y + 1) < pixelHeight && pixels[y + 1][x] >= threshold;

                var c = (topOn, bottomOn) switch
                {
                    (true, true) => '█',   // U+2588 full block
                    (true, false) => '▀',  // U+2580 upper half
                    (false, true) => '▄',  // U+2584 lower half
                    _ => ' ',
                };

                rowBuilder.Append(c);
            }

            rows.Add(rowBuilder.ToString());
        }

        return rows.ToArray();
    }
}
