"""
Builds src/Temizlikci.App/Assets/Temizlikci.ico from the macOS icon artwork in art/icon (the same three layers).

The SVGs hold only a vertical gradient and ring segments ("M … A … L … A … Z" around the center), so they are drawn
here directly instead of needing an SVG renderer. Windows shows icons as they are, without the macOS squircle mask,
so the tile gets rounded corners and a small margin.

Usage: python scripts/make-icon.py        (needs Pillow)
"""
import math
import pathlib
import re

from PIL import Image, ImageDraw

root = pathlib.Path(__file__).resolve().parent.parent
art = root / 'art' / 'icon'
output = root / 'src' / 'Temizlikci.App' / 'Assets' / 'Temizlikci.ico'

CANVAS = 1024
CENTER = 512
SUPERSAMPLE = 4
MARGIN = 0.04          # of the side, around the tile
CORNER = 0.22          # corner radius, of the tile's side
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def hex_color(text):
    text = text.lstrip('#')
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4))


def gradient_stops(svg):
    return [hex_color(color) for color in re.findall(r'stop-color="(#[0-9a-fA-F]{6})"', svg)]


def segments(svg):
    """(outer radius, inner radius, start angle, end angle, color) for each ring segment, angles clockwise from 12."""
    found = []
    for path, color in re.findall(r'<path d="([^"]+)" fill="(#[0-9a-fA-F]{6})"', svg):
        numbers = [float(n) for n in re.findall(r'-?\d+(?:\.\d+)?', path)]
        # M x0 y0 A R R 0 f s x1 y1 L x2 y2 A r r 0 f s x3 y3
        x0, y0, outer, _, _, _, _, x1, y1, x2, y2, inner = numbers[:12]
        start = math.atan2(x0 - CENTER, -(y0 - CENTER))
        end = math.atan2(x1 - CENTER, -(y1 - CENTER))
        if end <= start:
            end += 2 * math.pi
        found.append((outer, inner, start, end, hex_color(color)))
    return found


def sector(outer, inner, start, end, scale):
    steps = max(8, int((end - start) * outer * scale / 6))
    points = []
    for i in range(steps + 1):
        angle = start + (end - start) * i / steps
        points.append(((CENTER + outer * math.sin(angle)) * scale, (CENTER - outer * math.cos(angle)) * scale))
    for i in range(steps + 1):
        angle = end - (end - start) * i / steps
        points.append(((CENTER + inner * math.sin(angle)) * scale, (CENTER - inner * math.cos(angle)) * scale))
    return points


def render():
    scale = SUPERSAMPLE
    side = CANVAS * scale
    image = Image.new('RGBA', (side, side), (0, 0, 0, 0))

    top, bottom = gradient_stops((art / '0-background.svg').read_text(encoding='utf-8'))
    background = Image.new('RGBA', (side, side))
    draw = ImageDraw.Draw(background)
    for y in range(side):
        t = y / (side - 1)
        draw.line([(0, y), (side, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))

    margin = round(side * MARGIN)
    mask = Image.new('L', (side, side), 0)
    ImageDraw.Draw(mask).rounded_rectangle([margin, margin, side - margin - 1, side - margin - 1], radius=round((side - 2 * margin) * CORNER), fill=255)
    image.paste(background, (0, 0), mask)

    draw = ImageDraw.Draw(image)
    for name in ('1-rings.svg', '2-lifted-segment.svg'):
        for outer, inner, start, end, color in segments((art / name).read_text(encoding='utf-8')):
            draw.polygon(sector(outer, inner, start, end, scale), fill=color + (255,))
    return image.resize((CANVAS, CANVAS), Image.LANCZOS)


if __name__ == '__main__':
    master = render()
    output.parent.mkdir(parents=True, exist_ok=True)
    master.save(output, format='ICO', sizes=[(size, size) for size in SIZES])
    # The title bar shows the icon at 16 px; 64 px keeps it crisp up to 400% scaling.
    master.resize((64, 64), Image.LANCZOS).save(output.parent / 'TitleBarIcon.png')
    master.resize((256, 256), Image.LANCZOS).save(art / 'Temizlikci-256.png')
    print(f'wrote {output.relative_to(root)} ({", ".join(map(str, SIZES))})')
