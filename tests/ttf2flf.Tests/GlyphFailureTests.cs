using SixLabors.Fonts;

namespace ttf2flf.Tests;

public class GlyphFailureTests
{
    /// <summary>Real rasterizer that throws for selected characters.</summary>
    private sealed class FailingRasterizer(Func<char, bool> fails) : IGlyphRasterizer
    {
        public GlyphRenderer.MeasuredGlyph MeasurePixelPerfect(Font font, char character) =>
            fails(character)
                ? throw new InvalidOperationException("simulated render failure")
                : GlyphRasterizer.Default.MeasurePixelPerfect(font, character);

        public Bitmap RenderAntiAliased(Font font, char character, int height, int maxWidth) =>
            fails(character)
                ? throw new InvalidOperationException("simulated render failure")
                : GlyphRasterizer.Default.RenderAntiAliased(font, character, height, maxWidth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllGlyphsFailing_ReturnsNonzero_AndWritesNoFile(bool antiAliased)
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");
        var target = temp.File("font.flf");
        string[] args = antiAliased ? [input, "--aa", "-o", target] : [input, "-o", target];

        var (exitCode, output, error) = TestSupport.RunConverterWith(new FailingRasterizer(_ => true), args);

        Assert.Equal(1, exitCode);
        Assert.Contains("failed to render", error);
        Assert.False(File.Exists(target));
        Assert.Equal("", output);
    }

    [Fact]
    public void CoreGlyphFailure_ReturnsNonzero()
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");
        var target = temp.File("font.flf");

        var (exitCode, _, error) = TestSupport.RunConverterWith(
            new FailingRasterizer(c => c == 'A'), input, "-o", target);

        Assert.Equal(1, exitCode);
        Assert.Contains("core glyph", error);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void IsolatedNonCoreFailure_WarnsAndWritesBlankGlyph()
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");
        var target = temp.File("font.flf");

        var (exitCode, _, error) = TestSupport.RunConverterWith(
            new FailingRasterizer(c => c == '~'), input, "-o", target);

        Assert.True(exitCode == 0, error);
        Assert.Contains("[!] Failed to render character 126 (~)", error);
        var height = TestSupport.HeaderFields(target)[0];
        var tildeIndex = Array.IndexOf(FlfWriter.RequiredCharacters, '~');
        var tildeRows = TestSupport.GlyphLines(target).Skip(tildeIndex * height).Take(height);
        Assert.All(tildeRows, row => Assert.Matches("^ *@@?$", row));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void NonCoreFailures_AreToleratedUpToTenPercent(int failedCount, bool acceptable)
    {
        var nonCore = FlfWriter.RequiredCharacters.Where(c => !GlyphFailurePolicy.IsCore(c)).Take(failedCount).ToList();

        var failure = GlyphFailurePolicy.Evaluate(nonCore, FlfWriter.RequiredCharacters.Length);

        Assert.Equal(acceptable, failure is null);
    }

    [Theory]
    [InlineData(0f, 0.0, 0.0)]
    [InlineData(float.NaN, double.NaN, double.NaN)]
    [InlineData(12f, -5.0, -1.0)]
    [InlineData(12f, double.PositiveInfinity, double.PositiveInfinity)]
    [InlineData(-3f, 0.4, 0.2)]
    public void DegenerateMetrics_ProduceValidCanvas(float fontSize, double lineHeight, double advance)
    {
        var canvas = GlyphRenderer.ComputeCanvas(fontSize, lineHeight, advance);

        Assert.True(canvas.ImageWidth > 0);
        Assert.True(canvas.ImageHeight > 0);
        Assert.True(double.IsFinite(canvas.LineHeight) && canvas.LineHeight >= 1);
        Assert.True(canvas.CharWidth >= 1);
        Assert.True(canvas.ImageWidth > canvas.CharWidth && canvas.ImageHeight > canvas.LineHeight);
    }

    [Fact]
    public void Canvas_IsSizedFromBaselineExtent_DeepAscenderKeepsTopRow()
    {
        // 'A' sits on the baseline (span 1, bottom = global). 'B' is a deep ascender:
        // span 24 but bottom 6 px above the baseline, like U+00C4 in More 15.
        var glyphs = new Dictionary<int, GlyphRenderer.MeasuredGlyph>
        {
            ['A'] = new([new double[] { 1, 1 }], 2, 1, 31, 2, false),
            ['B'] = new(Enumerable.Range(0, 24).Select(_ => new double[] { 1, 1 }).ToArray(), 2, 24, 25, 2, false),
        };

        var canvasPixels = GlyphRenderer.RequiredCanvasPixels(glyphs.Values);
        var globalMaxBottom = 31;

        // The deep ascender needs span + (global - bottom) = 24 + 6 = 30 px.
        Assert.Equal(30, canvasPixels);

        var placed = GlyphRenderer.PlaceOnCanvas(glyphs['B'], canvasPixels, globalMaxBottom, rightSpacer: 0);
        var contentRows = Enumerable.Range(0, placed.Height)
            .Where(y => placed.Pixels[y].Any(v => v > 0))
            .ToList();

        // Every source row landed, starting at row 0 (no silent down-shift).
        Assert.Equal(24, contentRows.Count);
        Assert.Equal(0, contentRows.First());
        Assert.Equal(23, contentRows.Last()); // bottom edge: (30-1) - (31-25) = 23
    }

    [Fact]
    public void Canvas_AllEmptyGlyphs_IsOneRow()
    {
        var glyphs = new[] { new GlyphRenderer.MeasuredGlyph([], 1, 0, 0, 1, true) };

        Assert.Equal(2, GlyphRenderer.RequiredCanvasPixels(glyphs));
    }
}
