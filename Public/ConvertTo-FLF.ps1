function ConvertTo-FLF {
    [alias('ttf2flf')]
    <#
    .SYNOPSIS
        Converts a TrueType font to FIGlet font format.
    .DESCRIPTION
        Takes a TTF font file and generates an FLF (FIGlet) font file
        suitable for use with figlet, toilet, or similar ASCII art tools.

        The conversion process:
        1. Loads the TTF font using SixLabors.Fonts
        2. Renders each required character (ASCII 32-126 + German) to a bitmap
        3. Converts pixel data to Unicode block characters
        4. Formats output with proper FLF header and endmarks
    .PARAMETER Path
        Path to the TrueType font file (.ttf or .otf).
    .PARAMETER Height
        The height of FIGcharacters in rows. Default is 8.
        Higher values produce more detailed output but larger files.
    .PARAMETER OutputPath
        Path for the output .flf file. If not specified, uses the same
        name as the input file with .flf extension.
    .PARAMETER Hardblank
        The hardblank character used in the FLF file. Default is $.
        This character represents forced spacing in smushing modes.
    .PARAMETER Layout
        Layout mode written to the FLF header:
        - FullWidth: Characters at full designed width (no compression)
        - Kerned: Characters touch but don't overlap
        - Smushed: Characters overlap using smushing rules
    .PARAMETER Monospace
        Forces all characters to the same width by padding narrower
        characters with spaces.
    .PARAMETER PixelPerfect
        Enables pixel-perfect rendering for bitmap/pixel fonts.
        Uses half-block characters (▀▄█) where each terminal row
        represents 2 pixel rows. The font is rendered at the size
        specified by -PixelSize (default 8) and output height becomes
        PixelSize/2 rows. Produces crisp, accurate output for pixel fonts.
    .PARAMETER PixelSize
        The pixel height to render at when using -PixelPerfect mode.
        Common values: 8, 16, 24, 32. Default is 8.
        The FLF output height will be PixelSize/2 rows.
    .PARAMETER UnitsPerPixel
        Font units per pixel for bitmap fonts. If specified, overrides
        automatic detection by setting base em size = UnitsPerEm / UnitsPerPixel.
        Common values: 50, 25, 100. Example: DepartureMono uses 50.
    .PARAMETER NoTrim
        Disables trimming of empty rows from top and bottom in PixelPerfect mode.
        Use this to preserve the font's original vertical spacing, especially for
        bitmap fonts with precise metrics where spacing is part of the design.
    .PARAMETER PassThru
        Returns the FLF content as a string instead of writing to a file.
    .EXAMPLE
        ConvertTo-FLF -Path 'C:\Fonts\Impact.ttf'

        Converts Impact.ttf to Impact.flf in the same directory.
    .EXAMPLE
        ConvertTo-FLF -Path 'Arial.ttf' -Height 12 -Layout Smushed

        Converts Arial.ttf with 12-row height and smushed layout mode.
    .EXAMPLE
        Get-ChildItem *.ttf | ConvertTo-FLF -OutputPath 'fonts\'

        Converts all TTF files to FLF format in the fonts directory.
    .EXAMPLE
        ConvertTo-FLF -Path 'font.ttf' -PassThru | Set-Clipboard

        Converts font and copies FLF content to clipboard.
    .OUTPUTS
        [string] When -PassThru is specified, returns the FLF content.
        [System.IO.FileInfo] When writing to file, returns the output file info.
    .NOTES
        Requires PowerShell 7.0 or later due to SixLabors.Fonts dependency.

        FLF format specification: http://www.jave.de/figlet/figfont.html
    .LINK
        https://github.com/jakehildreth/ttf2flf
    .LINK
        Test-FLFFile
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    [OutputType([string], [System.IO.FileInfo])]
    param(
        [Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)]
        [ValidateScript({
            if (-not (Test-Path -Path $_ -PathType Leaf)) {
                throw "File not found: $_"
            }
            if ($_ -notmatch '\.(ttf|otf)$') {
                throw "File must be a TrueType font (.ttf or .otf): $_"
            }
            $true
        })]
        [Alias('FullName')]
        [string]$Path,

        [Parameter()]
        [ValidateRange(4, 32)]
        [int]$Height = 8,

        [Parameter()]
        [string]$OutputPath,

        [Parameter()]
        [ValidateScript({
            if ($_ -eq ' ' -or $_ -eq "`r" -or $_ -eq "`n" -or $_ -eq "`0") {
                throw "Hardblank cannot be space, carriage-return, newline, or null"
            }
            $true
        })]
        [char]$Hardblank = '$',

        [Parameter()]
        [ValidateSet('FullWidth', 'Kerned', 'Smushed')]
        [string]$Layout = 'FullWidth',

        [Parameter()]
        [switch]$Monospace,

        [Parameter()]
        [switch]$PixelPerfect,

        [Parameter()]
        [ValidateRange(4, 64)]
        [int]$PixelSize,

        [Parameter()]
        [ValidateRange(1, 1000)]
        [int]$UnitsPerPixel,

        [Parameter()]
        [switch]$NoTrim,

        [Parameter()]
        [switch]$PassThru
    )

    begin {
        Write-Verbose '[+] Starting TTF to FLF conversion'

        # Verify assemblies are loaded by trying to access the types directly
        try {
            $null = [SixLabors.Fonts.FontCollection]
            $null = [SixLabors.ImageSharp.Image]
        } catch {
            $errorRecord = [System.Management.Automation.ErrorRecord]::new(
                [System.InvalidOperationException]::new('Required SixLabors assemblies not found. Ensure SixLabors.Fonts.dll, SixLabors.ImageSharp.dll, and SixLabors.ImageSharp.Drawing.dll are in the Lib/ folder.'),
                'MissingAssembly',
                [System.Management.Automation.ErrorCategory]::NotInstalled,
                'SixLabors'
            )
            $PSCmdlet.ThrowTerminatingError($errorRecord)
        }
    }

    process {
        $resolvedPath = Resolve-Path -Path $Path | Select-Object -ExpandProperty Path
        Write-Verbose "[+] Processing: $resolvedPath"

        # Determine output path
        if (-not $OutputPath) {
            $outputFile = [System.IO.Path]::ChangeExtension($resolvedPath, '.flf')
        } elseif (Test-Path -Path $OutputPath -PathType Container) {
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedPath)
            $outputFile = Join-Path -Path $OutputPath -ChildPath "$baseName.flf"
        } else {
            $outputFile = $OutputPath
        }

        if ($PSCmdlet.ShouldProcess($resolvedPath, 'Convert to FIGlet font')) {
            try {
                # Load the font
                Write-Verbose '[+] Loading font...'
                $fontCollection = [SixLabors.Fonts.FontCollection]::new()
                $fontFamily = $fontCollection.Add($resolvedPath)

                # Check font compatibility
                $compatibility = Test-FontCompatibility -FontFamily $fontFamily -Path $resolvedPath
                foreach ($warning in $compatibility.Warnings) {
                    Write-Warning $warning
                }

                if (-not $compatibility.IsCompatible) {
                    Write-Warning "[!] Font may not produce usable output. Continuing anyway..."
                }

                # Determine rendering mode and calculate dimensions
                if ($PixelPerfect) {
                    # Auto-detect pixel height and width if not specified
                    if (-not $PSBoundParameters.ContainsKey('PixelSize')) {
                        $detectParams = @{ FontFamily = $fontFamily }
                        if ($PSBoundParameters.ContainsKey('UnitsPerPixel')) {
                            $detectParams['UnitsPerPixel'] = $UnitsPerPixel
                        }
                        $detectedSize = Get-BitmapFontPixelHeight @detectParams
                        $PixelSize = $detectedSize.Height
                        $detectedWidth = $detectedSize.Width
                        Write-Verbose "[+] Auto-detected pixel size: $($PixelSize)h × ${detectedWidth}w"
                    } else {
                        # Height specified, detect optimal width for that height
                        $detectParams = @{ 
                            FontFamily = $fontFamily
                            Height = $PixelSize
                        }
                        if ($PSBoundParameters.ContainsKey('UnitsPerPixel')) {
                            $detectParams['UnitsPerPixel'] = $UnitsPerPixel
                        }
                        $detectedSize = Get-BitmapFontPixelHeight @detectParams
                        $detectedWidth = $detectedSize.Width
                        Write-Verbose "[+] Detected optimal width for ${PixelSize}h: ${detectedWidth}w"
                    }
                    
                    # Pixel-perfect mode: render at exact pixel size. Use the FULL line
                    # height (ascender+descender), not the point size, so descenders
                    # (g, j, p, q, y) and deep punctuation fit the cell; the trim pass
                    # reclaims empty top rows for fonts whose line height has headroom.
                    $fontSize = $PixelSize
                    # Metrics come from a sized font instance (family metrics can be 0).
                    $probeFont = $fontFamily.CreateFont($fontSize)
                    $pm = $probeFont.FontMetrics
                    $mScale = $probeFont.Size / $pm.UnitsPerEm
                    $lineHeightPx = [Math]::Ceiling(($pm.HorizontalMetrics.Ascender * $mScale) + [Math]::Abs($pm.HorizontalMetrics.Descender * $mScale))
                    $renderHeight = [Math]::Max($PixelSize, $lineHeightPx)
                    $outputHeight = [Math]::Ceiling($renderHeight / 2)  # 2 pixel rows per terminal row
                    Write-Verbose "[+] Pixel-perfect mode: ${PixelSize}px font (line height ${renderHeight}px) -> ${outputHeight} row FLF"
                } else {
                    # Standard mode: render at larger size for quality, then downsample
                    $fontSize = [Math]::Max(48, $Height * 8)
                    $outputHeight = $Height
                    $detectedWidth = 0
                }

                $font = $fontFamily.CreateFont($fontSize)
                Write-Verbose "[+] Font loaded: $($compatibility.FontName) rendering at size ${fontSize}"

                # First pass: render all characters to find max width (for monospace mode)
                $characterData = [System.Collections.Generic.Dictionary[int, PSCustomObject]]::new()
                $maxWidth = 0

                # renderHeight was set above (line height for pixel-perfect, Height otherwise)
                if (-not $PixelPerfect) { $renderHeight = $Height }
                Write-Verbose "[+] Rendering $($script:RequiredCharacters.Count) characters..."

                foreach ($charCode in $script:RequiredCharacters) {
                    $char = [char]$charCode

                    try {
                        if ($PixelPerfect) {
                            # Pixel-perfect: render at native size with detected width, get raw pixels
                            if ($detectedWidth -gt 0) {
                                $bitmap = Get-GlyphBitmap -Font $font -FontCollection $fontCollection -Character $char -Height $renderHeight -Width $detectedWidth -PixelPerfect
                            } else {
                                $bitmap = Get-GlyphBitmap -Font $font -FontCollection $fontCollection -Character $char -Height $renderHeight -PixelPerfect
                            }
                        } else {
                            $bitmap = Get-GlyphBitmap -Font $font -Character $char -Height $Height
                        }
                        $characterData[$charCode] = $bitmap

                        if ($bitmap.Width -gt $maxWidth) {
                            $maxWidth = $bitmap.Width
                        }
                    } catch {
                        Write-Warning "[!] Failed to render character $charCode ($char): $_"
                        # Create empty placeholder
                        $placeholderHeight = if ($PixelPerfect) { $PixelSize } else { $Height }
                        $emptyPixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()
                        for ($row = 0; $row -lt $placeholderHeight; $row++) {
                            $emptyRow = [System.Collections.Generic.List[double]]::new()
                            $emptyRow.Add(0.0)
                            $emptyPixels.Add($emptyRow)
                        }
                        $characterData[$charCode] = [PSCustomObject]@{
                            Character = $char
                            Width     = 1
                            Height    = $placeholderHeight
                            Pixels    = $emptyPixels
                        }
                    }
                }

                # Determine target width for formatting
                $targetWidth = if ($Monospace) { $maxWidth } else { 0 }

                # Convert all characters to FLF format
                Write-Verbose '[+] Converting to FLF format...'
                $flfCharacters = [System.Collections.Generic.Dictionary[int, string[]]]::new()
                $actualMaxLength = 0

                foreach ($charCode in $script:RequiredCharacters) {
                    $bitmap = $characterData[$charCode]

                    # Choose conversion method based on mode
                    if ($PixelPerfect) {
                        $asciiRows = ConvertTo-HalfBlockCharacters -Pixels $bitmap.Pixels
                    } else {
                        $asciiRows = ConvertTo-BlockCharacters -Pixels $bitmap.Pixels -Hardblank $Hardblank
                    }

                    $flfRows = Format-FLFCharacter -Rows $asciiRows -TargetWidth $targetWidth -Hardblank $Hardblank

                    $flfCharacters[$charCode] = $flfRows

                    # Track max length (including endmarks)
                    foreach ($row in $flfRows) {
                        if ($row.Length -gt $actualMaxLength) {
                            $actualMaxLength = $row.Length
                        }
                    }
                }

                # In pixel-perfect mode, trim empty rows from top and bottom (unless -NoTrim specified)
                if ($PixelPerfect -and -not $NoTrim -and $flfCharacters.Count -gt 0) {
                    # Use the keys from the dictionary, not the script variable
                    $firstCharCode = ($flfCharacters.Keys | Select-Object -First 1)
                    $rowCount = $flfCharacters[$firstCharCode].Count
                    Write-Verbose "[+] Checking $rowCount rows for trimming across $($flfCharacters.Count) characters"

                    # Find first non-empty row (check all characters)
                    $firstContentRow = $rowCount  # Start at end, will be updated when we find content
                    :rowloop for ($r = 0; $r -lt $rowCount; $r++) {
                        $hasContent = $false
                        foreach ($charCode in $flfCharacters.Keys) {
                            $row = $flfCharacters[$charCode][$r]
                            # Check if row has any non-space content (excluding endmarks)
                            $content = $row -replace '@+$', ''
                            if ($content -match '[^\s]') {
                                $hasContent = $true
                                Write-Verbose "[+] Row $r has content from char $charCode : [$content]"
                                break
                            }
                        }
                        if ($hasContent) {
                            $firstContentRow = $r
                            break rowloop
                        }
                    }
                    Write-Verbose "[+] First content row: $firstContentRow"

                    # Find last non-empty row
                    $lastContentRow = $rowCount - 1
                    for ($r = $rowCount - 1; $r -ge 0; $r--) {
                        $hasContent = $false
                        foreach ($charCode in $flfCharacters.Keys) {
                            $row = $flfCharacters[$charCode][$r]
                            $content = $row -replace '@+$', ''
                            if ($content -match '[^\s]') {
                                $hasContent = $true
                                break
                            }
                        }
                        if ($hasContent) {
                            $lastContentRow = $r
                            break
                        }
                    }
                    Write-Verbose "[+] Last content row: $lastContentRow"

                    # Trim if needed
                    if ($firstContentRow -gt 0 -or $lastContentRow -lt ($rowCount - 1)) {
                        $trimmedRowCount = $lastContentRow - $firstContentRow + 1
                        Write-Verbose "[+] Trimming empty rows: keeping rows $firstContentRow-$lastContentRow ($trimmedRowCount rows)"

                        foreach ($charCode in $flfCharacters.Keys) {
                            $originalRows = $flfCharacters[$charCode]
                            # @() forces array: single-row slices unroll to a scalar otherwise
                            $trimmedRows = @($originalRows[$firstContentRow..$lastContentRow])

                            # Re-apply endmarks (last row needs @@, others need @)
                            for ($i = 0; $i -lt $trimmedRows.Count; $i++) {
                                $trimmedRows[$i] = $trimmedRows[$i] -replace '@+$', ''
                                if ($i -eq $trimmedRows.Count - 1) {
                                    $trimmedRows[$i] += '@@'
                                } else {
                                    $trimmedRows[$i] += '@'
                                }
                            }

                            $flfCharacters[$charCode] = $trimmedRows
                        }

                        # Update output height
                        $outputHeight = $trimmedRowCount
                    }
                }

                # Generate comments
                $moduleVersion = (Get-Module -Name ttf2flf -ErrorAction SilentlyContinue)?.Version.ToString() ?? '2026.1.11'
                $comments = New-FLFComment -FontName $compatibility.FontName -SourcePath $resolvedPath -ModuleVersion $moduleVersion

                # Generate header
                # Use outputHeight for pixel-perfect mode, Height otherwise
                $flfHeight = if ($PixelPerfect) { $outputHeight } else { $Height }
                # Baseline is typically Height - 1 (bottom line is baseline)
                $baseline = $flfHeight - 1
                $header = New-FLFHeader `
                    -Hardblank $Hardblank `
                    -Height $flfHeight `
                    -Baseline $baseline `
                    -MaxLength ($actualMaxLength + 2) `
                    -Layout $Layout `
                    -CommentLines $comments.Count

                # Build the FLF file content
                $flfContent = [System.Text.StringBuilder]::new()

                # Header
                [void]$flfContent.AppendLine($header)

                # Comments
                foreach ($comment in $comments) {
                    [void]$flfContent.AppendLine($comment)
                }

                # Required characters (ASCII 32-126 first, then German)
                # ASCII 32-126
                for ($charCode = 32; $charCode -le 126; $charCode++) {
                    foreach ($row in $flfCharacters[$charCode]) {
                        [void]$flfContent.AppendLine($row)
                    }
                }

                # German characters (196, 214, 220, 228, 246, 252, 223)
                foreach ($charCode in @(196, 214, 220, 228, 246, 252, 223)) {
                    foreach ($row in $flfCharacters[$charCode]) {
                        [void]$flfContent.AppendLine($row)
                    }
                }

                $finalContent = $flfContent.ToString()

                # Output
                if ($PassThru) {
                    Write-Verbose '[+] Returning FLF content'
                    $finalContent
                } else {
                    Write-Verbose "[+] Writing to: $outputFile"
                    $finalContent | Set-Content -Path $outputFile -NoNewline -Encoding utf8
                    Get-Item -Path $outputFile
                }

                Write-Verbose '[+] Conversion complete'

            } catch {
                $errorRecord = [System.Management.Automation.ErrorRecord]::new(
                    $_.Exception,
                    'ConversionFailed',
                    [System.Management.Automation.ErrorCategory]::InvalidOperation,
                    $resolvedPath
                )
                $PSCmdlet.ThrowTerminatingError($errorRecord)
            }
        }
    }

    end {
        Write-Verbose '[+] ConvertTo-FLF complete'
    }
}
