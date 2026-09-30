#requires -Version 7.0
<#
.SYNOPSIS
    Interactive browser for the .flf fonts in ./fonts.
.DESCRIPTION
    Pick a font from a searchable Spectre.Console list; it renders a sample with
    Spectre's built-in FIGlet engine (Write-SpectreFigletText). Loops until you bail.
.EXAMPLE
    ./Show-FLFShowcase.ps1
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$FontDir = (Join-Path $PSScriptRoot 'fonts'),

    [Parameter()]
    [string]$Sample = 'Sphinx of black quartz, judge my vow 0123456789'
)

Import-Module PwshSpectreConsole -ErrorAction Stop

$fonts = Get-ChildItem -Path $FontDir -Filter '*.flf' | Sort-Object Name
if (-not $fonts) { throw "No .flf fonts found in $FontDir" }

while ($true) {
    Clear-Host
    $choice = Read-SpectreSelection `
        -Message "[green]FLF Showcase[/] — $($fonts.Count) fonts. Type to search, esc to quit." `
        -Choices $fonts.BaseName `
        -PageSize 15 `
        -EnableSearch

    if (-not $choice) { break }   # esc / cancel

    Clear-Host
    Write-SpectreRule "[yellow]$choice[/]"
    try {
        Write-SpectreFigletText -Text $Sample -FigletFontPath (Join-Path $FontDir "$choice.flf") -Color White
    } catch {
        Write-SpectreHost "[red]Failed to render: $($_.Exception.Message)[/]"
    }
    Write-SpectreHost "`n[grey]Press any key for the list, esc to quit...[/]"
    $key = [System.Console]::ReadKey($true)
    if ($key.Key -eq 'Escape') { break }
}

Clear-Host
