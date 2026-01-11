BeforeAll {
    # Import the module
    $modulePath = Split-Path -Path $PSScriptRoot -Parent
    Import-Module -Name $modulePath -Force -ErrorAction Stop
}

Describe 'Test-FLFFile' {
    Context 'When validating a well-formed FLF file' {
        BeforeAll {
            # Create minimal valid FLF content
            $height = 2
            $hardblank = '$'
            $endmark = '@'

            # Build header: flf2a$ Height Baseline MaxLen OldLayout CommentLines PrintDir FullLayout CodetagCount
            $header = "flf2a$hardblank $height 1 10 -1 1 0 0 0"
            $comment = 'Test font'

            # Build 102 characters (ASCII 32-126 = 95, German = 7)
            $charLines = [System.Text.StringBuilder]::new()

            # Each character is 2 lines high, with single endmark on first line, double on last
            for ($i = 0; $i -lt 102; $i++) {
                [void]$charLines.AppendLine("X$endmark")
                [void]$charLines.AppendLine("X$endmark$endmark")
            }

            $script:validFLF = @"
$header
$comment
$($charLines.ToString().TrimEnd())
"@
        }

        It 'Should return IsValid as true' {
            $result = Test-FLFFile -Content $script:validFLF
            $result.IsValid | Should -BeTrue
        }

        It 'Should have no errors' {
            $result = Test-FLFFile -Content $script:validFLF
            $result.Errors | Should -HaveCount 0
        }

        It 'Should parse header correctly' {
            $result = Test-FLFFile -Content $script:validFLF
            $result.Header.Signature | Should -Be 'flf2a'
            $result.Header.Hardblank | Should -Be '$'
            $result.Header.Height | Should -Be 2
            $result.Header.Baseline | Should -Be 1
            $result.Header.CommentLines | Should -Be 1
        }
    }

    Context 'When validating an invalid FLF file' {
        It 'Should fail with invalid signature' {
            $invalidFLF = "invalid header"
            $result = Test-FLFFile -Content $invalidFLF
            $result.IsValid | Should -BeFalse
            $result.Errors | Should -Contain "Invalid signature. Expected 'flf2a', got: 'inval'"
        }

        It 'Should fail with missing header parameters' {
            $invalidFLF = "flf2a$ 8 7"
            $result = Test-FLFFile -Content $invalidFLF
            $result.IsValid | Should -BeFalse
            $result.Errors | Should -Not -BeNullOrEmpty
        }

        It 'Should fail with insufficient characters' {
            $invalidFLF = @"
flf2a$ 2 1 10 -1 0 0 0 0
X@
X@@
"@
            $result = Test-FLFFile -Content $invalidFLF
            $result.IsValid | Should -BeFalse
            $result.Errors | Should -Contain 'Insufficient characters: found 1, expected at least 102'
        }

        It 'Should fail on empty content' {
            # Empty string fails parameter validation, so use whitespace-only
            $result = Test-FLFFile -Content ' '
            $result.IsValid | Should -BeFalse
        }
    }

    Context 'When using -Strict mode' {
        BeforeAll {
            # Create FLF with warnings (baseline out of range) but still valid
            $height = 2
            $charLines = [System.Text.StringBuilder]::new()
            for ($i = 0; $i -lt 102; $i++) {
                [void]$charLines.AppendLine('X@')
                [void]$charLines.AppendLine('X@@')
            }
            # Use baseline=3 which is > height=2, generating a warning
            $script:warningFLF = @"
flf2a$ 2 3 10 -1 1 0 0 0
Test
$($charLines.ToString().TrimEnd())
"@
        }

        It 'Should pass without -Strict when only warnings exist' {
            $result = Test-FLFFile -Content $script:warningFLF
            $result.IsValid | Should -BeTrue
            $result.Warnings | Should -Not -BeNullOrEmpty
        }

        It 'Should fail with -Strict when warnings exist' {
            $result = Test-FLFFile -Content $script:warningFLF -Strict
            $result.IsValid | Should -BeFalse
        }
    }

    Context 'When reading from file path' {
        BeforeAll {
            $script:tempFile = Join-Path -Path $TestDrive -ChildPath 'test.flf'

            # Create minimal valid FLF
            $height = 2
            $charLines = [System.Text.StringBuilder]::new()
            for ($i = 0; $i -lt 102; $i++) {
                [void]$charLines.AppendLine('X@')
                [void]$charLines.AppendLine('X@@')
            }

            $content = @"
flf2a$ 2 1 10 -1 1 0 0 0
Test font
$($charLines.ToString().TrimEnd())
"@
            Set-Content -Path $script:tempFile -Value $content -NoNewline
        }

        It 'Should validate file from path' {
            $result = Test-FLFFile -Path $script:tempFile
            $result.IsValid | Should -BeTrue
            $result.Path | Should -Be $script:tempFile
        }

        It 'Should accept pipeline input' {
            $result = Get-Item -Path $script:tempFile | Test-FLFFile
            $result.IsValid | Should -BeTrue
        }
    }
}

Describe 'New-FLFHeader' {
    BeforeAll {
        # Dot-source the private function for testing
        $privatePath = Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'Private'
        . (Join-Path -Path $privatePath -ChildPath 'New-FLFHeader.ps1')
    }

    Context 'When generating header with default values' {
        It 'Should start with flf2a signature' {
            $header = New-FLFHeader -Height 8 -Baseline 7 -MaxLength 20
            $header | Should -Match '^flf2a'
        }

        It 'Should use $ as default hardblank' {
            $header = New-FLFHeader -Height 8 -Baseline 7 -MaxLength 20
            $header[5] | Should -Be '$'
        }

        It 'Should include correct height' {
            $header = New-FLFHeader -Height 10 -Baseline 9 -MaxLength 25
            $params = $header.Substring(7) -split '\s+'
            $params[0] | Should -Be '10'
        }
    }

    Context 'When specifying layout modes' {
        It 'Should set FullWidth layout values' {
            $header = New-FLFHeader -Height 8 -Baseline 7 -MaxLength 20 -Layout FullWidth
            $params = $header.Substring(7) -split '\s+'
            $params[3] | Should -Be '-1'  # OldLayout
        }

        It 'Should set Kerned layout values' {
            $header = New-FLFHeader -Height 8 -Baseline 7 -MaxLength 20 -Layout Kerned
            $params = $header.Substring(7) -split '\s+'
            $params[3] | Should -Be '0'   # OldLayout
        }

        It 'Should set Smushed layout values' {
            $header = New-FLFHeader -Height 8 -Baseline 7 -MaxLength 20 -Layout Smushed
            $params = $header.Substring(7) -split '\s+'
            $params[3] | Should -Be '63'  # OldLayout
        }
    }

    Context 'When using custom hardblank' {
        It 'Should use specified hardblank character' {
            $header = New-FLFHeader -Height 8 -Baseline 7 -MaxLength 20 -Hardblank '#'
            $header[5] | Should -Be '#'
        }
    }
}

Describe 'Format-FLFCharacter' {
    BeforeAll {
        $privatePath = Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'Private'
        . (Join-Path -Path $privatePath -ChildPath 'Format-FLFCharacter.ps1')
    }

    Context 'When formatting character rows' {
        It 'Should add single endmark to all lines except last' {
            $rows = @('ABC', 'DEF', 'GHI')
            $result = Format-FLFCharacter -Rows $rows
            $result[0] | Should -Be 'ABC@'
            $result[1] | Should -Be 'DEF@'
        }

        It 'Should add double endmark to last line' {
            $rows = @('ABC', 'DEF', 'GHI')
            $result = Format-FLFCharacter -Rows $rows
            $result[2] | Should -Be 'GHI@@'
        }

        It 'Should handle single row character' {
            $rows = @('X')
            $result = Format-FLFCharacter -Rows $rows
            $result | Should -HaveCount 1
            $result[0] | Should -Be 'X@@'
        }
    }

    Context 'When applying target width' {
        It 'Should pad narrow rows to target width' {
            $rows = @('AB', 'CD')
            $result = Format-FLFCharacter -Rows $rows -TargetWidth 5
            $result[0] | Should -Be 'AB   @'
            $result[1] | Should -Be 'CD   @@'
        }

        It 'Should not truncate rows wider than target' {
            $rows = @('ABCDEF')
            $result = Format-FLFCharacter -Rows $rows -TargetWidth 3
            $result[0] | Should -Be 'ABCDEF@@'
        }
    }

    Context 'When using custom endmark' {
        It 'Should use specified endmark character' {
            $rows = @('ABC', 'DEF')
            $result = Format-FLFCharacter -Rows $rows -Endmark '#'
            $result[0] | Should -Be 'ABC#'
            $result[1] | Should -Be 'DEF##'
        }
    }
}

Describe 'ConvertTo-BlockCharacters' {
    BeforeAll {
        $privatePath = Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'Private'
        . (Join-Path -Path $privatePath -ChildPath 'ConvertTo-BlockCharacters.ps1')
    }

    Context 'When converting brightness values to block characters' {
        It 'Should convert 0.0 brightness to space' {
            $pixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()
            $row = [System.Collections.Generic.List[double]]::new()
            $row.Add(0.0)
            $pixels.Add($row)

            $result = ConvertTo-BlockCharacters -Pixels $pixels
            $result[0] | Should -Be ' '
        }

        It 'Should convert 1.0 brightness to full block' {
            $pixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()
            $row = [System.Collections.Generic.List[double]]::new()
            $row.Add(1.0)
            $pixels.Add($row)

            $result = ConvertTo-BlockCharacters -Pixels $pixels
            $result[0] | Should -Be ([char]0x2588).ToString()  # █
        }

        It 'Should convert 0.5 brightness to medium shade' {
            $pixels = [System.Collections.Generic.List[System.Collections.Generic.List[double]]]::new()
            $row = [System.Collections.Generic.List[double]]::new()
            $row.Add(0.5)
            $pixels.Add($row)

            $result = ConvertTo-BlockCharacters -Pixels $pixels
            $result[0] | Should -Be ([char]0x2592).ToString()  # ▒
        }
    }
}

Describe 'New-FLFComment' {
    BeforeAll {
        $privatePath = Join-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -ChildPath 'Private'
        . (Join-Path -Path $privatePath -ChildPath 'New-FLFComment.ps1')
    }

    Context 'When generating comment lines' {
        It 'Should include font name' {
            $result = New-FLFComment -FontName 'TestFont' -SourcePath 'C:\test.ttf'
            $result[0] | Should -Match 'TestFont'
        }

        It 'Should include source file name' {
            $result = New-FLFComment -FontName 'TestFont' -SourcePath 'C:\Fonts\MyFont.ttf'
            $result[1] | Should -Match 'MyFont\.ttf'
        }

        It 'Should include generator info' {
            $result = New-FLFComment -FontName 'TestFont' -SourcePath 'test.ttf'
            $result[3] | Should -Match 'ttf2flf'
        }

        It 'Should return exactly 4 comment lines' {
            $result = New-FLFComment -FontName 'TestFont' -SourcePath 'test.ttf'
            $result | Should -HaveCount 4
        }
    }
}
