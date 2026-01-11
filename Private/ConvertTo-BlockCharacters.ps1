function ConvertTo-BlockCharacters {
    <#
    .SYNOPSIS
        Converts pixel brightness data to Unicode block characters.
    .DESCRIPTION
        Takes a 2D array of brightness values (0.0 to 1.0) and maps them
        to Unicode block characters based on coverage thresholds.
    .PARAMETER Pixels
        2D list of brightness values from Get-GlyphBitmap.
    .PARAMETER Hardblank
        The hardblank character to use for forced spacing.
    .OUTPUTS
        [string[]] Array of strings representing the ASCII art rows.
    .NOTES
        This is an internal helper function.
        Block character mapping:
          ' '  = 0-12% brightness (empty)
          ░    = 13-37% brightness (light shade)
          ▒    = 38-62% brightness (medium shade)
          ▓    = 63-87% brightness (dark shade)
          █    = 88-100% brightness (full block)
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.List[System.Collections.Generic.List[double]]]$Pixels,

        [Parameter()]
        [char]$Hardblank = '$'
    )

    $rows = [System.Collections.Generic.List[string]]::new()

    foreach ($pixelRow in $Pixels) {
        $rowBuilder = [System.Text.StringBuilder]::new()

        foreach ($brightness in $pixelRow) {
            # Map brightness to block character
            # Use if/elseif to ensure single character return
            if ($brightness -lt 0.125) {
                $char = ' '           # Empty
            } elseif ($brightness -lt 0.375) {
                $char = [char]0x2591  # ░ Light shade
            } elseif ($brightness -lt 0.625) {
                $char = [char]0x2592  # ▒ Medium shade
            } elseif ($brightness -lt 0.875) {
                $char = [char]0x2593  # ▓ Dark shade
            } else {
                $char = [char]0x2588  # █ Full block
            }
            [void]$rowBuilder.Append($char)
        }

        $rows.Add($rowBuilder.ToString())
    }

    $rows.ToArray()
}
