using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.Fonts;

namespace ttf2flf;

/// <summary>Detects the native pixel grid of a pixel-style font and the render size that reproduces it 1:1.</summary>
public static class FontGridDetector
{
    private static readonly int[] TestSizes = [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40];

    public readonly record struct GridSize(int Height, int Width);

    /// <summary>One calibrated render-size entry keyed by font file name.</summary>
    private sealed class RenderSizeEntry
    {
        public int Grid { get; set; }
        public int RenderSize { get; set; }
    }

    /// <summary>
    /// Computes the pixel size to RENDER at. These outline fonts use an em square far larger
    /// than the design grid, so at "size = grid" the design pixel is sub-pixel and the
    /// rasterizer shrinks/distorts strokes. The true design grid only emerges near
    /// grid × UnitsPerEm/capHeight. Authoritative per-font values live in render_sizes.json
    /// (PNG-calibrated) next to the font when present; otherwise the size is computed and
    /// nudged by stroke-singleness.
    /// </summary>
    public static int DetectRenderSize(
        FontFamily fontFamily, int grid, string sourcePath, Action<string>? verbose = null)
    {
        var fileName = Path.GetFileName(sourcePath);

        // 1. Baked PNG-calibrated table (authoritative for the bundled corpus) wins.
        if (CalibratedRenderSizes.ByFileName.TryGetValue(fileName, out var calibrated))
        {
            verbose?.Invoke($"[+] Render size {calibrated} from calibration table (grid {grid})");
            return calibrated;
        }

        // 2. render_sizes.json next to the font (runtime override).
        var table = LoadRenderSizeTable(Path.GetDirectoryName(sourcePath));
        if (table is not null && table.TryGetValue(fileName, out var entry) && entry.RenderSize > 0)
        {
            verbose?.Invoke($"[+] Render size {entry.RenderSize} from render_sizes.json (grid {grid})");
            return entry.RenderSize;
        }

        // 2. Compute from scale, then nudge by stroke-singleness.
        var probeFont = fontFamily.CreateFont(1000);
        var unitsPerEm = probeFont.FontMetrics.UnitsPerEm;
        var capBounds = TextMeasurer.MeasureBounds("H", new TextOptions(probeFont));
        var capUnits = capBounds.Height / 1000.0 * unitsPerEm;
        if (capUnits < 1)
        {
            verbose?.Invoke("[+] capUnits degenerate; rendering at grid");
            return grid;
        }

        var scale = unitsPerEm / capUnits;
        if (scale < 1.5)
        {
            // Native pixel grid (em square == design grid): render 1:1 at the grid.
            verbose?.Invoke($"[+] Render size {grid} (native grid; scale {scale:F3} ~ 1)");
            return grid;
        }

        var baseSize = (int)Math.Round(grid * scale);
        if (baseSize < 1) baseSize = 1;

        // Nudge: prefer a size whose strokes are single-width (not doubled).
        var chosen = baseSize;
        foreach (var candidate in new[] { baseSize, baseSize + 1, baseSize - 1, baseSize + 2 })
        {
            if (candidate < 1) continue;
            if (IsSingleStroke(fontFamily, candidate))
            {
                chosen = candidate;
                break;
            }
        }

        verbose?.Invoke(
            $"[+] Render size {chosen} (grid {grid}, upem {unitsPerEm}, capUnits {capUnits:F1}, scale {scale:F3}, base {baseSize})");
        return chosen;
    }

    /// <summary>True when the probe glyph's thinnest stroke renders as a single pixel.</summary>
    private static bool IsSingleStroke(FontFamily fontFamily, int size)
    {
        var font = fontFamily.CreateFont(size);
        var ok = MeasurePixelPerfectRuns(font, 'H', out var minRun);
        return ok && minRun == 1;
    }

    /// <summary>
    /// Renders a character tight and returns the minimum on-run length across each individual
    /// scanline and column (the thinnest stroke). Full-span runs (crossbars) are excluded.
    /// </summary>
    private static bool MeasurePixelPerfectRuns(Font font, char character, out int minRun)
    {
        minRun = 0;
        var g = GlyphRenderer.MeasurePixelPerfect(font, character);
        if (g.IsEmpty || g.Span == 0) return false;

        var pixels = g.Pixels;
        var height = g.Span;
        var width = g.Width;
        var min = int.MaxValue;

        for (var y = 0; y < height; y++)
        {
            var row = pixels[y];
            var x = 0;
            while (x < width)
            {
                if (row[x] >= 0.5)
                {
                    var len = 0;
                    while (x < width && row[x] >= 0.5) { len++; x++; }
                    if (len < width && len < min) min = len;
                }
                else x++;
            }
        }

        for (var x = 0; x < width; x++)
        {
            var y = 0;
            while (y < height)
            {
                if (pixels[y][x] >= 0.5)
                {
                    var len = 0;
                    while (y < height && pixels[y][x] >= 0.5) { len++; y++; }
                    if (len < height && len < min) min = len;
                }
                else y++;
            }
        }

        if (min == int.MaxValue) return false;
        minRun = min;
        return true;
    }

    private static Dictionary<string, RenderSizeEntry>? LoadRenderSizeTable(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        var path = Path.Combine(directory, "render_sizes.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, RenderSizeEntry>>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Smallest design grid accepted from a font-name number.</summary>
    public const int MinNameGrid = 5;

    /// <summary>Largest design grid accepted from a font-name number (matches the --height limit).</summary>
    public const int MaxNameGrid = 64;

    /// <summary>
    /// Returns the design grid hinted by a font family name: the last numeric token in
    /// <see cref="MinNameGrid"/>..<see cref="MaxNameGrid"/>. Tokens outside that range
    /// (model or version numbers such as "3270" or "256") are ignored. Returns null
    /// when no token qualifies.
    /// </summary>
    public static int? GridHintFromName(string fontName)
    {
        int? hint = null;
        foreach (Match match in Regex.Matches(fontName, @"\d+"))
        {
            if (int.TryParse(match.Value, out var value) && value is >= MinNameGrid and <= MaxNameGrid)
            {
                hint = value;
            }
        }

        return hint;
    }

    /// <summary>
    /// Detects the native design grid. Priority: explicit height (width-only detection),
    /// then UnitsPerPixel, then name hint (<see cref="GridHintFromName"/>), then
    /// stroke-width alignment, then default 8.
    /// </summary>
    public static GridSize Detect(
        FontFamily fontFamily, int explicitHeight = 0, int unitsPerPixel = 0,
        Action<string>? verbose = null)
    {
        // UnitsPerPixel: derive height directly from metrics.
        if (unitsPerPixel > 0)
        {
            var probe = fontFamily.CreateFont(12);
            var unitsPerEm = probe.FontMetrics.UnitsPerEm;
            var grid = Math.Max(1, (int)Math.Round(unitsPerEm / (double)unitsPerPixel));
            var width = GetWidth(fontFamily.CreateFont(grid));
            verbose?.Invoke(
                $"[+] UnitsPerPixel={unitsPerPixel} (UnitsPerEm={unitsPerEm}) -> height {grid}, width {width}");
            return new GridSize(grid, width);
        }

        // Explicit height: detect width only.
        if (explicitHeight > 0)
        {
            var width = GetWidth(fontFamily.CreateFont(explicitHeight));
            verbose?.Invoke($"[+] Using specified height {explicitHeight}, detected width {width}");
            return new GridSize(explicitHeight, width);
        }

        var fontName = fontFamily.Name;

        // 1. Name hint: the last plausible grid number in the family name.
        if (GridHintFromName(fontName) is { } hint)
        {
            var width = GetWidth(fontFamily.CreateFont(hint));
            verbose?.Invoke($"[+] Detected grid {hint} from font name (width {width})");
            return new GridSize(hint, width);
        }

        // 2. Stroke-width alignment: find the fundamental period for thick-stroke fonts.
        const char testChar = 'H'; // strong horizontal + vertical strokes
        var clean = new Dictionary<int, double>();

        foreach (var size in TestSizes)
        {
            try
            {
                var font = fontFamily.CreateFont(size);
                var glyph = GlyphRenderer.MeasurePixelPerfect(font, testChar);

                // Collect horizontal on-run lengths.
                var runs = new List<int>();
                foreach (var row in glyph.Pixels)
                {
                    var x = 0;
                    var w = row.Length;
                    while (x < w)
                    {
                        if (row[x] >= 0.5)
                        {
                            var len = 0;
                            while (x < w && row[x] >= 0.5) { len++; x++; }
                            if (len > 0) runs.Add(len);
                        }
                        else
                        {
                            x++;
                        }
                    }
                }

                if (runs.Count < 3)
                {
                    clean[size] = 0;
                    continue;
                }

                var min = runs.Min();
                if (min < 1)
                {
                    clean[size] = 0;
                    continue;
                }

                var aligned = 0;
                foreach (var r in runs)
                {
                    var k = (int)Math.Round(r / (double)min);
                    if (k >= 1 && Math.Abs(r - (k * min)) <= 0.5) aligned++;
                }

                clean[size] = aligned / (double)runs.Count;
                verbose?.Invoke($"[+]   size {size} : aligned {Math.Round(clean[size], 2)}");
            }
            catch
            {
                clean[size] = 0;
            }
        }

        // Discrimination: a thick-stroke font has at least one off-grid size that renders dirty.
        var hasDiscrimination = TestSizes.Count(size => clean[size] < 0.5) >= 1;

        if (hasDiscrimination)
        {
            foreach (var size in TestSizes)
            {
                if (clean[size] < 0.85) continue;
                var multiples = TestSizes.Where(s => s % size == 0).ToList();
                var cleanMultiples = multiples.Where(s => clean[s] >= 0.85).ToList();
                if (multiples.Count >= 2 && cleanMultiples.Count == multiples.Count)
                {
                    var width = GetWidth(fontFamily.CreateFont(size));
                    verbose?.Invoke($"[+] Detected grid {size} via stroke alignment (width {width})");
                    return new GridSize(size, width);
                }
            }
        }

        // 3. No signal (thin-stroke or scalable font): default 8.
        var defaultWidth = GetWidth(fontFamily.CreateFont(8));
        verbose?.Invoke("[+] No grid signal; defaulting to 8");
        return new GridSize(8, defaultWidth);
    }

    /// <summary>Advance width of the widest probe glyph (M, W, @, m) at this size.</summary>
    public static int GetWidth(Font font)
    {
        var maxWidth = 1;
        foreach (var probe in new[] { 'M', 'W', '@', 'm' })
        {
            try
            {
                var opts = new TextOptions(font);
                var adv = TextMeasurer.MeasureAdvance(probe.ToString(), opts);
                var w = Math.Max(1, (int)Math.Ceiling(adv.Width));
                if (w > maxWidth) maxWidth = w;
            }
            catch
            {
                // Probe failure leaves width unchanged (mirrors PS verbose-only handling).
            }
        }

        return maxWidth;
    }
}
