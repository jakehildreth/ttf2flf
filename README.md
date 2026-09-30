![ttf2flf logo, which is just the TrueType logo beside the Figlet logo on a white background.](ttf2flf.png)
# ttf2flf

Convert TrueType fonts (.ttf) to FIGlet font files (.flf) for ASCII art text rendering.

ttf2flf is a standalone **.NET command-line tool** (`src/ttf2flf`, produces a native
`ttf2flf` binary). It supports two rendering modes:

- **Pixel-perfect mode** (default): true bitmap font rendering using half-block
  characters (▀▄█), auto-detecting the font's native pixel grid
- **Standard mode**: traditional anti-aliased rendering with Unicode block
  characters (░▒▓█)

## Requirements

- .NET 8.0 SDK or later to build from source (or download a self-contained binary)
- Windows, macOS, or Linux

## Installation

```bash
# Clone the repository
git clone https://github.com/jakehildreth/ttf2flf.git
cd ttf2flf

# Run from source
dotnet run --project src/ttf2flf -- <font.ttf>

# Or publish a self-contained binary (example: macOS arm64)
dotnet publish src/ttf2flf -c Release -r osx-arm64 --self-contained -o dist/osx-arm64
# Binary is at dist/osx-arm64/ttf2flf
```

## Usage

```bash
# Pixel-perfect (default): auto-detect grid, minimal height (ceil(span/2) rows)
ttf2flf "PressStart2P.ttf" -o out/

# Convert many at once
ttf2flf fonts/*.ttf -o out/

# Pin the render size (skips auto-detection)
ttf2flf "font.ttf" --render-size 17

# Anti-aliased mode (░▒▓█ blocks); --height = terminal rows
ttf2flf "Impact.ttf" --aa --height 12

# Fixed-width output
ttf2flf "font.ttf" --monospace
```

### Options

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

### Output shape

**Height is the smallest number of rows that accurately represents the font:**
`ceil(tallest glyph's pixel span / 2)`. A 9px-tall font produces 5 rows, not 4 (odd
spans round up; the dangling top half-block's bottom pixel is simply off). Glyphs are
measured at their true proportional width, bottom-aligned so x-height, capitals, and
descenders keep their correct relationship, and given a 1px right spacer column so
letters stay legible.

## flfview (font previewer)

An interactive terminal browser for the generated fonts in `Corpus/`. Type a word and
it renders live in the selected font; switch fonts to compare the same word. Output
matches `figlet`'s FullWidth rendering byte-for-byte.

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

## Pixel-perfect mode details

Pixel-perfect mode uses half-block characters to achieve true pixel-level rendering:

- **█** (U+2588) — both pixel rows ON
- **▀** (U+2580) — top pixel row ON, bottom OFF
- **▄** (U+2584) — top pixel row OFF, bottom ON
- **Space** — both pixel rows OFF

So **2 pixel rows = 1 terminal row**, allowing precise bitmap font rendering.

### Auto-detection

Pixel-style fonts are outline fonts that mimic pixels — they carry no embedded bitmap
strike, so detection works from the font's geometry:

1. **Name hint.** A number ≥ 5 in the family name is the design grid
   (e.g., "Jacquard12" → 12, "Jersey20" → 20).
2. **Stroke-width alignment.** For thick-stroke fonts, render a probe glyph across
   candidate sizes and measure how often stroke widths are integer multiples of the
   thinnest stroke; the smallest such size is the grid.
3. **Fallback.** Thin-stroke fonts render cleanly at every size and default to 8.

The **render size** is computed separately from the grid as `grid × UnitsPerEm /
capHeightInUnits`, the size at which the rasterizer reproduces the design grid 1:1. The
bundled corpus fonts are calibrated per-font (see `Corpus/render_sizes.json`); override
any font with `--render-size`.

### Manual size selection

When auto-detection produces suboptimal results:

```bash
for size in 8 10 12 15 16 20 24; do
    ttf2flf "font.ttf" --render-size $size -o "test-$size.flf"
    figlet -f "test-$size.flf" "Test"
done
```

Look for the size where characters appear crisp and properly proportioned.

## Output

Generated FLF files include:

- 102 required FIGcharacters (ASCII 32-126 + German characters: Ä Ö Ü ä ö ü ß)
- Comment block with font name, source file, timestamp, and generator version
- **Pixel-perfect mode:** half-block characters for precise pixel rendering (▀▄█)
- **Standard mode:** Unicode block characters for shading (░▒▓█)

Empty rows at the top and bottom are removed (the minimal-height span), producing
compact FLF files without losing any visual information.

## Using generated fonts

```bash
# With figlet
figlet -f ./myfont.flf "Hello World"

# Or preview with the bundled viewer
flfview --render "Hello World" --font "myfont"
```

**Note:** Pixel-perfect fonts with half-block characters require terminal support for
Unicode box-drawing characters. Most modern terminals support this.

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

Browse them all with `flfview`.

## Links

- [FIGlet Font Specification](http://www.jave.de/figlet/figfont.html)
- [SixLabors.Fonts](https://github.com/SixLabors/Fonts)
- [FIGlet](http://www.figlet.org/)

## License

MIT License w/Commons Clause - see [LICENSE](LICENSE) file for details.

---

Made with 💜 by [Jake Hildreth](https://jakehildreth.com)
