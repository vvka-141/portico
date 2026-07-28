#!/usr/bin/env python3
"""Generate every Portico brand asset from one geometry definition (POR-135).

    python eng/brand/generate.py

Writes, all into this directory:

    portico.svg                  vector export — resolution-independent, regenerated not edited
    portico-icon-128.png         the NuGet package icon (NuGet recommends 128x128)
    portico-512.png              repository avatar / general-purpose square
    portico-social-1280x640.png  the GitHub social preview

The mark is a portico — two columns and an entablature — framing a shell prompt. The
metaphor is the framework's: a CLI is the front of the building, and what it promises at
the door is what you get inside.

SHAPES below is the master. Everything else, the SVG included, is derived from it, so a
colour change is a one-line edit and nothing in the repository is a PNG whose source has
been lost. Rasterising with Pillow rather than a real SVG renderer keeps the dependency
list to one package that is already common.

The assets are deterministically derived from that one geometry definition within a
consistent toolchain — which is not the same as byte-identical across machines. The social
card picks a system font, and that differs between Windows, Linux and macOS; PNG
compression can also shift between Pillow versions. Pinning Pillow and vendoring a licensed
font would close both gaps, and is not worth the complexity for brand assets. If a rebuild
ever needs to match an existing file exactly, regenerate on the machine that produced it.
"""

import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:  # pragma: no cover - operator-facing
    sys.exit("error: this script needs Pillow.  pip install Pillow")

HERE = os.path.dirname(os.path.abspath(__file__))

# --- Palette -----------------------------------------------------------------------

NAVY = "#091635"   # background
STONE = "#F5ECD6"  # the portico itself
CYAN = "#0FCBF1"   # the prompt
AMBER = "#FEB705"  # the cursor

# --- Geometry ----------------------------------------------------------------------
#
# Everything is expressed as a fraction of the square canvas, so one definition serves
# 128px, 512px and the social card's inset mark alike. Polygons are listed clockwise from
# the top-left. The chamfers on the entablature and the flare on the capitals and bases
# are what stop the shape reading as a plain table.

ARCHITRAVE_CHAMFER = 0.011

SHAPES = [
    # The entablature: an octagon, chamfered at all four corners.
    ("architrave", STONE, [
        (0.1611 + ARCHITRAVE_CHAMFER, 0.1667),
        (0.8381 - ARCHITRAVE_CHAMFER, 0.1667),
        (0.8381, 0.1667 + ARCHITRAVE_CHAMFER),
        (0.8381, 0.2608 - ARCHITRAVE_CHAMFER),
        (0.8381 - ARCHITRAVE_CHAMFER, 0.2608),
        (0.1611 + ARCHITRAVE_CHAMFER, 0.2608),
        (0.1611, 0.2608 - ARCHITRAVE_CHAMFER),
        (0.1611, 0.1667 + ARCHITRAVE_CHAMFER),
    ]),

    # Capitals — wider at the top, where they meet the entablature.
    ("capital-left", STONE, [
        (0.1970, 0.2783), (0.3445, 0.2783), (0.3230, 0.3094), (0.2185, 0.3094),
    ]),
    ("capital-right", STONE, [
        (0.6651, 0.2783), (0.8046, 0.2783), (0.7855, 0.3094), (0.6866, 0.3094),
    ]),

    # Shafts.
    ("shaft-left", STONE, [
        (0.2185, 0.3270), (0.3230, 0.3270), (0.3230, 0.6994), (0.2185, 0.6994),
    ]),
    ("shaft-right", STONE, [
        (0.6866, 0.3270), (0.7855, 0.3270), (0.7855, 0.6994), (0.6866, 0.6994),
    ]),

    # Bases — the capitals inverted, flaring outward as they reach the stylobate.
    ("base-left", STONE, [
        (0.2185, 0.7169), (0.3230, 0.7169), (0.3453, 0.7448), (0.1938, 0.7448),
    ]),
    ("base-right", STONE, [
        (0.6866, 0.7169), (0.7855, 0.7169), (0.8086, 0.7448), (0.6651, 0.7448),
    ]),

    # The stylobate, stepped: a plinth under each column, then one slab across.
    ("stylobate", STONE, [
        (0.1794, 0.7624), (0.3668, 0.7624), (0.3668, 0.7900), (0.6451, 0.7900),
        (0.6451, 0.7624), (0.8238, 0.7624), (0.8238, 0.8325), (0.1794, 0.8325),
    ]),
]

# The prompt, drawn as a stroked polyline so the two arms meet cleanly at the apex.
CHEVRON = [(0.3900, 0.4130), (0.5250, 0.5287), (0.3900, 0.6444)]
CHEVRON_WIDTH = 0.047

# The cursor, sitting one space after the prompt on the same baseline.
CURSOR = (0.5614, 0.5829, 0.6244, 0.6547)


def svg() -> str:
    """The vector export, on a 512-unit viewBox. Regenerated from SHAPES, so an edit here is
    overwritten on the next run — change the geometry instead."""
    u = 512.0

    def pts(points):
        return " ".join(f"{x * u:.2f},{y * u:.2f}" for x, y in points)

    body = [
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512"'
        ' role="img" aria-label="Portico">',
        "  <title>Portico</title>",
        f'  <rect width="512" height="512" fill="{NAVY}"/>',
    ]
    for name, fill, points in SHAPES:
        body.append(f'  <polygon id="{name}" fill="{fill}" points="{pts(points)}"/>')

    body.append(
        f'  <polyline id="prompt" fill="none" stroke="{CYAN}"'
        f' stroke-width="{CHEVRON_WIDTH * u:.2f}" stroke-linecap="butt"'
        f' stroke-linejoin="miter" points="{pts(CHEVRON)}"/>'
    )
    x0, y0, x1, y1 = CURSOR
    body.append(
        f'  <rect id="cursor" fill="{AMBER}" x="{x0 * u:.2f}" y="{y0 * u:.2f}"'
        f' width="{(x1 - x0) * u:.2f}" height="{(y1 - y0) * u:.2f}"/>'
    )
    body.append("</svg>")
    return "\n".join(body) + "\n"


def render_mark(size: int, supersample: int = 8, background=NAVY) -> Image.Image:
    """The square mark at `size` px. Supersampled, because every edge here is a diagonal
    or a chamfer and Pillow's polygon fill has no anti-aliasing of its own."""
    s = size * supersample
    img = Image.new("RGB", (s, s), background)
    draw = ImageDraw.Draw(img)

    for _name, fill, points in SHAPES:
        draw.polygon([(x * s, y * s) for x, y in points], fill=fill)

    draw.line(
        [(x * s, y * s) for x, y in CHEVRON],
        fill=CYAN,
        width=int(round(CHEVRON_WIDTH * s)),
        joint="curve",
    )

    x0, y0, x1, y1 = CURSOR
    draw.rectangle([x0 * s, y0 * s, x1 * s, y1 * s], fill=AMBER)

    return img.resize((size, size), Image.LANCZOS)


def load_font(size: int, bold: bool = True) -> ImageFont.FreeTypeFont:
    """A system font, tried in order. The social card is the only asset with type on it;
    if none of these is present, say so rather than silently falling back to a bitmap
    font that would look like a mistake."""
    candidates = (
        ["seguisb.ttf", "segoeuib.ttf", "arialbd.ttf", "calibrib.ttf", "DejaVuSans-Bold.ttf"]
        if bold
        else ["segoeui.ttf", "arial.ttf", "calibri.ttf", "DejaVuSans.ttf"]
    )
    roots = ["C:/Windows/Fonts", "/usr/share/fonts/truetype/dejavu", "/Library/Fonts"]
    for root in roots:
        for name in candidates:
            path = os.path.join(root, name)
            if os.path.exists(path):
                return ImageFont.truetype(path, size)
    raise SystemExit(
        "error: no usable font found for the social card. Tried "
        + ", ".join(candidates)
        + " under "
        + ", ".join(roots)
    )


def render_social(width: int = 1280, height: int = 640) -> Image.Image:
    """The GitHub social preview: the mark on the left, the name and the one-line claim on
    the right. GitHub crops toward the centre on some surfaces, so nothing load-bearing
    goes near an edge."""
    img = Image.new("RGB", (width, height), NAVY)

    mark_size = 340
    mark = render_mark(mark_size)
    mark_x, mark_y = 150, (height - mark_size) // 2
    img.paste(mark, (mark_x, mark_y))

    draw = ImageDraw.Draw(img)
    text_x = mark_x + mark_size + 90

    name_font = load_font(132, bold=True)
    tag_font = load_font(44, bold=False)

    # Optically centre the pair against the mark rather than the canvas.
    name_box = draw.textbbox((0, 0), "Portico", font=name_font)
    tag_box = draw.textbbox((0, 0), "Executable CLI contracts for .NET", font=tag_font)
    gap = 34
    block_height = (name_box[3] - name_box[1]) + gap + (tag_box[3] - tag_box[1])
    top = mark_y + (mark_size - block_height) // 2

    draw.text((text_x, top - name_box[1]), "Portico", font=name_font, fill=STONE)
    draw.text(
        (text_x, top + (name_box[3] - name_box[1]) + gap - tag_box[1]),
        "Executable CLI contracts for .NET",
        font=tag_font,
        fill=CYAN,
    )
    return img


def write(path: str, image: Image.Image) -> None:
    # optimize=True keeps the package icon far under NuGet's 1 MB ceiling and makes the
    # output stable between runs.
    image.save(path, "PNG", optimize=True)
    print(f"  {os.path.basename(path):<28} {image.size[0]}x{image.size[1]}"
          f"  {os.path.getsize(path) / 1024:.0f} KB")


def main() -> None:
    print("Portico brand assets")

    svg_path = os.path.join(HERE, "portico.svg")
    with open(svg_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(svg())
    print(f"  {'portico.svg':<28} vector"
          f"  {os.path.getsize(svg_path) / 1024:.0f} KB")

    write(os.path.join(HERE, "portico-icon-128.png"), render_mark(128))
    write(os.path.join(HERE, "portico-512.png"), render_mark(512))
    write(os.path.join(HERE, "portico-social-1280x640.png"), render_social())


if __name__ == "__main__":
    main()
