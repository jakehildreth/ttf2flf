function New-FLFComment {
    <#
    .SYNOPSIS
        Generates comment lines for an FLF file.
    .DESCRIPTION
        Creates the comment section that appears after the header line,
        including font name, source file, generation date, and module version.
    .PARAMETER FontName
        The name of the font being converted.
    .PARAMETER SourcePath
        The path to the original TTF file.
    .PARAMETER ModuleVersion
        The version of the ttf2flf module.
    .OUTPUTS
        [string[]] Array of comment lines.
    .NOTES
        This is an internal helper function.
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]
        [string]$FontName,

        [Parameter(Mandatory)]
        [string]$SourcePath,

        [Parameter()]
        [string]$ModuleVersion = '2026.1.11'
    )

    $sourceFileName = Split-Path -Path $SourcePath -Leaf
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    @(
        "$FontName FIGlet font"
        "Converted from: $sourceFileName"
        "Generated: $timestamp"
        "Generator: ttf2flf v$ModuleVersion (https://github.com/jakehildreth/ttf2flf)"
    )
}
