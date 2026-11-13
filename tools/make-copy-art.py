"""
Placeholder card art for Lean and Mean Cards' Duplicate and Clone.

Same visual language as tools/make-icon.py and the painted card art: thick cream
sticker outline with a black inner ring, flat saturated fills, chunky shapes that
survive being shrunk to a 40px bar sticker. Rendered at 3x and downsampled with
LANCZOS so the outlines stay smooth.

Transparent background on purpose: the card face and the bar are both dark, and a
baked-in black rectangle shows its corners on the bar.

  python make-copy-art.py <out-dir>

Writes duplicate.png / duplicate_mini.png / clone.png / clone_mini.png.
"""
import math
import sys
from PIL import Image, ImageDraw, ImageFilter

CREAM = (245, 239, 225, 255)
INK = (13, 15, 20, 255)

BLUE = (88, 140, 236, 255)
BLUE_DARK = (54, 96, 184, 255)
BLUE_LIGHT = (142, 186, 255, 255)

GOLD = (240, 192, 72, 255)
GOLD_DARK = (196, 142, 32, 255)
GOLD_LIGHT = (255, 226, 140, 255)

SS = 3  # supersample factor


def grow(src, radius, colour):
    """Dilate src's silhouette by radius and fill it with colour.

    Repeated MaxFilter, not blur-and-threshold: a blurred edge reads as a glow,
    and this look needs a hard one.
    """
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


def pip_diamond(d, cx, cy, w, h, fill, outline=None, width=0):
    d.polygon([(cx, cy - h / 2), (cx + w / 2, cy), (cx, cy + h / 2), (cx - w / 2, cy)],
              fill=fill, outline=outline, width=width)


def playing_card(w, h, body, body_dark, body_light, pip=True):
    """One card face: rounded rect, inner frame, centre pip, corner gloss."""
    pad = max(6, int(w * 0.02))
    img = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = pad, pad, pad + w, pad + h
    r = int(w * 0.11)

    d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=body)
    # inner frame
    inset = int(w * 0.085)
    d.rounded_rectangle([x0 + inset, y0 + inset, x1 - inset, y1 - inset],
                        radius=int(r * 0.7), outline=body_light, width=max(2, int(w * 0.022)))
    # top-left gloss wedge
    gloss = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(gloss).polygon(
        [(x0 + inset * 1.6, y0 + inset * 1.5),
         (x0 + w * 0.52, y0 + inset * 1.5),
         (x0 + inset * 1.6, y0 + h * 0.58)],
        fill=(255, 255, 255, 38))
    img.alpha_composite(gloss)

    if pip:
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        pip_diamond(d, cx, cy, w * 0.40, h * 0.40, body_light)
        pip_diamond(d, cx, cy, w * 0.40, h * 0.40, None, body_dark, max(2, int(w * 0.018)))
        # corner pips
        for sx, sy in ((-1, -1), (1, 1)):
            pip_diamond(d, cx + sx * w * 0.30, cy + sy * h * 0.33,
                        w * 0.12, h * 0.12, body_light)

    return img


def rotated(img, angle):
    return img.rotate(angle, resample=Image.BICUBIC, expand=True)


def paste_centre(base, art, cx, cy):
    base.alpha_composite(art, (int(cx - art.width / 2), int(cy - art.height / 2)))


def duplicate_motif(w, h):
    """Two identical cards, the copy sliding out from behind the original."""
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    cw, ch = int(min(w, h) * 0.52), int(min(w, h) * 0.74)

    back = rotated(playing_card(cw, ch, BLUE_DARK, BLUE_DARK, BLUE), -13)
    front = rotated(playing_card(cw, ch, BLUE, BLUE_DARK, BLUE_LIGHT), 7)

    paste_centre(art, back, w * 0.40, h * 0.46)
    paste_centre(art, front, w * 0.60, h * 0.55)
    return art


def clone_motif(w, h):
    """A fanned hand of five identical cards: the same card, over and over.

    Fanned around a pivot below the frame rather than stacked in a line — a
    straight cascade under a loop arrow read as a handbag at sticker size.
    """
    art = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    # Wider than tall frames get a bigger fan so the full art is not a small
    # sticker floating in margins; the cap keeps it inside a square mini.
    m = min(w, h * 1.22)
    cw, ch = int(m * 0.27), int(m * 0.40)

    pivot_x, pivot_y = w * 0.5, h * 0.80
    radius = m * 0.34

    # Back to front, so the brightest card sits on top of the fan.
    order = [-36, 36, -18, 18, 0]
    for angle in order:
        front = abs(angle) < 1
        mid = abs(angle) < 19
        body = GOLD if front or mid else GOLD_DARK
        light = GOLD_LIGHT if front or mid else GOLD
        card = rotated(playing_card(cw, ch, body, GOLD_DARK, light), -angle)
        a = math.radians(angle)
        paste_centre(art,
                     card,
                     pivot_x + math.sin(a) * radius,
                     pivot_y - math.cos(a) * radius)

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
    render(duplicate_motif, out + "/duplicate.png", 1100, 865, 13, 6)
    render(duplicate_motif, out + "/duplicate_mini.png", 512, 512, 9, 4)
    render(clone_motif, out + "/clone.png", 1100, 865, 13, 6)
    render(clone_motif, out + "/clone_mini.png", 512, 512, 9, 4)


if __name__ == "__main__":
    main()
