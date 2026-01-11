function New-FLFHeader {
    <#
    .SYNOPSIS
        Generates the header line for an FLF file.
    .DESCRIPTION
        Creates the FLF header line containing all required metadata:
        signature, hardblank, height, baseline, max length, layout mode, etc.
    .PARAMETER Hardblank
        The hardblank character (default: $).
    .PARAMETER Height
        The height of all FIGcharacters in rows.
    .PARAMETER Baseline
        Number of lines from baseline to top of character.
    .PARAMETER MaxLength
        Maximum line length including endmarks.
    .PARAMETER Layout
        Layout mode: FullWidth, Kerned, or Smushed.
    .PARAMETER CommentLines
        Number of comment lines following the header.
    .PARAMETER PrintDirection
        0 for left-to-right, 1 for right-to-left.
    .PARAMETER CodetagCount
        Number of code-tagged characters beyond the required 102.
    .OUTPUTS
        [string] The FLF header line.
    .NOTES
        FLF header format:
        flf2a$ Height Baseline MaxLen OldLayout CommentLines PrintDir FullLayout CodetagCount
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter()]
        [char]$Hardblank = '$',

        [Parameter(Mandatory)]
        [int]$Height,

        [Parameter(Mandatory)]
        [int]$Baseline,

        [Parameter(Mandatory)]
        [int]$MaxLength,

        [Parameter()]
        [ValidateSet('FullWidth', 'Kerned', 'Smushed')]
        [string]$Layout = 'FullWidth',

        [Parameter()]
        [int]$CommentLines = 4,

        [Parameter()]
        [int]$PrintDirection = 0,

        [Parameter()]
        [int]$CodetagCount = 0
    )

    # Layout mode values - use module variable or fallback for standalone testing
    $layoutModes = if ($null -ne $script:LayoutModes) {
        $script:LayoutModes
    } else {
        @{
            FullWidth = @{ OldLayout = -1; FullLayout = 0 }
            Kerned    = @{ OldLayout = 0; FullLayout = 64 }
            Smushed   = @{ OldLayout = 63; FullLayout = 16191 }
        }
    }

    $layoutValues = $layoutModes[$Layout]
    $oldLayout = $layoutValues.OldLayout
    $fullLayout = $layoutValues.FullLayout

    # FLF signature - use module variable or fallback
    $signature = if ($null -ne $script:FLFSignature) {
        $script:FLFSignature
    } else {
        'flf2a'
    }

    # Format: flf2a$ Height Baseline MaxLen OldLayout CommentLines PrintDir FullLayout CodetagCount
    '{0}{1} {2} {3} {4} {5} {6} {7} {8} {9}' -f @(
        $signature
        $Hardblank
        $Height
        $Baseline
        $MaxLength
        $oldLayout
        $CommentLines
        $PrintDirection
        $fullLayout
        $CodetagCount
    )
}
