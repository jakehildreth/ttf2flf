using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ttf2flf;

/// <summary>Renders a glyph to a pixel bitmap: tight bounding box, baseline/bottom anchoring.</summary>
public static class GlyphRenderer
{
    /// <summary>
    /// A pixel-perfect render of one glyph: the tight-cropped pixel content plus the
    /// information needed to position it on a baseline-aligned shared canvas.
    /// </summary>
    /// <param name="Pixels">Tight bounding-box pixel rows (brightness 0..1).</param>
    /// <param name="Width">Tight bounding-box width in pixels.</param>
    /// <param name="Span">Vertical pixel span (last content row - first content row + 1).</param>
    /// <param name="ContentBottomFromOrigin">
    /// Origin-relative image row of the glyph's last content pixel (its bottom edge). Used
    /// to bottom-align all glyphs on the shared canvas: the global max bottom maps to the
    /// canvas's last pixel row.
    /// </param>
    /// <param name="AdvanceWidth">Advance width in pixels; used for empty glyphs (space).</param>
    /// <param name="IsEmpty">True when the glyph rendered no pixels (e.g. space).</param>
    public readonly record struct MeasuredGlyph(
        double[][] Pixels,
        int Width,
        int Span,
        int ContentBottomFromOrigin,
        int AdvanceWidth,
        bool IsEmpty);

    /// <summary>Image geometry for rendering one glyph.</summary>
    /// <param name="LineHeight">Ascender + |descender| in pixels (at least 1).</param>
    /// <param name="CharWidth">Advance width in whole pixels (at least 1).</param>
    /// <param name="Padding">Margin around the glyph origin (at least 1).</param>
    /// <param name="ImageWidth">Canvas width in pixels (positive).</param>
    /// <param name="ImageHeight">Canvas height in pixels (positive).</param>
    public readonly record struct CanvasLayout(
        double LineHeight, int CharWidth, int Padding, int ImageWidth, int ImageHeight);

    /// <summary>
    /// Renders a glyph at native resolution and measures its tight vertical/horizontal
    /// bounding box and baseline position. Nothing is padded to the line-height cell;
    /// the caller decides the shared canvas height from the tallest span.
    /// </summary>
    public static MeasuredGlyph MeasurePixelPerfect(
        Font font, char character, int width = 0, int maxWidth = 0)
    {
        using var context = RenderToImage(font, character, out _, out var charWidth);
        var image = context.Image;

        // Tight bounding box of the rendered (non-black) pixels.
        var minX = image.Width;
        var maxX = -1;
        var minY = image.Height;
        var maxY = -1;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.R > 0 || pixel.G > 0 || pixel.B > 0)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        });

        var advanceWidth = Math.Max(1, charWidth);

        if (maxY < 0)
        {
            // Empty glyph (space): no content; width comes from the advance.
            return new MeasuredGlyph(
                [],
                advanceWidth,
                0,
                0,
                advanceWidth,
                true);
        }

        var outWidth = maxX - minX + 1;
        var span = maxY - minY + 1;

        // Bottom anchor: the glyph's bottom content edge relative to the render origin
        // (top of the em square). All glyphs share the origin, so this offset lets the
        // caller bottom-align them (global max bottom -> canvas's last pixel row) while
        // preserving each glyph's true vertical relationship (x-height, caps, descenders).
        var contentBottomFromOrigin = maxY - context.Padding;

        // Output width: explicit width wins, else monospace pad, else tight bbox.
        int targetWidth;
        if (width > 0)
        {
            targetWidth = width;
        }
        else if (maxWidth > 0 && outWidth < maxWidth)
        {
            targetWidth = maxWidth;
        }
        else
        {
            targetWidth = outWidth;
        }

        var pixels = new double[span][];
        var copyWidth = Math.Min(outWidth, targetWidth);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < span; y++)
            {
                var row = new double[targetWidth];
                var source = accessor.GetRowSpan(minY + y).Slice(minX, copyWidth);
                for (var x = 0; x < source.Length; x++)
                {
                    var pixel = source[x];
                    row[x] = (pixel.R + pixel.G + pixel.B) / (255.0 * 3);
                }

                // x >= outWidth stays 0.0 (monospace pad).
                pixels[y] = row;
            }
        });

        return new MeasuredGlyph(
            pixels,
            targetWidth,
            span,
            contentBottomFromOrigin,
            advanceWidth,
            false);
    }

    /// <summary>
    /// The canvas height in pixels that fits every measured glyph at its baseline-relative
    /// offset. A glyph needs span + (globalMaxBottom - contentBottom) pixels: its height
    /// plus the room above the shared baseline. Sizing from the tallest span alone clamps a
    /// deep ascender (low bottom, tall span) to the canvas top and shifts it down.
    /// Returns at least 2 (one half-block row), or 2 when every glyph is empty.
    /// </summary>
    public static int RequiredCanvasPixels(IReadOnlyCollection<MeasuredGlyph> glyphs)
    {
        var nonEmpty = glyphs.Where(g => !g.IsEmpty).ToList();
        if (nonEmpty.Count == 0)
            return 2;

        var globalMaxBottom = nonEmpty.Max(g => g.ContentBottomFromOrigin);
        var maxExtent = nonEmpty.Max(g => g.Span + (globalMaxBottom - g.ContentBottomFromOrigin));
        if (maxExtent < 1) maxExtent = 1;
        return (int)Math.Ceiling(maxExtent / 2.0) * 2;
    }

    /// <summary>
    /// Places a measured glyph onto the shared pixel canvas (height =
    /// outputHeight*2). Bottom-aligned: the global lowest content bottom
    /// (<paramref name="globalMaxBottom"/>) maps to the canvas's last pixel row, and every
    /// other glyph sits at its own bottom-relative offset so x-height, caps, and descenders
    /// keep their true relationship. Internal gaps are preserved verbatim. No glyph is ever
    /// clipped; the canvas is sized to the tallest glyph.
    /// </summary>
    public static Bitmap PlaceOnCanvas(
        in MeasuredGlyph glyph, int canvasPixels, int globalMaxBottom, int maxWidth = 0, int rightSpacer = 0)
    {
        if (glyph.IsEmpty)
        {
            var emptyWidth = glyph.AdvanceWidth; // space keeps its own advance; no spacer
            if (maxWidth > 0 && emptyWidth < maxWidth) emptyWidth = maxWidth;
            return new Bitmap
            {
                Width = emptyWidth,
                Height = canvasPixels,
                Pixels = ZeroPixels(canvasPixels, emptyWidth),
            };
        }

        // The glyph's bottom edge sits (globalMaxBottom - ContentBottomFromOrigin) rows above
        // the canvas bottom.
        var bottomRow = (canvasPixels - 1) - (globalMaxBottom - glyph.ContentBottomFromOrigin);
        var topRow = bottomRow - (glyph.Span - 1);

        // Never clip: if the span would run off the top, shift down just enough to fit.
        if (topRow < 0)
        {
            topRow = 0;
        }

        var width = glyph.Width + rightSpacer; // 1px right spacer between letters
        if (maxWidth > 0 && width < maxWidth) width = maxWidth;

        var pixels = ZeroPixels(canvasPixels, width);
        for (var y = 0; y < glyph.Span; y++)
        {
            var destRow = topRow + y;
            if (destRow < 0 || destRow >= canvasPixels) continue;
            var srcRow = glyph.Pixels[y];
            var destPixels = pixels[destRow];
            var copyWidth = Math.Min(srcRow.Length, width);
            for (var x = 0; x < copyWidth; x++)
            {
                destPixels[x] = srcRow[x];
            }
        }

        return new Bitmap { Width = width, Height = canvasPixels, Pixels = pixels };
    }

    /// <summary>
    /// Standard anti-aliased branch: renders large, then block-averages down to
    /// <paramref name="height"/> rows (port of the non-PixelPerfect branch).
    /// </summary>
    public static Bitmap RenderAntiAliased(Font font, char character, int height, int maxWidth = 0)
    {
        using var context = RenderToImage(font, character, out var lineHeight, out var charWidth);
        var image = context.Image;
        var imageWidth = image.Width;
        var imageHeight = image.Height;
        var padding = context.Padding;

        // Sampling region: exclude padding, use full line height.
        var srcX = padding;
        var srcY = padding;
        var srcWidth = charWidth;
        var srcHeight = (int)Math.Ceiling(lineHeight);

        var outHeight = height;
        var outWidth = Math.Max(1, (int)Math.Ceiling(charWidth * height / lineHeight));

        if (maxWidth > 0)
        {
            outWidth = maxWidth;
        }

        var pixels = new double[outHeight][];
        image.ProcessPixelRows(accessor =>
        {
            for (var outY = 0; outY < outHeight; outY++)
            {
                var row = new double[outWidth];

                var srcYStart = srcY + (outY * srcHeight / (double)outHeight);
                var srcYEnd = srcY + ((outY + 1) * srcHeight / (double)outHeight);
                var syFirst = Math.Max(0, (int)Math.Floor(srcYStart));
                var syEnd = Math.Min(imageHeight, (int)Math.Ceiling(srcYEnd));

                for (var outX = 0; outX < outWidth; outX++)
                {
                    var srcXStart = srcX + (outX * srcWidth / (double)outWidth);
                    var srcXEnd = srcX + ((outX + 1) * srcWidth / (double)outWidth);
                    var sxFirst = Math.Max(0, (int)Math.Floor(srcXStart));
                    var sxEnd = Math.Min(imageWidth, (int)Math.Ceiling(srcXEnd));

                    var totalBrightness = 0.0;
                    var sampleCount = 0;

                    for (var sy = syFirst; sy < syEnd; sy++)
                    {
                        var sourceRow = accessor.GetRowSpan(sy);
                        for (var sx = sxFirst; sx < sxEnd; sx++)
                        {
                            var pixel = sourceRow[sx];
                            totalBrightness += (pixel.R + pixel.G + pixel.B) / (255.0 * 3);
                            sampleCount++;
                        }
                    }

                    row[outX] = sampleCount > 0 ? totalBrightness / sampleCount : 0.0;
                }

                pixels[outY] = row;
            }
        });

        return new Bitmap { Width = outWidth, Height = outHeight, Pixels = pixels };
    }

    /// <summary>
    /// Computes the render canvas. Degenerate metrics (zero, negative, NaN, or infinite
    /// size, line height, or advance, e.g. from a zero UnitsPerEm) are clamped to valid
    /// minimums so the image always has positive dimensions: size falls back to 1, line
    /// height to the font size, advance to 1.
    /// </summary>
    public static CanvasLayout ComputeCanvas(float fontSize, double lineHeight, double advanceWidth)
    {
        var size = float.IsFinite(fontSize) && fontSize >= 1 ? fontSize : 1f;
        if (!double.IsFinite(lineHeight) || lineHeight < 1)
        {
            lineHeight = size;
        }

        var charWidth = double.IsFinite(advanceWidth) && advanceWidth > 0
            ? Math.Max(1, (int)Math.Ceiling(advanceWidth))
            : 1;

        var padding = Math.Max(1, (int)Math.Ceiling(size * 0.25));
        // Extra vertical room beyond the line height so nothing clips during measurement;
        // the caller crops to the tight bbox.
        var extra = (int)Math.Ceiling(size * 0.5);
        return new CanvasLayout(
            lineHeight,
            charWidth,
            padding,
            charWidth + (padding * 2),
            (int)Math.Ceiling(lineHeight) + (padding * 2) + extra);
    }

    /// <summary>
    /// Renders a character white-on-black with generous padding; returns the image plus
    /// the metrics the extraction passes need. The canvas has extra room below so
    /// descenders deeper than the declared metrics are never clipped during measurement.
    /// </summary>
    private static RenderContext RenderToImage(
        Font font, char character, out double lineHeight, out int charWidth)
    {
        var charString = character.ToString();

        var metrics = font.FontMetrics;
        var scale = font.Size / metrics.UnitsPerEm;
        var ascender = metrics.HorizontalMetrics.Ascender * scale;
        var descender = Math.Abs(metrics.HorizontalMetrics.Descender * scale);

        var advanceWidth = TextMeasurer.MeasureAdvance(charString, new TextOptions(font));
        var layout = ComputeCanvas(font.Size, ascender + descender, advanceWidth.Width);
        lineHeight = layout.LineHeight;
        charWidth = layout.CharWidth;

        var image = new Image<Rgba32>(layout.ImageWidth, layout.ImageHeight, new Rgba32(0, 0, 0, 255));

        var renderOptions = new RichTextOptions(font)
        {
            Origin = new PointF(layout.Padding, layout.Padding),
        };

        image.Mutate(ctx => ctx.DrawText(renderOptions, charString, Brushes.Solid(Color.White)));

        return new RenderContext(image, layout.Padding);
    }

    private static double[][] ZeroPixels(int height, int width)
    {
        var pixels = new double[height][];
        for (var y = 0; y < height; y++)
        {
            pixels[y] = new double[width];
        }

        return pixels;
    }

    private sealed class RenderContext(Image<Rgba32> image, int padding) : IDisposable
    {
        public Image<Rgba32> Image { get; } = image;
        public int Padding { get; } = padding;

        public void Dispose() => Image.Dispose();
    }
}
