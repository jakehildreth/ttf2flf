# ttf2flf

Convert TrueType fonts (.ttf) to FIGlet font files (.flf) for ASCII art text rendering.

Supports two rendering modes:
- **Standard mode**: Traditional antialiased rendering with Unicode block characters
- **Pixel-perfect mode**: True bitmap font rendering using half-block characters (▀▄█)

## Requirements

- **PowerShell 7.0 or later** - Required due to SixLabors.Fonts dependency on .NET 6+
- Windows, macOS, or Linux

## Installation

```powershell
# Clone the repository
git clone https://github.com/jakehildreth/ttf2flf.git

# Import the module
Import-Module ./ttf2flf/ttf2flf.psd1
```

## Usage

### Pixel-Perfect Mode (Bitmap Fonts)

Best for bitmap/pixel fonts. Uses half-block characters where 2 pixel rows = 1 terminal row.

```powershell
# Auto-detect pixel height (works for many bitmap fonts)
ConvertTo-FLF -Path 'PressStart2P.ttf' -PixelPerfect

# Specify pixel height when auto-detection fails
ConvertTo-FLF -Path 'font.ttf' -PixelSize 15 -PixelPerfect

# Common pixel sizes for bitmap fonts: 5, 6, 8, 10, 12, 15, 16, 20, 24
```

**Note:** Works best with true bitmap fonts that have no antialiasing. Fonts with smoothing or gradients may not convert cleanly.

### Standard Mode (Regular Fonts)

For antialiased/outline fonts. Uses traditional Unicode block characters (░▒▓█).

```powershell
# Basic conversion
ConvertTo-FLF -Path 'Impact.ttf' -OutputPath 'Impact.flf'

# Custom height (more detail)
ConvertTo-FLF -Path 'Arial.ttf' -Height 12

# Enable smushed layout mode
ConvertTo-FLF -Path 'font.ttf' -Layout Smushed

# Monospace output (all characters same width)
ConvertTo-FLF -Path 'font.ttf' -Monospace

# Get FLF content without writing file
$content = ConvertTo-FLF -Path 'font.ttf' -PassThru
```

### Validate an FLF file

```powershell
# Validate a single file
Test-FLFFile -Path 'myfont.flf'

# Strict validation (warnings become errors)
Test-FLFFile -Path 'myfont.flf' -Strict

# Validate all FLF files in a directory
Get-ChildItem *.flf | Test-FLFFile | Where-Object { -not $_.IsValid }
```

### Pipeline support

```powershell
# Convert all TTF files in a directory
Get-ChildItem -Path 'C:\Fonts' -Filter '*.ttf' | ConvertTo-FLF -OutputPath 'output\'
```

## Parameters

### ConvertTo-FLF

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Path` | string | (required) | Path to TTF/OTF font file |
| `-Height` | int | 8 | FIGcharacter height in rows (4-32) - standard mode only |
| `-PixelSize` | int | auto | Pixel height for rendering (4-64) - pixel-perfect mode |
| `-PixelPerfect` | switch | false | Enable pixel-perfect mode (half-block characters) |
| `-OutputPath` | string | same as input | Output .flf file path |
| `-Hardblank` | char | `$` | Hardblank character for spacing |
| `-Layout` | string | FullWidth | Layout mode: FullWidth, Kerned, or Smushed |
| `-Monospace` | switch | false | Force all characters to same width |
| `-PassThru` | switch | false | Return content instead of writing file |

**Note:** `-Height` is for standard mode, `-PixelSize` is for pixel-perfect mode. Don't use both together.

### Test-FLFFile

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Path` | string | - | Path to FLF file to validate |
| `-Content` | string | - | FLF content as string |
| `-Strict` | switch | false | Treat warnings as errors |

## Layout Modes

| Mode | Description |
|------|-------------|
| **FullWidth** | Characters at full designed width, no compression |
| **Kerned** | Characters touch but don't overlap (fitting) |
| **Smushed** | Characters overlap using FIGlet smushing rules |

## Pixel-Perfect Mode Details

### How It Works

Pixel-perfect mode uses half-block characters to achieve true pixel-level rendering:
- **█** (U+2588) - Both pixel rows ON
- **▀** (U+2580) - Top pixel row ON, bottom OFF
- **▄** (U+2584) - Top pixel row OFF, bottom ON
- **Space** - Both pixel rows OFF

This means **2 pixel rows = 1 terminal row**, allowing precise bitmap font rendering.

### Auto-Detection

The module attempts to detect the native pixel height by:
1. Testing common bitmap font sizes (5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16, 20, 24, 32)
2. Rendering test characters at each size
3. Measuring "clean pixel ratio" (% of pixels that are fully on/off, not antialiased)
4. Selecting the smallest size with ≥85% clean pixels
5. Checking font name for pixel size hints (e.g., "Jersey20" → try size 20)

**Limitations:** Some fonts scale cleanly at multiple sizes, causing auto-detection to pick a smaller size than the design size. When this happens, manually specify `-PixelSize`.

### Manual Pixel Size Selection

When auto-detection fails or produces suboptimal results:

```powershell
# Try sizes around common bitmap heights
foreach ($size in 8, 10, 12, 15, 16, 20, 24) {
    ConvertTo-FLF -Path 'font.ttf' -PixelSize $size -PixelPerfect -OutputPath "test-$size.flf"
    figlet -f "test-$size.flf" "Test"
}
```

Look for the size where characters appear crisp and properly proportioned.

## Output

Generated FLF files include:

- 102 required FIGcharacters (ASCII 32-126 + German characters: Ä Ö Ü ä ö ü ß)
- Comment block with font name, source file, timestamp, and generator version
- **Standard mode:** Unicode block characters for shading (░▒▓█)
- **Pixel-perfect mode:** Half-block characters for precise pixel rendering (▀▄█)

### Auto-trimming

Empty rows at the top and bottom are automatically removed while preserving all character content. This creates more compact FLF files without losing any visual information.

## Using Generated Fonts

```bash
# With figlet
figlet -f ./myfont.flf "Hello World"

# With toilet (may not support half-block characters)
toilet -f ./myfont.flf "Hello World"

# Test in PowerShell
figlet -f ./PressStart2P.flf "Pixel Perfect"
```

**Note:** Pixel-perfect fonts with half-block characters require terminal support for Unicode box-drawing characters. Most modern terminals support this.

## Why PowerShell 7+?

This module uses [SixLabors.Fonts](https://github.com/SixLabors/Fonts) and [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) for cross-platform TTF parsing and glyph rendering. These libraries require .NET 6+, which is only available in PowerShell 7.

Windows PowerShell 5.1 uses .NET Framework 4.x and is not compatible.

## License

MIT

## Included Test Fonts

The repository includes 20 high-quality bitmap/pixel fonts that have been tested and optimized for FLF conversion:

### 04B Series (10 fonts)
Compact bitmap fonts by 04 - various sizes from 3px to 25px. Most require manual `-PixelSize 8` for optimal results.

### Specialty Fonts
- **5by5** - Ultra-compact 5x5 pixel font
- **blocco** - Geometric outline font (12px recommended)
- **Bytesized-Regular** - Tiny bitmap font (8px recommended)
- **JacquardaBastarda9-Regular** - Gothic/blackletter texture (13px recommended)
- **Micro5-Regular** - Minimal 5px font
- **negative-quinpix** - Inverted pixel style
- **PressStart2P** - Classic retro gaming font
- **Silkscreen** - Clean pixel font
- **Sixtyfour-Regular** - Commodore 64 inspired
- **Tiny5** - Compact pixel font

All fonts located in `Tests/TestData/SourceTTF/` with corresponding FLF files in `Tests/TestData/OutputFLF/KnownGood/`.

### Manual Size Requirements

Some fonts need explicit `-PixelSize` for best results:
- 04B_03B_, 04B_08__, 04B_21__, 04B_24__: `-PixelSize 8`
- blocco: `-PixelSize 12`
- Bytesized-Regular: `-PixelSize 8`
- JacquardaBastarda9-Regular: `-PixelSize 13`

## Links

- [FIGlet Font Specification](http://www.jave.de/figlet/figfont.html)
- [SixLabors.Fonts](https://github.com/SixLabors/Fonts)
- [FIGlet](http://www.figlet.org/)
