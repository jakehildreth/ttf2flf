function Get-GlyphBitmap {
    <#
    .SYNOPSIS
        Renders a character glyph to a bitmap.
    .DESCRIPTION
        Uses SixLabors.ImageSharp to render a single character from a font
        to a bitmap image at the specified height. Returns pixel data for
        conversion to ASCII art.
    .PARAMETER Font
        The SixLabors.Fonts.Font object to use for rendering.
    .PARAMETER Character
        The character to render.
    .PARAMETER Height
        The target height in rows for the output ASCII art.
    .PARAMETER MaxWidth
        Maximum width for the rendered glyph (for monospace mode).
    .PARAMETER PixelPerfect
        When specified, renders at native pixel resolution without downsampling.
        Each pixel maps directly to brightness values. Used for bitmap fonts.
    .OUTPUTS
        [PSCustomObject] with Width, Height, and Pixels (2D brightness array).
    .NOTES
        This is an internal helper function.
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [SixLabors.Fonts.Font]$Font,

        [Parameter(Mandatory)]
        [char]$Character,

        [Parameter(Mandatory)]
        [int]$Height,

        [Parameter()]
        [int]$MaxWidth = 0,

        [Parameter()]
        [switch]$PixelPerfect
    )

    $charString = [string]$Character

    # Get font metrics to calculate proper dimensions
    $metrics = $Font.FontMetrics
    $unitsPerEm = $metrics.UnitsPerEm
    $scale = $Font.Size / $unitsPerEm

    # Calculate the full line height in pixels (ascender to descender)
    # Use horizontal metrics for vertical measurements (they're the same for most fonts)
    $ascender = $metrics.HorizontalMetrics.Ascender * $scale
    $descender = [Math]::Abs($metrics.HorizontalMetrics.Descender * $scale)
    $lineHeight = $ascender + $descender

    # Measure width of this specific character
    $textOptions = [SixLabors.Fonts.TextOptions]::new($Font)
    $bounds = [SixLabors.Fonts.TextMeasurer]::MeasureBounds($charString, $textOptions)
    $advanceWidth = [SixLabors.Fonts.TextMeasurer]::MeasureAdvance($charString, $textOptions)

    # Use advance width for character cell width
    $charWidth = [Math]::Max(1, [Math]::Ceiling($advanceWidth.Width))

    # Add generous padding for rendering
    $padding = [Math]::Ceiling($Font.Size * 0.25)
    $imageWidth = $charWidth + ($padding * 2)
    $imageHeight = [Math]::Ceiling($lineHeight) + ($padding * 2)

    # Create image with black background
    $blackPixel = [SixLabors.ImageSharp.PixelFormats.Rgba32]::new(0, 0, 0, 255)
    $image = [SixLabors.ImageSharp.Image[SixLabors.ImageSharp.PixelFormats.Rgba32]]::new($imageWidth, $imageHeight, $blackPixel)

    try {
        # White color for text
        $white = [SixLabors.ImageSharp.Color]::White

        # Create text options - origin is the baseline position
        # SixLabors draws text with origin at top-left of the em square,
        # so we just need padding offset
        $renderOptions = [SixLabors.ImageSharp.Drawing.Processing.RichTextOptions]::new($Font)
        $renderOptions.Origin = [SixLabors.ImageSharp.PointF]::new($padding, $padding)

        # Get brush for white text
        $brush = [SixLabors.ImageSharp.Drawing.Processing.Brushes]::Solid($white)

        # Draw text using Mutate pattern
        $action = [System.Action[SixLabors.ImageSharp.Processing.IImageProcessingContext]] {
            param($ctx)
            [SixLabors.ImageSharp.Drawing.Processing.DrawTextExtensions]::DrawText(
                $ctx,
                $renderOptions,
                $charString,
                $brush
            )
        }.GetNewClosure()

        [SixLabors.ImageSharp.Processing.ProcessingExtensions]::Mutate($image, $action)

        if ($PixelPerfect) {
            # Pixel-perfect mode: extract raw pixels without downsampling
            # Find the actual rendered bounds by scanning for non-black pixels
            $minX = $imageWidth
            $maxX = 0
            $minY = $imageHeight
            $maxY = 0

            for ($y = 0; $y -lt $imageHeight; $y++) {
                for ($x = 0; $x -lt $imageWidth; $x++) {
                    $pixel = $image[$x, $y]
                    if ($pixel.R -gt 0 -or $pixel.G -gt 0 -or $pixel.B -gt 0) {
                        if ($x -lt $minX) { $minX = $x }
                        if ($x -gt $maxX) { $maxX = $x }
                        if ($y -lt $minY) { $minY = $y }
                        if ($y -gt $maxY) { $maxY = $y }
                    }
                }
            }

            # Handle empty glyphs (like space)
            if ($minX -gt $maxX) {
                # Space character - use advance width
                $outWidth = [Math]::Max(1, [Math]::Ceiling($charWidth))
                $pixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()
                for ($row = 0; $row -lt $Height; $row++) {
                    $pixelRow = [System.Collections.Generic.List[double]]::new()
                    for ($col = 0; $col -lt $outWidth; $col++) {
                        $pixelRow.Add(0.0)
                    }
                    $pixels.Add($pixelRow)
                }
                return [PSCustomObject]@{
                    Character = $Character
                    Width     = $outWidth
                    Height    = $Height
                    Pixels    = $pixels
                }
            }

            # For pixel fonts, use the line height to get consistent vertical sizing
            # Extract pixels from padding to padding+lineHeight
            $outHeight = [Math]::Min($Height, [Math]::Ceiling($lineHeight))
            $outWidth = [Math]::Max(1, [Math]::Ceiling($charWidth))

            # Apply max width for monospace if specified
            if ($MaxWidth -gt 0) {
                $outWidth = $MaxWidth
            }

            $pixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()

            for ($y = 0; $y -lt $outHeight; $y++) {
                $row = [System.Collections.Generic.List[double]]::new()
                for ($x = 0; $x -lt $outWidth; $x++) {
                    $srcX = $padding + $x
                    $srcY = $padding + $y
                    if ($srcX -ge 0 -and $srcX -lt $imageWidth -and $srcY -ge 0 -and $srcY -lt $imageHeight) {
                        $pixel = $image[$srcX, $srcY]
                        $brightness = ($pixel.R + $pixel.G + $pixel.B) / (255.0 * 3)
                        $row.Add($brightness)
                    } else {
                        $row.Add(0.0)
                    }
                }
                $pixels.Add($row)
            }

            [PSCustomObject]@{
                Character = $Character
                Width     = $outWidth
                Height    = $outHeight
                Pixels    = $pixels
            }
        } else {
            # Standard mode: downsample from rendered image to target height
            # Calculate the sampling region (exclude padding, use full line height)
            $srcX = $padding
            $srcY = $padding
            $srcWidth = $charWidth
            $srcHeight = [Math]::Ceiling($lineHeight)

            # Calculate output dimensions
            $outHeight = $Height
            $outWidth = [Math]::Max(1, [Math]::Ceiling($charWidth * $Height / $lineHeight))

            # Apply max width for monospace if specified
            if ($MaxWidth -gt 0) {
                $outWidth = $MaxWidth
            }

            # Downsample by averaging pixel blocks
            $pixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()

            for ($outY = 0; $outY -lt $outHeight; $outY++) {
                $row = [System.Collections.Generic.List[double]]::new()

                # Map output row to source rows
                $srcYStart = $srcY + ($outY * $srcHeight / $outHeight)
                $srcYEnd = $srcY + (($outY + 1) * $srcHeight / $outHeight)

                for ($outX = 0; $outX -lt $outWidth; $outX++) {
                    # Map output column to source columns
                    $srcXStart = $srcX + ($outX * $srcWidth / $outWidth)
                    $srcXEnd = $srcX + (($outX + 1) * $srcWidth / $outWidth)

                    # Average brightness over the source region
                    $totalBrightness = 0.0
                    $sampleCount = 0

                    for ($sy = [Math]::Floor($srcYStart); $sy -lt [Math]::Ceiling($srcYEnd); $sy++) {
                        for ($sx = [Math]::Floor($srcXStart); $sx -lt [Math]::Ceiling($srcXEnd); $sx++) {
                            if ($sx -ge 0 -and $sx -lt $imageWidth -and $sy -ge 0 -and $sy -lt $imageHeight) {
                                $pixel = $image[[int]$sx, [int]$sy]
                                $brightness = ($pixel.R + $pixel.G + $pixel.B) / (255.0 * 3)
                                $totalBrightness += $brightness
                                $sampleCount++
                            }
                        }
                    }

                    $avgBrightness = if ($sampleCount -gt 0) { $totalBrightness / $sampleCount } else { 0.0 }
                    $row.Add($avgBrightness)
                }
                $pixels.Add($row)
            }

            [PSCustomObject]@{
                Character = $Character
                Width     = $outWidth
                Height    = $outHeight
                Pixels    = $pixels
            }
        }
    } finally {
        if ($null -ne $image) {
            $image.Dispose()
        }
    }
}
