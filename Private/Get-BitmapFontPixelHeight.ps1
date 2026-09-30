function Get-BitmapFontPixelHeight {
    <#
    .SYNOPSIS
        Detects the native design grid of a pixel-style font.
    .DESCRIPTION
        These fonts are outline fonts that mimic pixels; none carry an embedded bitmap
        strike, so pixel "cleanliness" alone cannot find the native grid. Two signals,
        in priority order:

        1. Name hint. Pixel fonts almost always encode their grid in the family name
           (Jacquard12 -> 12, Jewel 6 -> 6, Jersey20 -> 20). A number >= 5 wins outright.

        2. Stroke-width alignment. For thick-stroke fonts without a usable name number,
           binarize a probe glyph across candidate sizes and measure the fraction of
           stroke run-lengths that are integer multiples of the minimum run. Rendering on
           the native grid (or an integer multiple) keeps strokes uniform (~100% aligned);
           off-grid sizes break alignment. The fundamental is the smallest size that is
           aligned AND whose integer multiples are all aligned AND that shows real
           discrimination (some off-grid size renders misaligned).

        Thin-stroke fonts (Jacquard, blocco) render cleanly at every size and yield no
        alignment signal; they rely on the name hint or the 8px default.

        Height is the native grid itself. The rasterizer reproduces the grid faithfully
        only at the native size (and exact integer multiples); it anti-aliases and distorts
        thin strokes at any supersampled "legibility" multiple, so we always render 1:1.
    .PARAMETER FontFamily
        The SixLabors.Fonts.FontFamily to analyze.
    .PARAMETER Height
        When specified, skips detection and detects only the optimal width for this height.
    .PARAMETER UnitsPerPixel
        Font units per pixel. When specified, the height is UnitsPerEm / UnitsPerPixel and
        only the width is detected.
    .OUTPUTS
        [PSCustomObject] with Height and Width (both [int]).
    .NOTES
        Internal helper. Width is the advance width of the widest probe glyph, in pixels.
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [SixLabors.Fonts.FontFamily]$FontFamily,

        [Parameter()]
        [int]$Height,

        [Parameter()]
        [int]$UnitsPerPixel
    )

    # Advance width of the widest probe glyph at a given font size
    $getWidth = {
        param([SixLabors.Fonts.Font]$Font)
        $maxWidth = 1
        foreach ($probe in @('M', 'W', '@', 'm')) {
            try {
                $opts = [SixLabors.Fonts.TextOptions]::new($Font)
                $adv = [SixLabors.Fonts.TextMeasurer]::MeasureAdvance($probe, $opts)
                $w = [Math]::Max(1, [Math]::Ceiling($adv.Width))
                if ($w -gt $maxWidth) { $maxWidth = $w }
            } catch {
                Write-Verbose "[+]     Width probe for '$probe' failed: $_"
            }
        }
        return $maxWidth
    }

    # Render at the native grid. These are outline fonts mimicking pixels; the
    # rasterizer reproduces the grid faithfully ONLY at the native size (and exact
    # integer multiples), and it anti-aliases/distorts at non-grid sizes. A 16px
    # "legibility" supersample destroys thin-stroke fonts (verified via PNG parity),
    # so we always render 1:1 at the detected grid.
    $newResult = {
        param([int]$Grid, [int]$Width)
        [PSCustomObject]@{ Height = $Grid; Width = $Width }
    }

    # UnitsPerPixel: derive height directly from metrics
    if ($UnitsPerPixel -gt 0) {
        # Family metrics can report UnitsPerEm = 0; read it from a sized instance.
        $probe = $FontFamily.CreateFont(12)
        $unitsPerEm = $probe.FontMetrics.UnitsPerEm
        $grid = [Math]::Max(1, [int][Math]::Round($unitsPerEm / $UnitsPerPixel))
        $width = & $getWidth $FontFamily.CreateFont($grid)
        Write-Verbose "[+] UnitsPerPixel=$UnitsPerPixel (UnitsPerEm=$unitsPerEm) -> height $grid, width $width"
        return & $newResult $grid $width
    }

    # Explicit height: detect width only
    if ($Height -gt 0) {
        $width = & $getWidth $FontFamily.CreateFont($Height)
        Write-Verbose "[+] Using specified height $Height, detected width $width"
        return [PSCustomObject]@{ Height = $Height; Width = $width }
    }

    # --- Detection ---
    $fontName = $FontFamily.Name

    # 1. Name hint: a number >= 5 in the family name is the design grid.
    if ($fontName -match '(\d+)') {
        $hint = [int]$Matches[1]
        if ($hint -ge 5) {
            $width = & $getWidth $FontFamily.CreateFont($hint)
            $rec = & $newResult $hint $width
            Write-Verbose "[+] Detected grid $hint from font name (rendering at $($rec.Height), width $width)"
            return $rec
        }
    }

    # 2. Stroke-width alignment: find the fundamental period for thick-stroke fonts.
    $testChar = [char]72  # 'H' - strong horizontal + vertical strokes
    $testSizes = @(8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40)
    $clean = @{}

    foreach ($size in $testSizes) {
        try {
            $font = $FontFamily.CreateFont($size)
            $m = $font.FontMetrics
            $sc = $font.Size / $m.UnitsPerEm
            $lh = [Math]::Ceiling(($m.HorizontalMetrics.Ascender * $sc) + [Math]::Abs($m.HorizontalMetrics.Descender * $sc))
            $bitmap = Get-GlyphBitmap -Font $font -Character $testChar -Height $lh -PixelPerfect

            # Collect horizontal on-run lengths
            $runs = [System.Collections.Generic.List[int]]::new()
            foreach ($row in $bitmap.Pixels) {
                $x = 0
                $w = $row.Count
                while ($x -lt $w) {
                    if ($row[$x] -ge 0.5) {
                        $len = 0
                        while ($x -lt $w -and $row[$x] -ge 0.5) { $len++; $x++ }
                        if ($len -gt 0) { $runs.Add($len) }
                    } else { $x++ }
                }
            }
            if ($runs.Count -lt 3) { $clean[$size] = 0; continue }

            $min = ($runs | Measure-Object -Minimum).Minimum
            if ($min -lt 1) { $clean[$size] = 0; continue }
            $aligned = 0
            foreach ($r in $runs) {
                $k = [Math]::Round($r / [double]$min)
                if ($k -ge 1 -and [Math]::Abs($r - $k * $min) -le 0.5) { $aligned++ }
            }
            $clean[$size] = $aligned / $runs.Count
            Write-Verbose "[+]   size $size : aligned $([Math]::Round($clean[$size], 2))"
        } catch {
            $clean[$size] = 0
        }
    }

    # Discrimination: a thick-stroke font has at least one off-grid size that renders dirty.
    $hasDiscrimination = @($testSizes | Where-Object { $clean[$_] -lt 0.5 }).Count -ge 1

    if ($hasDiscrimination) {
        foreach ($size in $testSizes) {
            if ($clean[$size] -lt 0.85) { continue }
            $multiples = @($testSizes | Where-Object { $_ % $size -eq 0 })
            $cleanMultiples = @($multiples | Where-Object { $clean[$_] -ge 0.85 })
            if ($multiples.Count -ge 2 -and $cleanMultiples.Count -eq $multiples.Count) {
                $width = & $getWidth $FontFamily.CreateFont($size)
                $rec = & $newResult $size $width
                Write-Verbose "[+] Detected grid $size via stroke alignment (rendering at $($rec.Height), width $width)"
                return $rec
            }
        }
    }

    # 3. No signal (thin-stroke or scalable font): default 8
    $width = & $getWidth $FontFamily.CreateFont(8)
    Write-Verbose "[+] No grid signal; defaulting to 8 (rendering at $((& $newResult 8 $width).Height))"
    return & $newResult 8 $width
}
