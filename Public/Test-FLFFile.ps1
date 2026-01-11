function Test-FLFFile {
    <#
    .SYNOPSIS
        Validates an FLF (FIGlet) font file for spec compliance.
    .DESCRIPTION
        Performs comprehensive validation of an FLF file:
        - Header format and required parameters
        - Character count (102 required characters)
        - Consistent character height
        - Proper endmark placement
        - Comment line count matching header

        Returns a detailed validation result with pass/fail status
        and specific issues found.
    .PARAMETER Path
        Path to the FLF file to validate.
    .PARAMETER Content
        FLF content as a string (alternative to file path).
    .PARAMETER Strict
        Enables strict validation including warnings as failures.
    .EXAMPLE
        Test-FLFFile -Path 'myfont.flf'

        Validates myfont.flf and returns validation results.
    .EXAMPLE
        Test-FLFFile -Path 'myfont.flf' -Strict

        Validates with strict mode - warnings become errors.
    .EXAMPLE
        Get-ChildItem *.flf | Test-FLFFile | Where-Object { -not $_.IsValid }

        Find all invalid FLF files in current directory.
    .OUTPUTS
        [PSCustomObject] with properties:
        - Path: The file path tested
        - IsValid: Boolean pass/fail
        - Errors: Array of error messages
        - Warnings: Array of warning messages
        - Header: Parsed header information (if valid)
    .NOTES
        FLF format specification: http://www.jave.de/figlet/figfont.html
    .LINK
        https://github.com/jakehildreth/ttf2flf
    .LINK
        ConvertTo-FLF
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    [OutputType([PSCustomObject])]
    param(
        [Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = 'Path')]
        [ValidateScript({ Test-Path -Path $_ -PathType Leaf })]
        [Alias('FullName')]
        [string]$Path,

        [Parameter(Mandatory, ParameterSetName = 'Content')]
        [string]$Content,

        [Parameter()]
        [switch]$Strict
    )

    process {
        $errors = [System.Collections.Generic.List[string]]::new()
        $warnings = [System.Collections.Generic.List[string]]::new()
        $headerInfo = $null
        $sourcePath = $Path

        # Load content
        if ($PSCmdlet.ParameterSetName -eq 'Path') {
            try {
                $Content = Get-Content -Path $Path -Raw -ErrorAction Stop
                $sourcePath = Resolve-Path -Path $Path | Select-Object -ExpandProperty Path
            } catch {
                $errors.Add("Failed to read file: $_")
                return [PSCustomObject]@{
                    Path     = $sourcePath
                    IsValid  = $false
                    Errors   = $errors.ToArray()
                    Warnings = $warnings.ToArray()
                    Header   = $null
                }
            }
        } else {
            $sourcePath = '<string content>'
        }

        # Split into lines
        $lines = $Content -split '\r?\n'

        if ($lines.Count -lt 1) {
            $errors.Add('File is empty')
            return [PSCustomObject]@{
                Path     = $sourcePath
                IsValid  = $false
                Errors   = $errors.ToArray()
                Warnings = $warnings.ToArray()
                Header   = $null
            }
        }

        # Parse header line
        $headerLine = $lines[0]

        # Check signature (must start with 'flf2a')
        if (-not $headerLine.StartsWith('flf2a')) {
            $errors.Add("Invalid signature. Expected 'flf2a', got: '$($headerLine.Substring(0, [Math]::Min(5, $headerLine.Length)))'")
        } else {
            # Parse header components
            # Format: flf2a<hardblank> Height Baseline MaxLen OldLayout CommentLines [PrintDir [FullLayout [CodetagCount]]]
            $hardblank = $headerLine[5]
            $headerParams = $headerLine.Substring(6).Trim() -split '\s+'

            if ($headerParams.Count -lt 5) {
                $errors.Add("Header missing required parameters. Expected at least 5, got $($headerParams.Count)")
            } else {
                try {
                    $headerInfo = [PSCustomObject]@{
                        Signature     = 'flf2a'
                        Hardblank     = $hardblank
                        Height        = [int]$headerParams[0]
                        Baseline      = [int]$headerParams[1]
                        MaxLength     = [int]$headerParams[2]
                        OldLayout     = [int]$headerParams[3]
                        CommentLines  = [int]$headerParams[4]
                        PrintDirection = if ($headerParams.Count -gt 5) { [int]$headerParams[5] } else { 0 }
                        FullLayout    = if ($headerParams.Count -gt 6) { [int]$headerParams[6] } else { $null }
                        CodetagCount  = if ($headerParams.Count -gt 7) { [int]$headerParams[7] } else { 0 }
                    }

                    # Validate header values
                    if ($headerInfo.Height -lt 1) {
                        $errors.Add("Invalid height: $($headerInfo.Height). Must be at least 1.")
                    }

                    if ($headerInfo.Baseline -lt 1 -or $headerInfo.Baseline -gt $headerInfo.Height) {
                        $warnings.Add("Baseline ($($headerInfo.Baseline)) should be between 1 and Height ($($headerInfo.Height))")
                    }

                    if ($headerInfo.MaxLength -lt 1) {
                        $errors.Add("Invalid MaxLength: $($headerInfo.MaxLength)")
                    }

                    if ($headerInfo.OldLayout -lt -1 -or $headerInfo.OldLayout -gt 63) {
                        $warnings.Add("OldLayout ($($headerInfo.OldLayout)) is outside typical range (-1 to 63)")
                    }

                    if ($headerInfo.CommentLines -lt 0) {
                        $errors.Add("Invalid CommentLines: $($headerInfo.CommentLines)")
                    }

                } catch {
                    $errors.Add("Failed to parse header parameters: $_")
                }
            }
        }

        # If header parsing failed, return early
        if ($null -eq $headerInfo) {
            return [PSCustomObject]@{
                Path     = $sourcePath
                IsValid  = ($errors.Count -eq 0)
                Errors   = $errors.ToArray()
                Warnings = $warnings.ToArray()
                Header   = $null
            }
        }

        # Verify comment lines
        $expectedDataStart = 1 + $headerInfo.CommentLines
        if ($lines.Count -lt $expectedDataStart) {
            $errors.Add("Not enough lines for declared comment count ($($headerInfo.CommentLines))")
        }

        # Parse FIGcharacters
        $dataLines = $lines[$expectedDataStart..($lines.Count - 1)]

        # Required character count: 102 (ASCII 32-126 = 95, German = 7)
        $requiredCharCount = 102
        $expectedTotalLines = $requiredCharCount * $headerInfo.Height

        # Count characters by finding double-endmark lines
        $endmark = '@'  # Most common endmark
        $characterCount = 0
        $currentCharHeight = 0
        $lineIndex = 0

        foreach ($line in $dataLines) {
            if ([string]::IsNullOrEmpty($line)) {
                continue
            }

            $lineIndex++
            $currentCharHeight++

            # Check for double endmark (end of character)
            if ($line.EndsWith($endmark + $endmark) -or $line.EndsWith('##')) {
                if ($currentCharHeight -ne $headerInfo.Height) {
                    $warnings.Add("Character $($characterCount + 1) has height $currentCharHeight, expected $($headerInfo.Height)")
                }
                $characterCount++
                $currentCharHeight = 0
            } elseif (-not ($line.EndsWith($endmark) -or $line.EndsWith('#'))) {
                $warnings.Add("Line $lineIndex missing endmark: '$($line.Substring([Math]::Max(0, $line.Length - 10)))'")
            }

            # Check line length
            if ($line.Length -gt $headerInfo.MaxLength) {
                $warnings.Add("Line $lineIndex exceeds MaxLength ($($line.Length) > $($headerInfo.MaxLength))")
            }
        }

        # Validate character count
        if ($characterCount -lt $requiredCharCount) {
            $errors.Add("Insufficient characters: found $characterCount, expected at least $requiredCharCount")
        } elseif ($characterCount -gt $requiredCharCount + $headerInfo.CodetagCount) {
            $warnings.Add("More characters ($characterCount) than expected ($requiredCharCount + $($headerInfo.CodetagCount) code-tagged)")
        }

        # Build result
        $isValid = $errors.Count -eq 0
        if ($Strict -and $warnings.Count -gt 0) {
            $isValid = $false
        }

        [PSCustomObject]@{
            Path     = $sourcePath
            IsValid  = $isValid
            Errors   = $errors.ToArray()
            Warnings = $warnings.ToArray()
            Header   = $headerInfo
        }
    }
}
