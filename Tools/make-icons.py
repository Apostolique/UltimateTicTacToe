#!/usr/bin/env python3
"""Draws the game's icon and writes every format the platforms need.

    python Tools/make-icons.py

One source of truth beats six binaries that drift apart. The palette and the
proportions match what BoardView draws, so the icon and the game agree.

Needs Pillow: python -m pip install pillow
"""

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent

# Straight out of TWColor, so the icon and the board can't drift apart.
GRAY_900 = (17, 24, 39, 255)
GRAY_800 = (31, 41, 55, 255)
GRAY_100 = (243, 244, 246, 255)
GRAY_700 = (55, 65, 81, 255)
RED_400, RED_600 = (248, 113, 113, 255), (220, 38, 38, 255)
BLUE_400, BLUE_600 = (96, 165, 250, 255), (37, 99, 235, 255)

# Drawn big and resampled down, because diagonals and round caps alias badly
# when they're rasterized at 16 px directly.
MASTER = 1024


def vertical_gradient(size, top, bottom):
    """A top-to-bottom ramp, the same direction the marks use in game."""
    grad = Image.new("RGBA", (1, size))
    for y in range(size):
        t = y / max(size - 1, 1)
        grad.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return grad.resize((size, size))


# Stroke thickness as a fraction of the mark, so a mark is never swamped by its
# own outline the way a fixed width does at small box sizes.
STROKE = 0.19


def draw_x(draw, box):
    """Two strokes corner to corner, pulled in so the X fits the circle an O would."""
    x0, y0, x1, y1 = box
    width = (x1 - x0) * STROKE
    # An X drawn to the full box reads bigger than a circle of the same box,
    # because its arms reach the corners. Pull them in until the two balance.
    arm = ((x1 - x0) * 0.5 - width / 2) * 0.78
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    for a, b in (((-1, -1), (1, 1)), ((-1, 1), (1, -1))):
        draw.line(
            [(cx + a[0] * arm, cy + a[1] * arm), (cx + b[0] * arm, cy + b[1] * arm)],
            fill=255, width=round(width))
        # PIL leaves line ends square, so round them by hand.
        for dx, dy in (a, b):
            r = width / 2
            draw.ellipse([cx + dx * arm - r, cy + dy * arm - r,
                          cx + dx * arm + r, cy + dy * arm + r], fill=255)


def draw_o(draw, box):
    x0, y0, x1, y1 = box
    width = (x1 - x0) * STROKE
    inset = width / 2
    draw.ellipse([x0 + inset, y0 + inset, x1 - inset, y1 - inset],
                 outline=255, width=round(width))


def capped_line(draw, a, b, width, fill):
    """A line with round ends, the shape FillLine gives the grid in game."""
    draw.line([a, b], fill=fill, width=round(width))
    r = width / 2
    for x, y in (a, b):
        draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def render(size=MASTER, detail=True):
    s = size
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # A rounded square rather than a full bleed one: macOS draws the icon as
    # given and a hard square reads as a foreign body next to everything else.
    plate = Image.new("L", (s, s), 0)
    ImageDraw.Draw(plate).rounded_rectangle([0, 0, s - 1, s - 1], radius=round(s * 0.2), fill=255)
    img.paste(Image.composite(
        vertical_gradient(s, GRAY_800, GRAY_900),
        Image.new("RGBA", (s, s), GRAY_900),
        Image.new("L", (s, s), 255)), (0, 0), plate)

    pad = s * 0.13
    board = s - pad * 2
    cell = board / 3
    # The small sizes carry less, so what's left has to be heavier to survive.
    grid_w = s * (0.03 if detail else 0.036)

    # The nested grid in the middle cell is what makes this ultimate tic-tac-toe
    # rather than the ordinary kind. Below about 64 px it stops being legible and
    # turns the middle into noise, so the small sizes are drawn without it.
    if detail:
        micro = cell / 3
        mx, my = pad + cell, pad + cell
        micro_layer = ImageDraw.Draw(img)
        for i in (1, 2):
            capped_line(micro_layer, (mx + i * micro, my + micro * 0.15),
                        (mx + i * micro, my + cell - micro * 0.15), s * 0.011, GRAY_700)
            capped_line(micro_layer, (mx + micro * 0.15, my + i * micro),
                        (mx + cell - micro * 0.15, my + i * micro), s * 0.011, GRAY_700)

    grid = ImageDraw.Draw(img)
    for i in (1, 2):
        capped_line(grid, (pad + i * cell, pad), (pad + i * cell, pad + board), grid_w, GRAY_100)
        capped_line(grid, (pad, pad + i * cell), (pad + board, pad + i * cell), grid_w, GRAY_100)

    # Marks go on a diagonal so the icon stays balanced at any size.
    for kind, (col, row), top, bottom in (
        ("x", (0, 0), RED_400, RED_600),
        ("o", (2, 2), BLUE_400, BLUE_600),
    ):
        inner = cell * (0.13 if detail else 0.05)
        box = (pad + col * cell + inner, pad + row * cell + inner,
               pad + (col + 1) * cell - inner, pad + (row + 1) * cell - inner)
        mask = Image.new("L", (s, s), 0)
        d = ImageDraw.Draw(mask)
        (draw_x if kind == "x" else draw_o)(d, box)
        img.paste(vertical_gradient(s, top, bottom), (0, 0), mask)

    return img


def at(px):
    """One icon at one size, drawn 4x and resampled so the diagonals stay smooth."""
    return render(max(px * 4, 512), detail=px >= 64).resize((px, px), Image.LANCZOS)


def png_bytes(image):
    buf = io.BytesIO()
    image.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_icns(path, sizes):
    """Flat container: 'icns', total length, then OSType + length + PNG each."""
    types = [(b"icp4", 16), (b"icp5", 32), (b"icp6", 64), (b"ic07", 128),
             (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024), (b"ic11", 32),
             (b"ic12", 64), (b"ic13", 256), (b"ic14", 512)]
    body = b""
    for ostype, px in types:
        data = png_bytes(sizes[px])
        body += ostype + struct.pack(">I", len(data) + 8) + data
    path.write_bytes(b"icns" + struct.pack(">I", len(body) + 8) + body)
    return len(body) + 8


def write_ico(path, sizes, wanted):
    """
    Written by hand rather than through Pillow, which resamples one image for
    every entry. Each size here is drawn for itself, so the small ones can leave
    out detail that would only smear. PNG payloads, which Windows has taken
    since Vista.
    """
    payloads = [png_bytes(sizes[px]) for px in wanted]
    offset = 6 + 16 * len(wanted)
    entries = b""
    for px, data in zip(wanted, payloads):
        dim = 0 if px >= 256 else px  # 0 means 256 in the directory entry
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(struct.pack("<HHH", 0, 1, len(wanted)) + entries + b"".join(payloads))
    return path.stat().st_size


def main():
    ico_wanted = [16, 24, 32, 48, 64, 128, 256]
    needed = sorted({16, 24, 32, 48, 64, 128, 256, 512, 1024})
    sizes = {px: at(px) for px in needed}

    for rel in ("Platforms/DesktopGL/Icon.ico",
                "Platforms/WindowsDX/Icon.ico",
                "Platforms/DesktopGL.KNI/Icon.ico",
                "Platforms/WindowsDX.KNI/Icon.ico",
                "Platforms/BlazorGL.KNI/wwwroot/favicon.ico"):
        print(f"{rel:52} {write_ico(ROOT / rel, sizes, ico_wanted):>8} bytes")

    icns_out = ROOT / "Platforms/DesktopGL/Icon.icns"
    print(f"{'Platforms/DesktopGL/Icon.icns':52} {write_icns(icns_out, sizes):>8} bytes")

    # SDL wants a bitmap for the window icon, and BMP has no alpha, so flatten
    # onto the same background the plate already uses.
    bmp = Image.new("RGB", (128, 128), GRAY_900[:3])
    bmp.paste(sizes[128], (0, 0), sizes[128])
    for rel in ("Platforms/DesktopGL/Icon.bmp",
                "Platforms/DesktopGL.KNI/Icon.bmp"):
        bmp_out = ROOT / rel
        bmp.save(bmp_out, format="BMP")
        print(f"{rel:52} {bmp_out.stat().st_size:>8} bytes")

    preview = ROOT / "Images/icon.png"
    preview.parent.mkdir(exist_ok=True)
    sizes[256].save(preview, format="PNG", optimize=True)
    print(f"{'Images/icon.png':52} {preview.stat().st_size:>8} bytes")


if __name__ == "__main__":
    main()
