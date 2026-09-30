![ttf2flf logo, which is just the TrueType logo beside the Figlet logo on a white background.](ttf2flf.png)
# ttf2flf

Convert TrueType fonts (.ttf) to FIGlet font files (.flf) for ASCII art text rendering.

Available as both a **PowerShell module** and a standalone **C# command-line tool**
(`src/ttf2flf`, produces a native `ttf2flf` binary). Both share the same conversion
pipeline.

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

## C# CLI (`ttf2flf`)

A standalone .NET console app with no PowerShell dependency. Pixel-perfect mode is the
default; it auto-detects the font's native pixel grid and renders 1:1 for crisp,
minimal-height output.

### Build

```bash
# Run from source
dotnet run --project src/ttf2flf -- <font.ttf>

# Publish a self-contained binary (example: macOS arm64)
dotnet publish src/ttf2flf -c Release -r osx-arm64 --self-contained -o dist/osx-arm64
```

### Usage

```bash
# Pixel-perfect (default): auto-detect grid, minimal height (ceil(span/2) rows)
ttf2flf "PressStart2P.ttf" -o out/

# Convert many at once
ttf2flf fonts/*.ttf -o out/

# Pin the render size (skips auto-detection)
ttf2flf "font.ttf" --render-size 17

# Anti-aliased mode (░▒▓█ blocks), --height = terminal rows
ttf2flf "Impact.ttf" --aa --height 12

# Fixed-width output
ttf2flf "font.ttf" --monospace
```

| Option | Default | Description |
|--------|---------|-------------|
| `-o`, `--output` | same as input | Output .flf path or directory |
| `--render-size` | auto | Explicit pixel render size (px) |
| `--height` | 8 | Row height (pixel-perfect: px; `--aa`: terminal rows) |
| `--aa` | off | Anti-aliased mode instead of half-block |
| `--monospace` | off | Pad all glyphs to max advance width |
| `--no-trim` | off | Keep empty top/bottom rows |
| `--hardblank` | `$` | Hardblank character |
| `--layout` | FullWidth | FullWidth, Kerned, or Smushed |
| `--units-per-pixel` | auto | Override detection (grid = UnitsPerEm / n) |
| `-v`, `--verbose` | off | Verbose logging |

**Output height is the smallest number of rows that accurately represents the font:**
`ceil(tallest glyph's pixel span / 2)`. A 9px-tall font produces 5 rows, not 4 (odd
spans round up; the dangling top half-block's bottom pixel is simply off). Glyphs are
measured at their true proportional width and bottom-aligned so x-height, capitals, and
descenders keep their correct relationship.

## flfview (font previewer)

An interactive terminal browser for the generated fonts in `Corpus/`. Type a word and
it renders live in the selected font; switch fonts to compare the same word.

```bash
dotnet run --project src/flfview          # interactive
src/flfview/bin/Release/net10.0/flfview   # or the built binary
```

| Key / command | Action |
|----------------|--------|
| printable chars | Append to word (live re-render) |
| `Backspace` | Delete last char |
| `Up`/`Down`, `Tab`/`Shift+Tab` | Previous / next font |
| `PgUp`/`PgDn` | Jump ±10 fonts |
| `/all <word>` | Render the word in every font, paged |
| `/font <name>` | Jump to a font by name |
| `/tier all\|verified\|clean\|approximate` | Filter the font list by quality tier |
| `/fonts` | List all fonts |
| `/clear`, `/help`, `/quit` | — |
| `Esc` / `Ctrl+C` | Exit |

One-shot (scriptable) mode:

```bash
flfview --render "Hello" --font "Jewel 6"
flfview --render "Sphinx" --all
flfview --fonts
```

Fonts are tagged by quality tier: `[v]` verified (bitmap-exact against the author's
reference sheet), `[ ]` clean, `[~]` approximate (decorative/stroked source).

## PowerShell module usage

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

Pixel-style fonts are outline fonts that mimic pixels — they carry no embedded bitmap
strike, so detection works from the font's geometry, in priority order:

1. **Name hint.** A number ≥ 5 in the family name is the design grid
   (e.g., "Jacquard12" → 12, "Jersey20" → 20). This is the most reliable signal.
2. **Stroke-width alignment.** For thick-stroke fonts, render a probe glyph across
   candidate sizes and measure how often stroke widths are integer multiples of the
   thinnest stroke. The native grid (and its integer multiples) keeps strokes uniform;
   off-grid sizes break alignment. The smallest such size is the grid.
3. **Fallback.** Thin-stroke fonts render cleanly at every size and give no signal;
   they default to 8.

The recommended render size is then the smallest integer multiple of the grid that
reaches a 16px legibility floor (native 8 → render 16). Supersampling thickens 1px
strokes to 2px and repairs anti-aliased edges.

**Limitations:** Detection finds the font's *native grid*, not necessarily the most
legible size. If output looks too thin, try a higher `-PixelSize` (e.g., a 2x multiple
of the grid). Auto-detection can't know your aesthetic preference.

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

## Bundled font corpus

`Corpus/` holds the converted fonts and the reference material used to calibrate the
converter:

- `Corpus/OutputFLF/` — 55 pixel/bitmap fonts converted and verified bitmap-exact
  against each author's reference sheet.
- `Corpus/FontBookFLF/` — additional pixel fonts converted from an installed font
  library (the `name_Xh`/`name_Xp` variants plus extras like 3270, UniVGA16, C64 Pro).
- `Corpus/Reference/` — the authors' reference sprite-sheet PNGs.
- `Corpus/render_sizes.json` — the calibrated per-font render sizes.
- `Corpus/compare_png.py` — the parity harness that compares generated FLF glyphs to the
  reference PNGs.

Browse them all with `flfview`. The older PowerShell test fonts live in
`Tests/TestData/SourceTTF/`.

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

## License

MIT License w/Commons Clause - see [LICENSE](LICENSE) file for details.

---

Made with 💜 by [Jake Hildreth](https://jakehildreth.com)
