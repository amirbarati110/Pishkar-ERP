"""
Draws the Pishkar app icon and the MSIX tile images from the same geometry the
PishkarLogo control uses (src/ERP.Desktop/DesignSystem/PishkarLogo.xaml, which
came from the user's Figma file «00 — Cover & Brand», node 15:3). Keeping one
source for the shape means the taskbar icon and the logo on screen can never
drift apart.

The icons that shipped before this were the default WinUI template images (a
grey crossed box). Run from the repository root:
    python design-system/retail-erp/make-app-icon.py
"""

from __future__ import annotations

import os

from PIL import Image, ImageDraw

NAVY = (15, 23, 42, 255)      # #0F172A — Figma «سرمه‌ای برند»
WHITE = (255, 255, 255, 255)
AMBER = (217, 119, 6, 255)    # #D97706 — emphasis only

# The symbol is authored on a 200×200 canvas, same as the XAML <Canvas>.
CANVAS = 200.0
SUPERSAMPLE = 8


def cubic(p0, p1, p2, p3, steps=48):
    """Flattens one cubic Bézier into a polyline."""
    out = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        x = u * u * u * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t * t * t * p3[0]
        y = u * u * u * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t * t * t * p3[1]
        out.append((x, y))
    return out


def bowl_points():
    """M164 40 H82 C46 40 28 57 28 88 C28 119 48 136 82 136 H156 C169 136 176 128 176 115 V82"""
    points = [(164.0, 40.0), (82.0, 40.0)]
    points += cubic((82, 40), (46, 40), (28, 57), (28, 88))[1:]
    points += cubic((28, 88), (28, 119), (48, 136), (82, 136))[1:]
    points.append((156.0, 136.0))
    points += cubic((156, 136), (169, 136), (176, 128), (176, 115))[1:]
    points.append((176.0, 82.0))
    return points


CHECK_POINTS = [(116.0, 88.0), (133.0, 105.0), (168.0, 66.0)]
DOTS = [(64.0, 157.0), (92.0, 157.0), (120.0, 157.0)]
DOT_SIZE = 16.0


def stroke(draw, points, colour, width, scale, offset):
    """A round-capped, round-joined stroke — PIL has no such pen, so joints and caps are discs."""
    scaled = [(offset[0] + x * scale, offset[1] + y * scale) for x, y in points]
    thickness = max(1, round(width * scale))
    draw.line(scaled, fill=colour, width=thickness, joint="curve")
    radius = thickness / 2
    for x, y in (scaled[0], scaled[-1]):
        draw.ellipse([x - radius, y - radius, x + radius, y + radius], fill=colour)


def draw_symbol(draw, box, scale, offset):
    stroke(draw, bowl_points(), WHITE, 16, scale, offset)
    stroke(draw, CHECK_POINTS, AMBER, 13, scale, offset)
    for x, y in DOTS:
        left = offset[0] + x * scale
        top = offset[1] + y * scale
        size = DOT_SIZE * scale
        draw.ellipse([left, top, left + size, top + size], fill=WHITE)


def tile(width, height, tile_background=True):
    """
    The square tile: navy plate, corner radius a quarter of the side (Figma
    40 → 10), symbol inset the same 4/36 the XAML Viewbox margin gives it.
    """
    big_w, big_h = width * SUPERSAMPLE, height * SUPERSAMPLE
    image = Image.new("RGBA", (big_w, big_h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    side = min(big_w, big_h)
    plate = [(big_w - side) / 2, (big_h - side) / 2, (big_w + side) / 2, (big_h + side) / 2]
    if tile_background:
        draw.rounded_rectangle(plate, radius=side * 0.25, fill=NAVY)

    inset = side * (4 / 36)
    inner = side - 2 * inset
    scale = inner / CANVAS
    offset = (plate[0] + inset, plate[1] + inset)
    draw_symbol(draw, plate, scale, offset)

    return image.resize((width, height), Image.LANCZOS)


def main():
    assets = os.path.join("src", "ERP.Desktop", "Assets")

    ico_sizes = [256, 128, 64, 48, 32, 16]
    frames = [tile(size, size) for size in ico_sizes]
    frames[0].save(
        os.path.join(assets, "AppIcon.ico"),
        format="ICO",
        sizes=[(size, size) for size in ico_sizes],
        append_images=frames[1:],
    )

    for name, (width, height) in {
        "LockScreenLogo.scale-200.png": (48, 48),
        "SplashScreen.scale-200.png": (1240, 600),
        "Square150x150Logo.scale-200.png": (300, 300),
        "Square44x44Logo.scale-200.png": (88, 88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 24),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": (48, 48),
        "StoreLogo.png": (50, 50),
        "Wide310x150Logo.scale-200.png": (620, 300),
    }.items():
        tile(width, height).save(os.path.join(assets, name))
        print("wrote", name, width, "x", height)

    print("wrote AppIcon.ico", ico_sizes)


if __name__ == "__main__":
    main()
