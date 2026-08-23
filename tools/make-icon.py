"""
Thunderstore mod icons for ProMLGStats and LeanAndMeanCards.

Matches the MulliganMadness icon's visual language so the three read as a family:
dark rounded-square ground, one saturated accent colour, thick cream sticker
outlines with a black inner ring, chunky stacked uppercase text, and an angry
ROUNDS blob in a bottom corner. Each mod gets its own accent and hero motif.

Rendered at 4x and downsampled with LANCZOS so the outlines stay smooth at 256px.
"""
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter

S = 256
SS = 4                      # supersample factor
W = S * SS                  # working resolution

CREAM = (245, 239, 225, 255)
INK = (13, 15, 20, 255)
BG_DARK = (23, 26, 34, 255)
BG_DARKER = (15, 17, 23, 255)

FONT_BLACK = "C:/Windows/Fonts/ariblk.ttf"


def layer():
    return Image.new("RGBA", (W, W), (0, 0, 0, 0))


def grow(src, radius, colour):
    """Return `src`'s silhouette dilated by `radius`, filled with `colour`.

    Real dilation (repeated MaxFilter) rather than blur-and-threshold. Blurring
    leaves a soft falloff that reads as a glow; the ROUNDS sticker look needs a
    hard edge. Aliasing at 4x disappears in the LANCZOS downsample.
    """
    a = src.split()[3].point(lambda v: 255 if v > 110 else 0)
    step = 9                                    # MaxFilter size -> grows by 4px
    for _ in range(max(1, round(radius / 4))):
        a = a.filter(ImageFilter.MaxFilter(step))
    a = a.filter(ImageFilter.GaussianBlur(1.2))  # knock the hardest jaggies off
    out = Image.new("RGBA", src.size, colour)
    out.putalpha(a)
    return out


def sticker(base, art, cream_px=26, ink_px=11):
    """Composite `art` onto `base` with a black inner ring and cream outer border."""
    base.alpha_composite(grow(art, cream_px + ink_px, CREAM))
    base.alpha_composite(grow(art, ink_px, INK))
    base.alpha_composite(art)


def background(accent_dim):
    """Rounded-square ground with faint diagonal stripes and a vignette."""
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = int(W * 0.175)
    d.rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=BG_DARK)

    stripes = layer()
    ds = ImageDraw.Draw(stripes)
    step = int(W * 0.105)
    for i in range(-W, W * 2, step):
        ds.line([(i, 0), (i + W, W)], fill=accent_dim, width=int(W * 0.016))
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=r, fill=255)
    stripes.putalpha(Image.composite(stripes.split()[3], Image.new("L", (W, W), 0), mask))
    img.alpha_composite(stripes)

    # vignette: darken the corners so the centre motif pops
    vig = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    dv = ImageDraw.Draw(vig)
    for i in range(14):
        t = i / 14
        dv.rounded_rectangle(
            [int(-W * 0.12 + W * 0.10 * t)] * 2 + [int(W * 1.12 - W * 0.10 * t)] * 2,
            radius=r, outline=(0, 0, 0, 15), width=int(W * 0.035))
    vig.putalpha(Image.composite(vig.split()[3], Image.new("L", (W, W), 0), mask))
    img.alpha_composite(vig)
    return img


def blob(size, body, angry=True):
    """The ROUNDS blob: round body, stubby feet, angular scowl.

    The anger lives in the eye shape itself — slanted wedges — not in separate
    brow strokes, which merge into a black smear at icon scale.
    """
    lay = layer()
    d = ImageDraw.Draw(lay)
    cx = cy = size // 2
    rr = int(size * 0.40)

    fw, fh = int(size * 0.19), int(size * 0.12)
    for fx in (cx - int(size * 0.21), cx + int(size * 0.21) - fw):
        d.rounded_rectangle([fx, cy + rr - int(size * 0.05), fx + fw, cy + rr + fh],
                            radius=fh // 2, fill=body)
    d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=body)

    ey = cy - int(size * 0.06)
    ew, eh = int(size * 0.20), int(size * 0.20)
    for sx in (-1, 1):
        ex = cx + sx * int(size * 0.16)
        outer_top = (ex - sx * ew * 0.5, ey - eh * 0.30)
        inner_top = (ex + sx * ew * 0.5, ey - eh * 0.72)
        inner_bot = (ex + sx * ew * 0.42, ey + eh * 0.20)
        outer_bot = (ex - sx * ew * 0.42, ey + eh * 0.48)
        d.polygon([outer_top, inner_top, inner_bot, outer_bot], fill=INK)

    mw = int(size * 0.26)
    my = cy + int(size * 0.20)
    zig = []
    for i in range(5):
        zig.append((cx - mw // 2 + mw * i / 4, my + (0 if i % 2 == 0 else size * 0.055)))
    for i in range(4, -1, -1):
        zig.append((cx - mw // 2 + mw * i / 4,
                    my + size * 0.035 + (0 if i % 2 == 0 else size * 0.055)))
    d.polygon(zig, fill=INK)
    return lay


def fitted_font(text, target_w, max_h):
    size = 10
    f = ImageFont.truetype(FONT_BLACK, size)
    while size < 600:
        nxt = ImageFont.truetype(FONT_BLACK, size + 4)
        box = nxt.getbbox(text)
        if box[2] - box[0] > target_w or box[3] - box[1] > max_h:
            break
        size += 4
        f = nxt
    return f


def text_layer(lines, box_w, line_h, tracking=0.0):
    """Stacked uppercase lines, each scaled to fill box_w."""
    lay = layer()
    d = ImageDraw.Draw(lay)
    y = 0
    for line in lines:
        f = fitted_font(line, box_w, line_h)
        bb = f.getbbox(line)
        x = (box_w - (bb[2] - bb[0])) // 2 - bb[0]
        d.text((x, y - bb[1]), line, font=f, fill=CREAM)
        y += line_h
    return lay


def card(w, h, fill, angle, pos, canvas):
    """A rounded playing-card shape, rotated and pasted at pos (its centre)."""
    pad = int(max(w, h) * 0.5)
    c = Image.new("RGBA", (w + pad * 2, h + pad * 2), (0, 0, 0, 0))
    ImageDraw.Draw(c).rounded_rectangle(
        [pad, pad, pad + w, pad + h], radius=int(min(w, h) * 0.13), fill=fill)
    c = c.rotate(angle, resample=Image.BICUBIC, expand=True)
    canvas.alpha_composite(c, (pos[0] - c.width // 2, pos[1] - c.height // 2))


# --------------------------------------------------------------------------
def pro_mlg_stats():
    """Teal. Hero motif: a rising bar chart with an upward arrow."""
    ACCENT = (62, 216, 196, 255)
    ACCENT_DEEP = (30, 150, 140, 255)
    ACCENT_DIM = (62, 216, 196, 11)

    img = background(ACCENT_DIM)

    bars = layer()
    db = ImageDraw.Draw(bars)
    n = 4
    bw = int(W * 0.105)
    gap = int(W * 0.043)
    total = n * bw + (n - 1) * gap
    x0 = (W - total) // 2
    base_y = int(W * 0.645)
    for i in range(n):
        h = int(W * (0.135 + 0.088 * i))
        x = x0 + i * (bw + gap)
        shade = ACCENT if i % 2 == 0 else ACCENT_DEEP
        db.rounded_rectangle([x, base_y - h, x + bw, base_y],
                             radius=int(bw * 0.26), fill=shade)
    sticker(img, bars, cream_px=24, ink_px=10)

    arrow = layer()
    da = ImageDraw.Draw(arrow)
    ax, ay = int(W * 0.735), int(W * 0.300)
    s = int(W * 0.105)
    da.polygon([(ax, ay - s), (ax + s, ay), (ax + s * 0.42, ay),
                (ax + s * 0.42, ay + s * 0.95), (ax - s * 0.42, ay + s * 0.95),
                (ax - s * 0.42, ay), (ax - s, ay)], fill=ACCENT)
    # Accent, not cream: a second cream mass fought the "STATS" wordmark for
    # attention and flattened the hierarchy.
    sticker(img, arrow, cream_px=20, ink_px=9)

    txt = text_layer(["STATS"], int(W * 0.62), int(W * 0.215))
    holder = layer()
    holder.alpha_composite(txt, (int(W * 0.19), int(W * 0.685)))
    sticker(img, holder, cream_px=0, ink_px=13)

    b = blob(int(W * 0.30), ACCENT)
    sticker(img, _place(b, int(W * 0.055), int(W * 0.055)), cream_px=17, ink_px=8)
    return img


def lean_and_mean_cards():
    """Orange. Hero motif: a fan of cards with an angry blob on the face card."""
    ACCENT = (255, 138, 61, 255)
    ACCENT_DEEP = (214, 96, 30, 255)
    ACCENT_DIM = (255, 138, 61, 10)

    img = background(ACCENT_DIM)

    back = layer()
    card(int(W * 0.36), int(W * 0.50), ACCENT_DEEP, 20, (int(W * 0.34), int(W * 0.47)), back)
    card(int(W * 0.36), int(W * 0.50), ACCENT_DEEP, 9, (int(W * 0.42), int(W * 0.45)), back)
    sticker(img, back, cream_px=22, ink_px=9)

    front = layer()
    card(int(W * 0.40), int(W * 0.55), ACCENT, -8, (int(W * 0.56), int(W * 0.44)), front)
    sticker(img, front, cream_px=24, ink_px=10)

    # No face on the card: detached from a body the eye wedges read as two stray
    # marks above the wordmark. The corner blob carries the character instead.
    txt = text_layer(["LEAN", "MEAN"], int(W * 0.345), int(W * 0.185))
    holder = layer()
    holder.alpha_composite(txt, (int(W * 0.385), int(W * 0.310)))
    holder = holder.rotate(-8, resample=Image.BICUBIC, center=(int(W * 0.56), int(W * 0.44)))
    sticker(img, holder, cream_px=0, ink_px=11)

    b = blob(int(W * 0.30), ACCENT)
    sticker(img, _place(b, int(W * 0.045), int(W * 0.615)), cream_px=17, ink_px=8)
    return img


def _place(small, x, y):
    lay = layer()
    lay.alpha_composite(small, (x, y))
    return lay


def save(img, path):
    """256x256 opaque RGB, matching the MulliganMadness icon.

    That one paints its rounded corners a near-black navy rather than leaving
    them transparent. Keeping the same treatment means the three siblings look
    consistent wherever Thunderstore renders them, including on light grounds.
    """
    out = img.resize((S, S), Image.LANCZOS)
    flat = Image.new("RGBA", (S, S), (1, 14, 24, 255))
    flat.alpha_composite(out)
    flat.convert("RGB").save(path, "PNG")
    print(f"wrote {path}  {S}x{S} RGB")


if __name__ == "__main__":
    save(pro_mlg_stats(), "promlgstats-icon.png")
    save(lean_and_mean_cards(), "leanandmeancards-icon.png")
