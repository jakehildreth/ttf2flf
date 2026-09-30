namespace ttf2flf;

/// <summary>Row-major brightness grid (0..1) for one rendered glyph.</summary>
public sealed class Bitmap
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>[row][column]; every row has <see cref="Width"/> entries.</summary>
    public required double[][] Pixels { get; init; }
}
