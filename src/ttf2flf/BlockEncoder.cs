using System.Text;

namespace ttf2flf;

/// <summary>Converts pixel brightness to anti-aliased block characters (░▒▓█).</summary>
public static class BlockEncoder
{
    public static string[] Encode(double[][] pixels)
    {
        var rows = new List<string>(pixels.Length);

        foreach (var pixelRow in pixels)
        {
            var rowBuilder = new StringBuilder(pixelRow.Length);

            foreach (var brightness in pixelRow)
            {
                var c = brightness switch
                {
                    < 0.125 => ' ',   // empty
                    < 0.375 => '░',   // U+2591 light shade
                    < 0.625 => '▒',   // U+2592 medium shade
                    < 0.875 => '▓',   // U+2593 dark shade
                    _ => '█',         // U+2588 full block
                };

                rowBuilder.Append(c);
            }

            rows.Add(rowBuilder.ToString());
        }

        return rows.ToArray();
    }
}
