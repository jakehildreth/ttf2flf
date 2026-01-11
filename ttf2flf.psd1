@{
    RootModule        = 'ttf2flf.psm1'
    ModuleVersion     = '2026.1.11'
    GUID              = 'a3b7c8d9-e0f1-4a5b-9c6d-7e8f9a0b1c2d'
    Author            = 'Jake Hildreth'
    CompanyName       = 'Jake Hildreth'
    Copyright         = '(c) 2026 Jake Hildreth. All rights reserved.'
    Description       = 'Convert TrueType fonts (.ttf) to FIGlet font files (.flf) for ASCII art text rendering.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'ConvertTo-FLF'
        'Test-FLFFile'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags         = @('FIGlet', 'ASCII', 'Font', 'TTF', 'FLF', 'ASCII-Art')
            LicenseUri   = 'https://github.com/jakehildreth/ttf2flf/blob/main/LICENSE'
            ProjectUri   = 'https://github.com/jakehildreth/ttf2flf'
            ReleaseNotes = 'Initial release - Convert TTF fonts to FIGlet format'
        }
    }
}
