using System.Text.RegularExpressions;

namespace flfview;

/// <summary>Finds and loads the fonts flfview shows.</summary>
public static class FontLibrary
{
    /// <summary>
    /// Default font directories, relative to the application directory. The build and
    /// publish steps copy the bundled corpus there (fonts/verified, fonts/clean), so
    /// a copied or published flfview folder finds its fonts without a repository checkout.
    /// </summary>
    public static IReadOnlyList<(string Dir, string Tier)> DefaultDirectories(string applicationDirectory) =>
    [
        (Path.Combine(applicationDirectory, "fonts", "verified"), "verified"),
        (Path.Combine(applicationDirectory, "fonts", "clean"), "clean"),
    ];

    /// <summary>
    /// Loads every *.flf in the given directories (sorted, first name wins across
    /// directories). Missing directories and unreadable or unsupported fonts are
    /// reported to <paramref name="warnings"/> and skipped.
    /// </summary>
    public static List<FlfFont> Load(IEnumerable<(string Dir, string Tier)> fontDirs, TextWriter warnings)
    {
        var fonts = new List<FlfFont>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, tier) in fontDirs)
        {
            if (!Directory.Exists(dir))
            {
                warnings.WriteLine($"warning: font directory not found: {dir}");
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(dir, "*.flf").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!seenNames.Add(Path.GetFileNameWithoutExtension(path)))
                    continue; // dedupe by font name across dirs
                try
                {
                    var font = FlfFont.Load(path);
                    // h/p variants of verified families convert cleanly at the calibrated render
                    // size (16) and were confirmed visually -> verified.
                    font.Tier = FlfFont.ApproximateFonts.Contains(font.Name) ? "approximate"
                        : Regex.IsMatch(font.Name, @"\d[hp]$", RegexOptions.IgnoreCase) ? "verified"
                        : tier;
                    fonts.Add(font);
                }
                catch (Exception ex)
                {
                    warnings.WriteLine($"warning: skipping {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        return fonts;
    }
}
