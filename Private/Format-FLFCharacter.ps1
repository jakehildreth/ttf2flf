function Format-FLFCharacter {
    <#
    .SYNOPSIS
        Formats ASCII art rows into FLF character format with endmarks.
    .DESCRIPTION
        Takes an array of ASCII art rows and adds proper FLF formatting:
        - Single endmark (@) at end of each line except last
        - Double endmark (@@) at end of last line
        - Optional width padding for monospace mode
    .PARAMETER Rows
        Array of strings representing the ASCII art rows.
    .PARAMETER Endmark
        The endmark character (default: @).
    .PARAMETER TargetWidth
        Target width for monospace padding. If 0, uses natural width.
    .PARAMETER Hardblank
        The hardblank character for padding (default: $).
    .OUTPUTS
        [string[]] Array of formatted FLF character lines.
    .NOTES
        This is an internal helper function.
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]
        [string[]]$Rows,

        [Parameter()]
        [char]$Endmark = '@',

        [Parameter()]
        [int]$TargetWidth = 0,

        [Parameter()]
        [char]$Hardblank = '$'
    )

    $formattedRows = [System.Collections.Generic.List[string]]::new()
    $rowCount = $Rows.Count

    for ($i = 0; $i -lt $rowCount; $i++) {
        $row = $Rows[$i]

        # Apply target width padding if specified
        if ($TargetWidth -gt 0 -and $row.Length -lt $TargetWidth) {
            $padding = $TargetWidth - $row.Length
            $row = $row + (' ' * $padding)
        }

        # Add endmarks: single for all lines except last, double for last
        if ($i -eq ($rowCount - 1)) {
            $formattedRow = $row + $Endmark + $Endmark
        } else {
            $formattedRow = $row + $Endmark
        }

        $formattedRows.Add($formattedRow)
    }

    # Use Write-Output with -NoEnumerate to preserve array type for single elements
    Write-Output -NoEnumerate $formattedRows.ToArray()
}
