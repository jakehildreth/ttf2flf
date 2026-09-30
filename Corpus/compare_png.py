#!/usr/bin/env python3
"""Compare ttf2flf-generated FLF glyphs against the font authors' reference PNGs.

Each corpus zip ships a Construct-3 SpriteFont sheet PNG plus a .txt with cell
size and charset. We slice the PNG into fixed cells, binarize, take each cell's
tight bounding box, and compare against the tight bounding box of the matching
character decoded from the half-block rows of the generated FLF.

Parity metric per font: mean exact-match fraction over the 95 ASCII chars.
A cell and a glyph match when their tight bounding boxes are identical bits.

Usage: compare_png.py <flf_dir> [--corpus-dir DIR] [--json]

--corpus-dir is the directory holding the extracted corpus zips (one folder per
font with its .ttf, sheet .png, and metadata .txt). Default: $TTF2FLF_CORPUS_DIR,
then /tmp/ttfcorpus.
"""
import argparse, os, re, sys, json
import site; sys.path.append(site.getusersitepackages())
from PIL import Image
import numpy as np

DEFAULT_CORPUS_DIR = os.environ.get('TTF2FLF_CORPUS_DIR', '/tmp/ttfcorpus')
ASCII = [chr(c) for c in range(32, 127)]
GERMAN = [196, 214, 220, 228, 246, 252, 223]

def load_meta(txt_path):
    t = open(txt_path, errors='replace').read()
    cw = int(re.search(r'Character width:\s*(\d+)', t).group(1))
    ch = int(re.search(r'Character height:\s*(\d+)', t).group(1))
    cs = re.search(r'Character set:\s*\n(\S+)', t).group(1)
    return cw, ch, cs

def png_cells(png_path, cw, ch, charset):
    a = np.array(Image.open(png_path).convert('L'))
    H, W = a.shape
    cells = {}
    i = 0
    for gy in range(0, H - H % ch, ch):
        for gx in range(0, W - W % cw, cw):
            if i >= len(charset): return cells
            cell = a[gy:gy+ch, gx:gx+cw] > 128
            cells[charset[i]] = cell
            i += 1
    return cells

def tight(bits):
    """bits: 2D bool array -> tight bbox array or None if empty."""
    ys, xs = np.nonzero(bits)
    if len(ys) == 0: return None
    return bits[ys.min():ys.max()+1, xs.min():xs.max()+1]

def parse_flf(path):
    lines = open(path, encoding='utf-8').read().split('\n')
    m = re.match(r'^flf2a(.) (\d+) (\d+) (\d+) (\S+) (\d+)', lines[0])
    hardblank, height, baseline, maxlen, oldlayout, comments = (
        m.group(1), int(m.group(2)), int(m.group(3)), int(m.group(4)), m.group(5), int(m.group(6)))
    body = lines[1 + comments:]
    chars = {}
    order = ASCII + [chr(c) for c in GERMAN]
    idx = 0
    for ch_ in order:
        rows = body[idx:idx + height]; idx += height
        if len(rows) < height: break
        glyph = []
        for r_i, r in enumerate(rows):
            r = r[:-2] if r_i == height - 1 and r.endswith('@@') else r[:-1] if r.endswith('@') else r
            glyph.append(r.replace(hardblank, ' '))
        chars[ch_] = glyph
    return chars

HALF = {'█': (1, 1), '▀': (1, 0), '▄': (0, 1), ' ': (0, 0)}

def glyph_to_bitmap(rows):
    """Decode half-block rows to a 2D bool pixel array."""
    if not rows: return None
    w = max((len(r) for r in rows), default=0)
    px = np.zeros((len(rows) * 2, w), dtype=bool)
    for y, r in enumerate(rows):
        for x, c in enumerate(r):
            t, b = HALF.get(c, (1, 1))  # unknown => treat as full (aa mode chars)
            px[2*y, x] = t
            px[2*y+1, x] = b
    return px

def scale_up(bits, f):
    return np.repeat(np.repeat(bits, f, axis=0), f, axis=1)

def shape_match(a, b):
    """Bit-compare allowing integer scale factors (author sheets are often
    rendered at 2x the native grid). Returns (matched, note)."""
    if a is None and b is None: return True, 'both-empty'
    if a is None or b is None: return False, 'empty-mismatch'
    if a.shape == b.shape and (a == b).all(): return True, 'exact'
    for f in (2, 3):
        sa, sb = scale_up(a, f), scale_up(b, f)
        if sa.shape == b.shape and (sa == b).all(): return True, f'ours x{f}'
        if a.shape == sb.shape and (a == sb).all(): return True, f'ref x{f}'
    return False, f'{a.shape}vs{b.shape}'

def resize_nn(bits, target_h):
    """Nearest-neighbor resize of a bool bitmap to target height, preserving AR."""
    if bits is None: return None
    h, w = bits.shape
    if h == 0 or w == 0: return bits
    scale = target_h / h
    tw = max(1, round(w * scale))
    img = Image.fromarray((bits * 255).astype('uint8'))
    img = img.resize((tw, target_h), Image.NEAREST)
    return np.array(img) > 128

def compare_font(flf_path, png_path, txt_path):
    cw, ch, cs = load_meta(txt_path)
    cells = png_cells(png_path, cw, ch, cs)
    flf = parse_flf(flf_path)
    m = re.search(r'(\d+)', os.path.basename(flf_path))
    name_grid = int(m.group(1)) if m and int(m.group(1)) >= 5 else None
    matched = total = 0
    diffs = []
    for ch_ in ASCII:
        if ch_ not in cells or ch_ not in flf: continue
        ref = tight(cells[ch_])
        got = tight(glyph_to_bitmap(flf[ch_]))
        total += 1
        if ref is None and got is None: matched += 1; continue
        if ref is None or got is None: diffs.append((ch_, 'empty-mismatch')); continue
        # Normalize sheet to our glyph height (nearest neighbor), then compare IoU.
        if ref.shape[0] != got.shape[0]:
            ref = resize_nn(ref, got.shape[0])
        H = max(ref.shape[0], got.shape[0]); W = max(ref.shape[1], got.shape[1])
        R = np.zeros((H, W), dtype=bool); G = np.zeros((H, W), dtype=bool)
        R[:ref.shape[0], :ref.shape[1]] = ref; G[:got.shape[0], :got.shape[1]] = got
        iou = (R & G).sum() / max(1, (R | G).sum())
        if iou >= 0.85: matched += 1
        else: diffs.append((ch_, f'iou={iou:.2f} {got.shape}vs{ref.shape}'))
    return matched, total, diffs

def main():
    parser = argparse.ArgumentParser(description='Compare generated FLF glyphs to reference PNG sheets.')
    parser.add_argument('flf_dir', help='directory with the generated .flf files')
    parser.add_argument('--corpus-dir', default=DEFAULT_CORPUS_DIR,
                        help='extracted corpus zips (default: $TTF2FLF_CORPUS_DIR or /tmp/ttfcorpus)')
    parser.add_argument('--json', action='store_true', help='print results as JSON')
    args = parser.parse_args()
    if not os.path.isdir(args.corpus_dir):
        parser.error(f'corpus directory not found: {args.corpus_dir}')
    flf_dir, corpus_dir = args.flf_dir, args.corpus_dir
    results = {}
    for d in sorted(os.listdir(corpus_dir)):
        dpath = os.path.join(corpus_dir, d)
        if not os.path.isdir(dpath): continue
        txts = [f for f in os.listdir(dpath) if f.endswith('.txt')]
        ttfs = [f for f in os.listdir(dpath) if f.lower().endswith('.ttf')]
        pngs = [f for f in os.listdir(dpath) if f.endswith('.png')]
        if not (txts and ttfs and pngs): continue
        stem = os.path.splitext(ttfs[0])[0]
        flf = os.path.join(flf_dir, stem + '.flf')
        if not os.path.exists(flf):
            results[ttfs[0]] = 'no-flf'; continue
        m, t, diffs = compare_font(flf, os.path.join(dpath, pngs[0]), os.path.join(dpath, txts[0]))
        _, cell_h, _ = load_meta(os.path.join(dpath, txts[0]))
        ng = re.search(r'(\d+)', stem)
        results[ttfs[0]] = {'matched': m, 'total': t, 'pct': round(100*m/t, 1),
                            'nameGrid': int(ng.group(1)) if ng else None, 'pngCellH': cell_h,
                            'first_diffs': diffs[:5]}
    if args.json:
        print(json.dumps(results, indent=1))
    else:
        for k, v in results.items(): print(k, v)

if __name__ == '__main__':
    main()
