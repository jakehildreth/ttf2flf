function Test-FontCompatibility {
    <#
    .SYNOPSIS
        Tests if a font is suitable for FLF conversion.
    .DESCRIPTION
        Checks font properties to identify potential issues with conversion,
        such as color fonts, emoji fonts, or symbol fonts that may not
        produce meaningful ASCII art output.
    .PARAMETER FontFamily
        The SixLabors.Fonts.FontFamily object to test.
    .PARAMETER Path
        The path to the font file (used for warning messages).
    .OUTPUTS
        [PSCustomObject] with IsCompatible boolean and Warnings array.
    .NOTES
        This is an internal helper function.
    #>
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory)]
        [SixLabors.Fonts.FontFamily]$FontFamily,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $warnings = [System.Collections.Generic.List[string]]::new()
    $isCompatible = $true

    # Get font name for messages
    $fontName = $FontFamily.Name

    # Check for symbol font naming patterns
    $symbolPatterns = @('wingding', 'webding', 'symbol', 'dingbat', 'icon', 'emoji')
    foreach ($pattern in $symbolPatterns) {
        if ($fontName -like "*$pattern*") {
            $warnings.Add("[!] Font '$fontName' appears to be a symbol/icon font. Output may not represent text correctly.")
            break
        }
    }

    # Try to create a font instance to check for additional properties
    try {
        $font = $FontFamily.CreateFont(12)

        # Check if basic ASCII characters have glyphs
        $testChars = @([int][char]'A', [int][char]'a', [int][char]'0')
        $missingGlyphs = 0

        foreach ($charCode in $testChars) {
            $codePoint = [SixLabors.Fonts.Unicode.CodePoint]::new($charCode)
            if (-not $font.FontMetrics.TryGetGlyphMetrics($codePoint, [SixLabors.Fonts.TextAttributes]::None, [SixLabors.Fonts.TextDecorations]::None, [SixLabors.Fonts.LayoutMode]::HorizontalTopBottom, [SixLabors.Fonts.ColorFontSupport]::None, [ref]$null)) {
                $missingGlyphs++
            }
        }

        if ($missingGlyphs -eq $testChars.Count) {
            $warnings.Add("[!] Font '$fontName' appears to have no standard ASCII glyphs. This may be a symbol font.")
            $isCompatible = $false
        } elseif ($missingGlyphs -gt 0) {
            $warnings.Add("[i] Font '$fontName' is missing some basic ASCII glyphs.")
        }
    } catch {
        $warnings.Add("[!] Could not fully analyze font '$fontName': $_")
    }

    [PSCustomObject]@{
        IsCompatible = $isCompatible
        Warnings     = $warnings.ToArray()
        FontName     = $fontName
    }
}
