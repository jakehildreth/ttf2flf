function Get-BitmapFontPixelHeight {
    <#
    .SYNOPSIS
        Detects the native design size of a bitmap font.
    .DESCRIPTION
        Probes a bitmap font at various sizes to find where pixels render cleanly
        without antialiasing, indicating the native bitmap design size.
    .PARAMETER FontFamily
        The SixLabors.Fonts.FontFamily to analyze.
    .OUTPUTS
        [int] The detected font size.
    .NOTES
        This is an internal helper function for bitmap font detection.
        Returns the font SIZE, caller calculates pixel height from metrics.
    #>
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)]
        [SixLabors.Fonts.FontFamily]$FontFamily
    )

    # Test sizes to try (common bitmap font design sizes)
    $testSizes = @(5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 20, 24, 32)
    
    # Test with 'H' - simple character with clear vertical/horizontal strokes
    $testChar = [char]72  # 'H'
    
    Write-Verbose "[+] Auto-detecting bitmap font design size..."
    
    # Collect all candidates that meet the threshold
    $candidates = @()
    
    foreach ($size in $testSizes) {
        $font = $FontFamily.CreateFont($size)
        $metrics = $font.FontMetrics
        
        # Calculate line height from font metrics
        $scale = $font.Size / $metrics.UnitsPerEm
        $ascender = $metrics.HorizontalMetrics.Ascender * $scale
        $descender = [Math]::Abs($metrics.HorizontalMetrics.Descender * $scale)
        $lineHeight = [Math]::Ceiling($ascender + $descender)
        
        Write-Verbose "[+]   Testing size $size : lineHeight=$lineHeight"
        
        # Skip sizes where metrics suggest heavy scaling (not native)
        # For bitmap fonts, lineHeight should be reasonably close to the font size
        if ($lineHeight -lt ($size - 1) -or $lineHeight -gt ($size + 2)) {
            continue
        }
        
        try {
            $bitmap = Get-GlyphBitmap -Font $font -Character $testChar -Height $lineHeight -PixelPerfect
            
            # Check for clean pixel alignment - bitmap fonts should have
            # mostly full-on (1.0) or full-off (0.0) pixels, not anti-aliased grays
            $totalPixels = 0
            $cleanPixels = 0
            
            for ($y = 0; $y -lt $bitmap.Pixels.Count; $y++) {
                $row = $bitmap.Pixels[$y]
                for ($x = 0; $x -lt $row.Count; $x++) {
                    $pixel = $row[$x]
                    $totalPixels++
                    # Consider a pixel "clean" if it's very close to 0 or 1
                    if ($pixel -lt 0.1 -or $pixel -gt 0.9) {
                        $cleanPixels++
                    }
                }
            }
            
            $cleanRatio = if ($totalPixels -gt 0) { $cleanPixels / $totalPixels } else { 0 }
            Write-Verbose "[+]     Clean pixel ratio: $([math]::Round($cleanRatio, 2))"
            
            # Bitmap fonts at native size should have >85% clean pixels
            if ($cleanRatio -ge 0.85) {
                $candidates += [PSCustomObject]@{
                    Size = $size
                    LineHeight = $lineHeight
                    CleanRatio = $cleanRatio
                }
            }
        } catch {
            Write-Verbose "[+]     Render failed: $_"
            continue
        }
    }
    
    # Choose the best candidate
    if ($candidates.Count -gt 0) {
        # Check if font name contains a number (e.g., "Jacquard12", "Jersey20")
        # which often indicates the design pixel height
        $fontName = $FontFamily.Name
        if ($fontName -match '(\d+)') {
            $nameHint = [int]$matches[1]
            # If a candidate's size matches the name hint, prefer it
            $hintMatch = $candidates | Where-Object { $_.Size -eq $nameHint } | Select-Object -First 1
            if ($hintMatch) {
                Write-Verbose "[+] Detected design size: $($hintMatch.Size) (from font name, lineHeight: $($hintMatch.LineHeight), clean: $([math]::Round($hintMatch.CleanRatio, 2)))"
                return [int]$hintMatch.Size
            }
        }
        
        # Otherwise prefer smallest size with clean rendering
        $bestCandidate = $candidates | Sort-Object Size, @{Expression='CleanRatio'; Descending=$true} | Select-Object -First 1
        Write-Verbose "[+] Detected design size: $($bestCandidate.Size) (lineHeight: $($bestCandidate.LineHeight), clean: $([math]::Round($bestCandidate.CleanRatio, 2)))"
        return [int]$bestCandidate.Size
    }
    
    # If detection fails, default to 8
    Write-Verbose "[+] Could not detect design size, defaulting to 8"
    return 8
}
