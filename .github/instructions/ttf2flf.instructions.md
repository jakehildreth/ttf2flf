---
applyTo: '**/*.ps1,**/*.psm1,**/*.psd1'
description: 'TTF to FLF conversion module development guidelines and project context'
---

# ttf2flf Project Instructions

## Project Overview

**ttf2flf** is a PowerShell module that converts TrueType fonts (.ttf) to FIGlet font files (.flf). It uses SixLabors libraries for font rendering and outputs Unicode block characters for the ASCII art representation.

## Architecture

### Dependencies

- **SixLabors.ImageSharp** (v3.0.0) - Image processing
- **SixLabors.Fonts** - TTF parsing and font metrics
- **SixLabors.ImageSharp.Drawing** - Text rendering via DrawText extensions
- **PowerShell 7.0+** - Required due to SixLabors .NET 6+ dependency

### Assembly Loading

Assemblies must load in specific order due to dependencies:
1. SixLabors.ImageSharp
2. SixLabors.Fonts  
3. SixLabors.ImageSharp.Drawing

### Module Structure

```
ttf2flf/
    ttf2flf.psd1          # Module manifest (CalVer versioning)
    ttf2flf.psm1          # Module loader, script-scope variables
    Public/
        ConvertTo-FLF.ps1     # Main conversion cmdlet
        Test-FLFFile.ps1      # FLF validation function
    Private/
        Get-GlyphBitmap.ps1           # Render character to pixel array
        ConvertTo-BlockCharacters.ps1 # Pixel brightness to Unicode blocks
        Format-FLFCharacter.ps1       # Add FLF endmarks (@/@@)
        New-FLFHeader.ps1             # Generate FLF header line
        New-FLFComment.ps1            # Generate comment lines
        Test-FontCompatibility.ps1    # Detect symbol/emoji fonts
    Lib/
        SixLabors.*.dll       # Bundled assemblies
    Tests/
        ttf2flf.Tests.ps1     # Pester v5 tests
        TestData/
            SourceTTF/        # Source TTF fonts for testing
            OutputFLF/        # Generated FLF output files
            Reference/        # Reference files for validation
```

## FLF Format Specification

### Required Characters (102 total)
- ASCII 32-126 (95 printable characters)
- German characters: Ä Ö Ü ä ö ü ß (code points 196, 214, 220, 228, 246, 252, 223)

### Header Format
```
flf2a$ Height Baseline MaxLength OldLayout CommentLines PrintDirection FullLayout
```

### Layout Modes
- **FullWidth** (-1): No kerning, characters separated
- **Kerned** (0): Characters fit together without overlapping
- **Smushed** (63): Characters overlap using smush rules

### Endmarks
- Single `@` at end of each row except the last
- Double `@@` at end of the final row of each character

## Rendering Pipeline

### Current Implementation (Anti-aliased)

1. Load font at large size (Height * 8, min 48pt) for quality
2. Calculate font metrics (ascender, descender, line height)
3. Render each character to ImageSharp image (white on black)
4. Downsample to target height using block averaging
5. Convert brightness values to Unicode block characters (░▒▓█)
6. Format with FLF endmarks

### Unicode Block Characters (Current)
- ` ` (space) - 0.0-0.2 brightness
- `░` - 0.2-0.4 brightness  
- `▒` - 0.4-0.6 brightness
- `▓` - 0.6-0.8 brightness
- `█` - 0.8-1.0 brightness

## Planned: Pixel-Perfect Bitmap Mode

For bitmap/pixel fonts, auto-detect the native pixel height and use half-block characters where each terminal row represents 2 pixel rows.

### Pixel Height Auto-Detection
Pixel-style fonts are outline fonts mimicking pixels (no embedded bitmap strike), so
detection uses geometry, not strike tables:
1. **Name hint** — a number ≥ 5 in the family name is the design grid (Jacquard12 → 12).
2. **Stroke-width alignment** — binarize a probe glyph across sizes; the fraction of
   stroke run-lengths that are integer multiples of the min run stays ~100% at the native
   grid and its integer multiples, and breaks off-grid. Smallest aligned fundamental wins.
3. **Fallback** — thin-stroke fonts give no signal; default 8.
Recommended render size = smallest integer multiple of the grid ≥ 16px (supersampling
thickens strokes, repairs anti-aliased edges). `Get-BitmapFontPixelHeight` returns
`{ Height = recommended; Width }`; `-NoSupersample` returns the bare grid.
Output terminal rows: render_height / 2 (rounded up).

### Half-Block Characters
- `█` (U+2588) - Both pixels on
- `▀` (U+2580) - Top pixel on, bottom off
- `▄` (U+2584) - Top pixel off, bottom on  
- ` ` (space) - Both pixels off

### Benefits
- True 1:1 pixel mapping for bitmap fonts
- 8-pixel tall font = 4 FLF rows
- Crisp, pixel-perfect rendering
- No anti-aliasing artifacts

### Detection Notes
- UnitsPerEm is NOT a reliable grid proxy (Jewel=1024 divides by both 16 and 32).
- Outline-mimic fonts have gray edge pixels at native size; a fixed cleanliness
  threshold both admits crushed sizes and misses integer-multiple supersamples.

## SixLabors v3 API Notes

### Mutate Pattern for PowerShell
Extension methods don't work directly in PowerShell. Use:
```powershell
$action = [System.Action[SixLabors.ImageSharp.Processing.IImageProcessingContext]] {
    param($ctx)
    [SixLabors.ImageSharp.Drawing.Processing.DrawTextExtensions]::DrawText(
        $ctx, $renderOptions, $charString, $brush
    )
}.GetNewClosure()

[SixLabors.ImageSharp.Processing.ProcessingExtensions]::Mutate($image, $action)
```

The `.GetNewClosure()` is critical - it captures variables from the outer scope.

### Font Metrics
```powershell
$metrics = $Font.FontMetrics
$scale = $Font.Size / $metrics.UnitsPerEm
$ascender = $metrics.HorizontalMetrics.Ascender * $scale
$descender = [Math]::Abs($metrics.HorizontalMetrics.Descender * $scale)
$lineHeight = $ascender + $descender
```

## Testing

### Directory Structure
- **SourceTTF/** - Input TTF fonts for testing (only use fonts from this directory)
- **OutputFLF/** - Generated FLF files (write all test output here)
- **Reference/** - Expected output for validation

### Requirements
- Pester v5 for unit tests
- figlet (Homebrew: `brew install figlet`) for functional testing

### Running Tests
```powershell
Invoke-Pester -Output Detailed
```

### Test Fonts (in SourceTTF/)
- **PressStart2P.ttf** - 8px bitmap font, ideal for pixel-perfect testing
- **Silkscreen.ttf** - 8px bitmap font
- **Tiny5.ttf** - 5px bitmap font (ultra-compact)
- **DejaVuSansMono.ttf** - Scalable monospace font
- **PublicPixel.ttf** - Bitmap font (may have issues)

### Test Coverage
- Test-FLFFile validation (valid/invalid files, strict mode)
- New-FLFHeader generation (layout modes, hardblanks)
- Format-FLFCharacter (endmarks, padding)
- ConvertTo-BlockCharacters (brightness thresholds)
- New-FLFComment (metadata lines)

## ConvertTo-FLF Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| Path | string | (required) | Path to TTF file |
| Height | int | 8 | Target height in rows |
| OutputPath | string | auto | Output FLF file path |
| Hardblank | char | $ | Character for hard spaces |
| Layout | string | FullWidth | FullWidth, Kerned, or Smushed |
| Monospace | switch | false | Force fixed width characters |
| PassThru | switch | false | Return FileInfo object |

## Known Issues and Solutions

1. **Assembly load failures**: Ensure correct load order in psm1
2. **Extension methods fail**: Use Mutate pattern with GetNewClosure()
3. **Truncated glyphs**: Use full line height (ascender + descender)
4. **Test variable scope**: Private functions need fallback values for `$script:` variables when dot-sourced directly

## Font Recommendations

### Best Results (Pixel Fonts)
- Press Start 2P - Classic 8-bit style
- Pixel fonts with clear grid alignment

### Good Results (Monospace)
- Courier New - Clean typewriter style
- Andale Mono - Good monospace rendering

### Acceptable (Anti-aliased)
- Monaco - Soft edges, less distinct
- Any TrueType font (quality varies)
