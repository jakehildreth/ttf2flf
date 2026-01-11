#Requires -Version 7.0

# ttf2flf - Convert TrueType fonts to FIGlet font files
# https://github.com/jakehildreth/ttf2flf

#region Assembly Loading

$LibPath = Join-Path -Path $PSScriptRoot -ChildPath 'Lib'

# SixLabors assemblies required for TTF parsing and rendering
# Load order matters: ImageSharp must load before Fonts and Drawing
$RequiredAssemblies = @(
    'SixLabors.ImageSharp.dll'
    'SixLabors.Fonts.dll'
    'SixLabors.ImageSharp.Drawing.dll'
)

foreach ($assembly in $RequiredAssemblies) {
    $assemblyPath = Join-Path -Path $LibPath -ChildPath $assembly
    if (Test-Path -Path $assemblyPath) {
        try {
            Add-Type -Path $assemblyPath -ErrorAction Stop
            Write-Verbose "[+] Loaded assembly: $assembly"
        } catch {
            Write-Warning "[!] Failed to load assembly: $assembly - $_"
        }
    } else {
        Write-Warning "[!] Assembly not found: $assemblyPath"
        Write-Warning "[i] Run Install-TTF2FLFDependencies or download SixLabors packages to Lib/ folder"
    }
}

#endregion Assembly Loading

#region Function Loading

$Public = @(Get-ChildItem -Path "$PSScriptRoot\Public\*.ps1" -ErrorAction SilentlyContinue -Recurse)
$Private = @(Get-ChildItem -Path "$PSScriptRoot\Private\*.ps1" -ErrorAction SilentlyContinue -Recurse)

foreach ($import in @($Public + $Private)) {
    try {
        Write-Verbose "[+] Importing $($import.FullName)"
        . $import.FullName
    } catch {
        Write-Error "[!] Failed to import function $($import.FullName): $_"
    }
}

#endregion Function Loading

#region Module Variables

# FLF format constants
$script:FLFSignature = 'flf2a'
$script:DefaultHardblank = '$'
$script:DefaultEndmark = '@'

# Required FIGlet characters (ASCII 32-126 + 7 German characters)
$script:RequiredCharacters = @(32..126) + @(196, 214, 220, 228, 246, 252, 223)

# Layout mode values
$script:LayoutModes = @{
    FullWidth = @{ OldLayout = -1; FullLayout = 0 }
    Kerned    = @{ OldLayout = 0; FullLayout = 64 }
    Smushed   = @{ OldLayout = 63; FullLayout = 16191 }
}

# Unicode block characters for pixel mapping (by brightness/coverage)
$script:BlockCharacters = @{
    Empty  = ' '           # 0% coverage
    Light  = [char]0x2591  # ░ 25% coverage
    Medium = [char]0x2592  # ▒ 50% coverage
    Dark   = [char]0x2593  # ▓ 75% coverage
    Full   = [char]0x2588  # █ 100% coverage
}

#endregion Module Variables

# Export public functions
Export-ModuleMember -Function $Public.BaseName
