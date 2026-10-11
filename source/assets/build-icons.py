"""Build the shared EdgeLink mark, PNG, Windows ICO and macOS ICNS on any OS.

Requires Pillow: python -m pip install Pillow
Geometry below is the single source for both SVG and antialiased raster output.
"""
from pathlib import Path
import argparse
import math
import struct

from PIL import Image, ImageDraw

SIZES = (16, 24, 32, 48, 64, 128, 256, 512, 1024)
BACKGROUND = "#14272d"
MINT = "#59e2c7"
STROKE = 64
SUPERSAMPLE = 4
TILE = (64, 64, 960, 960)
TILE_RADIUS = 196
# A geometric EL monogram: common stroke, rounded terminals and shared baseline.
# Both letters are optically centered as a group; no screen outlines remain.
LETTERS = (
    (("M", 444, 328), ("L", 316, 328), ("A", 316, 400, 72, 270, 180),
     ("L", 244, 624), ("A", 316, 624, 72, 180, 90), ("L", 444, 696)),
    (("M", 244, 512), ("L", 444, 512)),
    (("M", 580, 328), ("L", 580, 624),
     ("A", 652, 624, 72, 180, 90), ("L", 780, 696)),
)


def write_icns(assets):
    entries = ((b"icp4", 16), (b"icp5", 32), (b"icp6", 64),
               (b"ic07", 128), (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024),
               (b"ic11", 32), (b"ic12", 64), (b"ic13", 256), (b"ic14", 512))
    chunks = []
    for kind, size in entries:
        data = (assets / f"icon-{size}.png").read_bytes()
        chunks.append(struct.pack(">4sI", kind, len(data) + 8) + data)
    payload = b"".join(chunks)
    (assets / "EdgeLink.icns").write_bytes(struct.pack(">4sI", b"icns", len(payload) + 8) + payload)


def path_points(commands):
    points = []
    for command, *args in commands:
        if command in ("M", "L"):
            points.append(tuple(args))
        else:
            cx, cy, radius, start, end = args
            for step in range(1, 97):
                angle = math.radians(start+(end-start)*step/96)
                points.append((cx+radius*math.cos(angle), cy+radius*math.sin(angle)))
    return points


def svg_path(commands):
    parts = []
    for command, *args in commands:
        if command in ("M", "L"):
            parts.append(f"{command}{args[0]} {args[1]}")
        else:
            cx, cy, radius, start, end = args
            angle = math.radians(end)
            x, y = round(cx+radius*math.cos(angle)), round(cy+radius*math.sin(angle))
            parts.append(f"A{radius} {radius} 0 0 {int(end>start)} {x} {y}")
    return " ".join(parts)


def mark(draw, monochrome=False, scale=SUPERSAMPLE):
    color = "#000000" if monochrome else MINT
    for commands in LETTERS:
        points = [(x*scale, y*scale) for x, y in path_points(commands)]
        draw.line(points, fill=color, width=STROKE*scale, joint="curve")
        half = STROKE*scale/2
        for x, y in points:
            draw.ellipse((x-half, y-half, x+half, y+half), fill=color)


def render(size):
    canvas = Image.new("RGBA", (1024*SUPERSAMPLE, 1024*SUPERSAMPLE))
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle(tuple(v*SUPERSAMPLE for v in TILE), radius=TILE_RADIUS*SUPERSAMPLE, fill=BACKGROUND)
    foreground = Image.new("RGBA", canvas.size)
    mark(ImageDraw.Draw(foreground))
    canvas.alpha_composite(foreground)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def svg():
    left, top, right, bottom = TILE
    shapes = [f'<rect x="{left}" y="{top}" width="{right-left}" height="{bottom-top}" rx="{TILE_RADIUS}" fill="{BACKGROUND}"/>']
    for commands in LETTERS:
        shapes.append(f'<path d="{svg_path(commands)}" fill="none" stroke="{MINT}" stroke-width="{STROKE}" stroke-linecap="round" stroke-linejoin="round"/>')
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">\n  ' + '\n  '.join(shapes) + '\n</svg>\n'


def build(assets):
    assets.mkdir(parents=True, exist_ok=True)
    (assets / "EdgeLink.svg").write_text(svg(), encoding="utf-8")
    for size in SIZES:
        render(size).save(assets / f"icon-{size}.png")
    iconset = assets / "EdgeLink.iconset"
    iconset.mkdir(exist_ok=True)
    for logical in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            suffix = "@2x" if scale == 2 else ""
            (iconset / f"icon_{logical}x{logical}{suffix}.png").write_bytes(
                (assets / f"icon-{logical*scale}.png").read_bytes())

    # PNG-backed ICO entries preserve our exact antialiased sizes.
    images = [(size, (assets / f"icon-{size}.png").read_bytes()) for size in SIZES if size <= 256]
    offset = 6 + 16 * len(images)
    entries, blobs = [], []
    for size, data in images:
        entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
        blobs.append(data)
        offset += len(data)
    (assets / "EdgeLink.ico").write_bytes(struct.pack("<HHH", 0, 1, len(images)) + b"".join(entries + blobs))

    # PNG-backed ICNS entries include the native 16 px icon as well as Retina
    # variants. Pillow's ICNS writer omits the 16 px representation.
    write_icns(assets)

    # A true alpha template for light/dark macOS menu bars, without the app tile.
    template = Image.new("RGBA", (1024*SUPERSAMPLE, 1024*SUPERSAMPLE))
    mark(ImageDraw.Draw(template), monochrome=True)
    template = template.crop(tuple(v*SUPERSAMPLE for v in (160, 280, 864, 744)))
    for scale in (1, 2):
        image = Image.new("RGBA", (23*scale, 18*scale))
        symbol = template.resize((21*scale, 14*scale), Image.Resampling.LANCZOS)
        image.alpha_composite(symbol, (scale, 2*scale))
        image.save(assets / f"status-{scale}x.png")
    print("Generated shared SVG, PNG sizes, ICO, ICNS, iconset and menu-bar templates")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent)
    build(parser.parse_args().output)
