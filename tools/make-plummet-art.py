"""
Placeholder card art for Lean and Mean Cards' Plummet.

Same sticker language as make-copy-art.py: thick cream outline over a black inner
ring, flat saturated fills, chunky shapes that survive the 40px card-bar sticker.
Rendered at 3x and downsampled with LANCZOS.

Full art: an orange blob spiked head-first into a cracked slab, speed lines
streaming up behind it. Mini: a fat down arrow slamming into the same slab, which
is what still reads at bar size.

  python make-plummet-art.py <out-dir>

Writes plummet.png / plummet_mini.png.
"""
import math
import sys
from PIL import Image, ImageDraw, ImageFilter

CREAM = (245, 239, 225, 255)
INK = (13, 15, 20, 255)

ORANGE = (246, 132, 34, 255)
ORANGE_DARK = (204, 88, 18, 255)
ORANGE_LIGHT = (255, 186, 104, 255)

RED = (232, 64, 52, 255)
RED_DARK = (170, 34, 30, 255)
RED_LIGHT = (255, 128, 104, 255)

YELLOW = (255, 210, 58, 255)
WHITE_HOT = (255, 248, 218, 255)

SLAB = (124, 120, 134, 255)
SLAB_DARK = (82, 78, 94, 255)
SLAB_LIGHT = (168, 164, 180, 255)

MOUTH = (120, 24, 30, 255)
TONGUE = (238, 92, 92, 255)

SS = 3  # supersample factor


def grow(src, radius, colour):
    """Dilate src's silhouette by radius and fill it with colour (hard edge, no glow)."""
    a = src.split()[3].point(lambda v: 255 if v > 110 else 0)
    step = 9  # grows by ~4px a pass
    for _ in range(max(1, round(radius / 4))):
        a = a.filter(ImageFilter.MaxFilter(step))
    a = a.filter(ImageFilter.GaussianBlur(1.2))
    out = Image.new("RGBA", src.size, colour)
    out.putalpha(a)
    return out


def sticker(base, art, cream_px, ink_px):
    base.alpha_composite(grow(art, cream_px + ink_px, CREAM))
    base.alpha_composite(grow(art, ink_px, INK))
    base.alpha_composite(art)


def star(cx, cy, r_out, r_in, points, spin=0.0, up=1.0, down=1.0, wide=1.0):
    """Star polygon. up/down scale the spikes above/below centre, wide scales x."""
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(spin) + math.pi * i / points
        dy = math.sin(a) * r
        pts.append((cx + math.cos(a) * r * wide, cy + dy * (down if dy > 0 else up)))
    return pts


def ink_line(d, pts, width):
    d.line(pts, fill=INK, width=width, joint="curve")
    for x, y in (pts[0], pts[-1]):
        d.ellipse([x - width / 2, y - width / 2, x + width / 2, y + width / 2], fill=INK)


def slab(d, x0, y0, x1, y1, impact_x):
    """Cracked floor block, impact point centred on its top edge."""
    h = y1 - y0
    d.rounded_rectangle([x0, y0, x1, y1], radius=int(h * 0.22), fill=SLAB)
    d.rounded_rectangle([x0, y0 + h * 0.62, x1, y1], radius=int(h * 0.22), fill=SLAB_DARK)
    d.rectangle([x0 + h * 0.22, y0 + h * 0.10, x1 - h * 0.22, y0 + h * 0.24], fill=SLAB_LIGHT)
    w = max(3, int(h * 0.07))
    # Long and shallow so they run out past the burst along the slab.
    for sx, drop, kink in ((-1, 0.70, 0.18), (-1, 0.35, -0.12), (1, 0.42, 0.14), (1, 0.78, -0.16)):
        ex, ey = impact_x + sx * (x1 - x0) * 0.40, y0 + drop * h
        mx, my = impact_x + sx * (x1 - x0) * 0.22, y0 + (drop * 0.5 + kink) * h
        ink_line(d, [(impact_x, y0 + h * 0.04), (mx, my), (ex, ey)], w)


def impact(d, cx, cy, r):
    """Burst that sprays sideways and up; the floor eats the downward half."""
    d.polygon(star(cx, cy, r, r * 0.55, 11, spin=-90, up=0.8, down=0.32, wide=1.25), fill=YELLOW)
    d.polygon(star(cx, cy, r * 0.62, r * 0.34, 11, spin=-74, up=0.8, down=0.32, wide=1.25), fill=WHITE_HOT)


def speed_lines(d, cx, top, bottom, spread, thickness, count=5, offsets=None):
    """Rounded vertical streaks, longest in the middle."""
    offsets = offsets or [(i / (count - 1)) * 2 - 1 for i in range(count)]  # -1..1
    for t in offsets:
        x = cx + t * spread
        length = (bottom - top) * (1.0 - 0.38 * abs(t))
        y1 = bottom - abs(t) * (bottom - top) * 0.08
        y0 = y1 - length
        d.rounded_rectangle([x - thickness / 2, y0, x + thickness / 2, y1],
                            radius=int(thickness / 2), fill=CREAM)


def blob(img, cx, cy, rx, ry):
    """The orange guy, squashed flat on impact, arms flung up by the fall."""
    d = ImageDraw.Draw(img)
    # arms first so the body overlaps their roots
    for sx in (-1, 1):
        arm = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ad = ImageDraw.Draw(arm)
        # A long capsule rooted at the body's side, swung up and out.
        ax, ay = cx + sx * rx * 0.86, cy - ry * 0.05
        aw, al = rx * 0.13, ry * 1.25
        ad.rounded_rectangle([ax - aw, ay - al, ax + aw, ay], radius=int(aw), fill=ORANGE_DARK)
        ad.ellipse([ax - aw * 1.45, ay - al - aw * 1.2, ax + aw * 1.45, ay - al + aw * 1.5], fill=ORANGE)
        img.alpha_composite(arm.rotate(-sx * 40, resample=Image.BICUBIC, center=(ax, ay)))

    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=ORANGE)

    shade = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    sd.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=ORANGE_DARK)
    sd.ellipse([cx - rx * 1.08, cy - ry * 1.18, cx + rx * 0.86, cy + ry * 0.70], fill=(0, 0, 0, 0))
    img.alpha_composite(shade)

    d.ellipse([cx - rx * 0.66, cy - ry * 0.74, cx - rx * 0.18, cy - ry * 0.38], fill=ORANGE_LIGHT)

    # screwed-shut eyes: > <
    w = max(4, int(ry * 0.10))
    ey = cy - ry * 0.05
    for sx in (-1, 1):
        ex = cx + sx * rx * 0.34
        a = rx * 0.15
        ink_line(d, [(ex - sx * a, ey - a * 0.8), (ex + sx * a * 0.4, ey), (ex - sx * a, ey + a * 0.8)], w)

    # yelling mouth
    my = cy + ry * 0.42
    d.ellipse([cx - rx * 0.24, my - ry * 0.22, cx + rx * 0.24, my + ry * 0.26], fill=INK)
    d.ellipse([cx - rx * 0.19, my - ry * 0.16, cx + rx * 0.19, my + ry * 0.20], fill=MOUTH)
    d.ellipse([cx - rx * 0.13, my + ry * 0.02, cx + rx * 0.13, my + ry * 0.20], fill=TONGUE)


def full_motif(w, h):
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(art)
    floor_y = h * 0.76
    cx = w * 0.5

    speed_lines(d, cx, h * 0.03, h * 0.40, h * 0.22, h * 0.036, offsets=[-1.0, -0.45, 0.0, 0.5, 1.0])
    slab(d, w * 0.13, floor_y, w * 0.87, h * 0.93, cx)
    impact(d, cx, floor_y, h * 0.30)
    blob(art, cx, h * 0.55, h * 0.27, h * 0.20)
    return art


def down_arrow(d, cx, top, tip, width, fill, dark, light):
    shaft_w = width * 0.46
    head_top = tip - width * 0.82
    d.rounded_rectangle([cx - shaft_w / 2, top, cx + shaft_w / 2, head_top + width * 0.1],
                        radius=int(shaft_w * 0.3), fill=fill)
    d.polygon([(cx - width / 2, head_top), (cx + width / 2, head_top), (cx, tip)], fill=fill)
    # right-hand shade and left-hand gloss
    d.polygon([(cx + shaft_w * 0.08, head_top), (cx + width / 2, head_top), (cx, tip)], fill=dark)
    d.rectangle([cx - shaft_w * 0.32, top + shaft_w * 0.25, cx - shaft_w * 0.12, head_top - shaft_w * 0.1],
                fill=light)


def mini_motif(w, h):
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(art)
    floor_y = h * 0.74
    cx = w * 0.5

    # Clear of the arrow so none peek out behind its shaft.
    speed_lines(d, cx, h * 0.10, h * 0.42, w * 0.36, w * 0.05, offsets=[-1.0, -0.72, 0.72, 1.0])
    slab(d, w * 0.10, floor_y, w * 0.90, h * 0.90, cx)
    impact(d, cx, floor_y, w * 0.26)
    down_arrow(d, cx, h * 0.10, floor_y + h * 0.02, w * 0.44, RED, RED_DARK, RED_LIGHT)
    return art


def render(motif_fn, out_path, width, height, cream, ink):
    w, h = width * SS, height * SS
    art = motif_fn(w, h)
    base = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sticker(base, art, cream * SS, ink * SS)
    base.resize((width, height), Image.LANCZOS).save(out_path)
    print("wrote", out_path)


def main():
    out = sys.argv[1].rstrip("/\\")
    render(full_motif, out + "/plummet.png", 1100, 865, 13, 6)
    render(mini_motif, out + "/plummet_mini.png", 512, 512, 9, 4)


if __name__ == "__main__":
    main()
