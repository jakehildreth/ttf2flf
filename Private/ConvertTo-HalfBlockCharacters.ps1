function ConvertTo-HalfBlockCharacters {
    <#
    .SYNOPSIS
        Converts pixel data to half-block Unicode characters for pixel-perfect rendering.
    .DESCRIPTION
        Takes a 2D array of brightness values and converts pairs of rows into
        half-block characters. Each output row represents 2 pixel rows using:
          █ (U+2588) = both pixels on
          ▀ (U+2580) = top pixel on, bottom off
          ▄ (U+2584) = top pixel off, bottom on
          ' ' (space) = both pixels off
    .PARAMETER Pixels
        2D list of brightness values from Get-GlyphBitmap.
    .PARAMETER Threshold
        Brightness threshold for considering a pixel "on" (default 0.5).
    .OUTPUTS
        [string[]] Array of strings representing the ASCII art rows.
    .NOTES
        This is an internal helper function for pixel-perfect bitmap font rendering.
        The output height is half the input pixel height (rounded up).
    .EXAMPLE
        $halfBlocks = ConvertTo-HalfBlockCharacters -Pixels $bitmap.Pixels
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.List[System.Collections.Generic.List[double]]]$Pixels,

        [Parameter()]
        [double]$Threshold = 0.5
    )

    $rows = [System.Collections.Generic.List[string]]::new()
    $pixelHeight = $Pixels.Count
    $pixelWidth = if ($pixelHeight -gt 0) { $Pixels[0].Count } else { 0 }

    # Process pairs of rows
    for ($y = 0; $y -lt $pixelHeight; $y += 2) {
        $rowBuilder = [System.Text.StringBuilder]::new()

        for ($x = 0; $x -lt $pixelWidth; $x++) {
            # Get top pixel (current row)
            $topOn = $Pixels[$y][$x] -ge $Threshold

            # Get bottom pixel (next row, or false if at end)
            $bottomOn = $false
            if (($y + 1) -lt $pixelHeight) {
                $bottomOn = $Pixels[$y + 1][$x] -ge $Threshold
            }

            # Map to half-block character
            if ($topOn -and $bottomOn) {
                $char = [char]0x2588  # █ Full block
            } elseif ($topOn) {
                $char = [char]0x2580  # ▀ Upper half block
            } elseif ($bottomOn) {
                $char = [char]0x2584  # ▄ Lower half block
            } else {
                $char = ' '          # Space (both off)
            }

            [void]$rowBuilder.Append($char)
        }

        $rows.Add($rowBuilder.ToString())
    }

    $rows.ToArray()
}
