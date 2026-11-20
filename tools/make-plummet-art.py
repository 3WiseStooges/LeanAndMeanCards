"""
Card art for Lean and Mean Cards' Plummet, drawn to sit with the painted cards.

The painted set (Dynamite, Yeet Cannon, Shove, Thief...) shares one look: a black
ground, heavy ink round every shape (so overlapping parts draw their own inner
lines), a soft light-to-dark gradient with a hard cel shadow on the side away from
the light, a white gloss spot, tapered cream speed shards, and one thick cream
sticker border round the whole silhouette.

This builds that look out of masks. Every part is a PIL mask; its ink is a
distance-transform dilation of the mask, its fill a gradient along the light
direction, and its cel shadow is whatever is left after subtracting a copy of the
mask nudged toward the light. Rendered at 3x and downsampled with LANCZOS.

Full art: the orange guy spiked into a stone slab, burst and rubble spraying out
of the crater, speed shards streaming above him. Black ground, like the painted
full arts. Mini: a glossy red down arrow slammed into the same slab, which is what
still reads at card-bar size. Transparent, like the painted minis.

  python make-plummet-art.py <out-dir>

Writes plummet.png / plummet_mini.png.
"""
import math
import random
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

SS = 3  # supersample factor


def rgb(*c):
    return np.array(c, np.float32) / 255.0


INK = rgb(14, 12, 18)
CREAM = rgb(247, 238, 218)
CREAM_DARK = rgb(214, 198, 170)

ORANGE = rgb(247, 138, 28)
ORANGE_LIGHT = rgb(255, 184, 82)
ORANGE_DARK = rgb(204, 82, 12)
ORANGE_GLOSS = rgb(255, 232, 186)

BURST = rgb(255, 150, 26)
BURST_DARK = rgb(232, 96, 14)
YELLOW = rgb(255, 214, 54)
YELLOW_LIGHT = rgb(255, 240, 140)
YELLOW_DARK = rgb(236, 150, 16)
WHITE_HOT = rgb(255, 250, 228)

STONE = rgb(118, 114, 138)
STONE_LIGHT = rgb(176, 172, 196)
STONE_DARK = rgb(74, 70, 94)
STONE_DEEP = rgb(40, 36, 52)

RED = rgb(228, 44, 40)
RED_LIGHT = rgb(255, 120, 98)
RED_DARK = rgb(150, 20, 24)
RED_GLOSS = rgb(255, 200, 186)

MOUTH = rgb(92, 14, 24)
TONGUE = rgb(234, 80, 82)
SWEAT = rgb(188, 230, 255)
SWEAT_DARK = rgb(110, 170, 220)

# Up and to the left, in image space (y grows down).
LIGHT = (-0.5, -0.866)


# ---------------------------------------------------------------- geometry

def ellipse_pts(cx, cy, rx, ry, angle=0.0, n=96):
    a = math.radians(angle)
    ca, sa = math.cos(a), math.sin(a)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = math.cos(t) * rx, math.sin(t) * ry
        pts.append((cx + x * ca - y * sa, cy + x * sa + y * ca))
    return pts


def star_pts(cx, cy, r_out, r_in, points, spin=0.0):
    pts = []
    for i in range(points * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(spin - 90) + math.pi * i / points
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def burst_pts(cx, cy, rx, ry_up, ry_down, points, seed=0, spin=0.0):
    """Jagged comic burst. Spikes above centre reach ry_up, below it ry_down."""
    rnd = random.Random(seed)
    pts = []
    for i in range(points * 2):
        a = math.radians(spin) + math.pi * i / points
        r = rnd.uniform(0.72, 1.0) if i % 2 == 0 else rnd.uniform(0.40, 0.52)
        dx, dy = math.cos(a) * r, math.sin(a) * r
        pts.append((cx + dx * rx, cy + dy * (ry_down if dy > 0 else ry_up)))
    return pts


def rock_pts(cx, cy, r, seed, spin=0.0):
    rnd = random.Random(seed)
    n = rnd.choice((5, 6, 7))
    pts = []
    for i in range(n):
        a = math.radians(spin) + 2 * math.pi * (i + rnd.uniform(-0.18, 0.18)) / n
        rr = r * rnd.uniform(0.72, 1.08)
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    return pts


def shard_pts(x, top, bottom, width):
    """Long kite pointing up: needle tip at the top, blunt end at the bottom."""
    return [(x, top), (x + width / 2, bottom - width * 0.4), (x, bottom + width * 0.25),
            (x - width / 2, bottom - width * 0.4)]


def drop_pts(cx, cy, r, n=48):
    """Sweat drop: round bottom, point at the top."""
    pts = []
    for i in range(n + 1):
        t = math.pi * i / n  # 0..pi, bottom half circle
        pts.append((cx + math.cos(t) * r, cy + math.sin(t) * r))
    pts.append((cx - r * 0.92, cy - r * 0.30))
    pts.append((cx, cy - r * 2.0))
    pts.append((cx + r * 0.92, cy - r * 0.30))
    return pts


# ---------------------------------------------------------------- masks

class Pen:
    """Draws one mask, in design units."""

    def __init__(self, w, h):
        self.im = Image.new("L", (w, h), 0)
        self.d = ImageDraw.Draw(self.im)

    @staticmethod
    def _p(pts):
        return [(x * SS, y * SS) for x, y in pts]

    def poly(self, pts):
        self.d.polygon(self._p(pts), fill=255)
        return self

    def ellipse(self, cx, cy, rx, ry, angle=0.0):
        return self.poly(ellipse_pts(cx, cy, rx, ry, angle))

    def rect(self, x0, y0, x1, y1):
        self.d.rectangle([x0 * SS, y0 * SS, x1 * SS, y1 * SS], fill=255)
        return self

    def rrect(self, x0, y0, x1, y1, r):
        self.d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS, y1 * SS], radius=r * SS, fill=255)
        return self

    def line(self, pts, width):
        p = self._p(pts)
        w = width * SS
        self.d.line(p, fill=255, width=int(round(w)), joint="curve")
        for x, y in p:
            self.d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=255)
        return self

    def capsule(self, p0, p1, r):
        return self.line([p0, p1], 2 * r)

    @property
    def m(self):
        return np.asarray(self.im, np.float32) / 255.0


def bbox(m, pad=0, thresh=0.003):
    ys = np.flatnonzero(m.max(axis=1) > thresh)
    if ys.size == 0:
        return None
    xs = np.flatnonzero(m.max(axis=0) > thresh)
    h, w = m.shape
    return (max(0, ys[0] - pad), min(h, ys[-1] + pad + 1),
            max(0, xs[0] - pad), min(w, xs[-1] + pad + 1))


def dilate(m, px):
    """m grown by px design units. Distance transform, so the edge stays hard."""
    r = px * SS
    inside = m > 0.5
    out = np.zeros_like(m)
    box = bbox(inside.astype(np.float32), pad=int(r) + 4)
    if box is None:
        return out
    y0, y1, x0, x1 = box
    d = ndimage.distance_transform_edt(~inside[y0:y1, x0:x1])
    out[y0:y1, x0:x1] = np.clip(r + 0.5 - d, 0, 1)
    return np.maximum(out, m)


def shifted(m, dx, dy):
    sx, sy = int(round(dx * SS)), int(round(dy * SS))
    h, w = m.shape
    out = np.zeros_like(m)
    src = m[max(0, -sy):h - max(0, sy), max(0, -sx):w - max(0, sx)]
    out[max(0, sy):max(0, sy) + src.shape[0], max(0, sx):max(0, sx) + src.shape[1]] = src
    return out


# ---------------------------------------------------------------- canvas

class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.W, self.H = w * SS, h * SS
        self.rgb = np.zeros((self.H, self.W, 3), np.float32)
        self.a = np.zeros((self.H, self.W), np.float32)

    def pen(self):
        return Pen(self.W, self.H)

    def over(self, color, alpha):
        box = bbox(alpha)
        if box is None:
            return
        y0, y1, x0, x1 = box
        self.over_box(color, alpha[y0:y1, x0:x1], box)

    def over_box(self, color, a, box):
        y0, y1, x0, x1 = box
        a = np.clip(a, 0, 1)
        ca = self.a[y0:y1, x0:x1]
        crgb = self.rgb[y0:y1, x0:x1]
        out_a = a + ca * (1 - a)
        col = np.asarray(color, np.float32)
        if col.ndim == 1:
            col = col[None, None, :]
        num = col * a[..., None] + crgb * (ca * (1 - a))[..., None]
        self.rgb[y0:y1, x0:x1] = num / np.maximum(out_a, 1e-6)[..., None]
        self.a[y0:y1, x0:x1] = out_a


def paint(cv, m, base, light=None, dark=None, ink=7.0, cel=0.0, grad=0.6, ldir=LIGHT,
          gloss=(), gloss_col=None, gloss_alpha=0.75):
    """Ink, gradient fill, cel shadow and gloss for one part."""
    if ink:
        cv.over(INK, dilate(m, ink))
    box = bbox(m)
    if box is None:
        return
    y0, y1, x0, x1 = box
    a = m[y0:y1, x0:x1]
    light = base if light is None else light
    dark = base if dark is None else dark

    lx, ly = ldir
    n = math.hypot(lx, ly)
    lx, ly = lx / n, ly / n
    yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    proj = xx * lx + yy * ly  # grows toward the light
    sel = proj[a > 0.5]
    lo, hi = (sel.min(), sel.max()) if sel.size else (0.0, 1.0)
    t = np.clip((proj - lo) / max(hi - lo, 1.0), 0, 1)[..., None]
    soft_l = base + (light - base) * grad
    soft_d = base + (dark - base) * grad
    col = np.where(t > 0.5, base + (soft_l - base) * (t - 0.5) * 2, soft_d + (base - soft_d) * t * 2)
    cv.over_box(col, a, box)

    if cel:
        lit = shifted(m, lx * cel, ly * cel)[y0:y1, x0:x1]
        lit = ndimage.gaussian_filter(lit, SS * 0.8)
        cv.over_box(dark, a * (1 - lit) * 0.92, box)

    if gloss:
        p = cv.pen()
        for g in gloss:
            p.ellipse(*g)
        gm = ndimage.gaussian_filter(p.m, SS * 1.4) * m
        cv.over(gloss_col if gloss_col is not None else light, gm * gloss_alpha)


def ink_lines(cv, lines, width, clip=None):
    p = cv.pen()
    for pts in lines:
        p.line(pts, width)
    m = p.m
    if clip is not None:
        m = m * clip
    cv.over(INK, m)


def rock(cv, cx, cy, r, seed, spin=0.0, ink=6.0):
    m = cv.pen().poly(rock_pts(cx, cy, r, seed, spin)).m
    paint(cv, m, STONE, STONE_LIGHT, STONE_DARK, ink=ink, cel=r * 0.32, grad=0.7)


def star(cv, cx, cy, r, spin=0.0, ink=6.0):
    m = cv.pen().poly(star_pts(cx, cy, r, r * 0.5, 5, spin)).m
    paint(cv, m, YELLOW, YELLOW_LIGHT, YELLOW_DARK, ink=ink, cel=r * 0.16, grad=0.7,
          gloss=[(cx - r * 0.18, cy - r * 0.22, r * 0.18, r * 0.10, -30)], gloss_col=WHITE_HOT)


def puff(cv, cx, cy, s, ink=6.0):
    p = cv.pen()
    for dx, dy, r in ((-32, 4, 30), (6, -14, 38), (44, 6, 27)):
        p.ellipse(cx + dx * s, cy + dy * s, r * s, r * s)
    paint(cv, p.m, CREAM, CREAM, CREAM_DARK, ink=ink, cel=9 * s, grad=0.3)


def slab(cv, x0, y0, x1, y1, radius, notch, cracks, ink, crack_w):
    """Stone floor block with a crater bitten out of its top edge."""
    block = cv.pen().rrect(x0, y0, x1, y1, radius).m
    hole = cv.pen().poly(notch).m
    crater = block * hole
    cv.over(STONE_DEEP, crater)
    m = block * (1 - hole)
    paint(cv, m, STONE, STONE_LIGHT, STONE_DARK, ink=ink, grad=0.5, ldir=(0, -1))
    h = y1 - y0
    cv.over(STONE_LIGHT, cv.pen().rect(x0, y0, x1, y0 + h * 0.21).m * m)
    cv.over(STONE_DARK, cv.pen().rect(x0, y1 - h * 0.24, x1, y1).m * m)
    # a soft sheen along the top face
    cv.over(rgb(214, 212, 230), cv.pen().rrect(x0 + h * 0.3, y0 + h * 0.05, x1 - h * 0.3, y0 + h * 0.11,
                                                 h * 0.03).m * m * 0.55)
    ink_lines(cv, cracks, crack_w, clip=m)


def finish(cv, out_path, cream_px, under=None, bg=None):
    final = Canvas(cv.w, cv.h)
    full = (0, cv.H, 0, cv.W)
    if bg is not None:
        final.rgb[:] = bg
        final.a[:] = 1.0
    if under is not None:
        final.over_box(under.rgb, under.a, full)
    final.over(CREAM, dilate(cv.a, cream_px))
    final.over_box(cv.rgb, cv.a, full)
    data = np.dstack([final.rgb * 255.0, final.a * 255.0]).clip(0, 255).astype(np.uint8)
    img = Image.fromarray(data, "RGBA").resize((cv.w, cv.h), Image.LANCZOS)
    img.save(out_path)
    print("wrote", out_path)


# ---------------------------------------------------------------- full art

def full_art(out_path):
    w, h = 1100, 865
    cv = Canvas(w, h)
    under = Canvas(w, h)
    cx = 550

    # Speed shards streaming up off the fall, cream straight on the black ground.
    p = under.pen()
    for x, top, bottom, width in ((414, 92, 352, 30), (482, 30, 340, 40), (550, 8, 340, 48),
                                  (618, 38, 340, 40), (686, 100, 352, 30)):
        p.poly(shard_pts(x, top, bottom, width))
    for ex, ey, rx, ry in ((352, 150, 9, 16), (750, 168, 8, 14), (318, 76, 6, 11), (786, 92, 6, 11)):
        p.ellipse(ex, ey, rx, ry)
    under.over(CREAM, p.m)

    # Impact burst, behind the slab so it only sprays up and out of the crater.
    cy = 652
    paint(cv, cv.pen().poly(burst_pts(cx, cy, 500, 340, 70, 15, seed=3)).m,
          BURST, YELLOW, BURST_DARK, ink=7, grad=0.6, ldir=(0, -1))
    cv.over(YELLOW, cv.pen().poly(burst_pts(cx, cy, 400, 262, 50, 15, seed=4, spin=6)).m)
    cv.over(WHITE_HOT, cv.pen().poly(burst_pts(cx, cy, 270, 170, 30, 13, seed=5, spin=-4)).m)

    # Flying rubble behind the guy.
    rock(cv, 236, 498, 17, seed=11, spin=20)
    rock(cv, 868, 492, 16, seed=12, spin=-10)
    rock(cv, 196, 404, 10, seed=13)
    rock(cv, 908, 400, 10, seed=14, spin=40)

    slab(cv, 56, 646, 1044, 814, 26,
         notch=[(376, 596), (390, 646), (430, 672), (468, 664), (502, 704), (550, 692),
                (598, 708), (634, 668), (672, 674), (710, 646), (724, 596)],
         cracks=[[(468, 700), (420, 736), (352, 728), (280, 772)],
                 [(428, 676), (362, 692), (300, 674), (206, 694)],
                 [(634, 704), (690, 742), (770, 734), (852, 778)],
                 [(670, 678), (742, 694), (810, 678), (902, 698)],
                 [(522, 712), (508, 772)], [(590, 714), (612, 770)]],
         ink=7, crack_w=7)

    puff(cv, 178, 630, 1.0)
    puff(cv, 922, 630, 1.0)

    # Arms flung straight up by the stop, mitten hands.
    for s in (-1, 1):
        shoulder = (cx + s * 166, 456)
        hand = (cx + s * 300, 262)
        paint(cv, cv.pen().capsule(shoulder, hand, 33).m, ORANGE, ORANGE_LIGHT, ORANGE_DARK,
              ink=7, cel=11)
        paint(cv, cv.pen().ellipse(hand[0], hand[1] - 8, 50, 52).m, ORANGE, ORANGE_LIGHT, ORANGE_DARK,
              ink=7, cel=13, gloss=[(hand[0] - 16, hand[1] - 34, 16, 10, -30)], gloss_col=ORANGE_GLOSS)
        paint(cv, cv.pen().ellipse(hand[0] - s * 40, hand[1] + 16, 21, 17, s * 30).m,
              ORANGE, ORANGE_LIGHT, ORANGE_DARK, ink=6, cel=6)

    # The body, sunk into the crater.
    body = cv.pen().ellipse(cx, 500, 216, 186).m
    paint(cv, body, ORANGE, ORANGE_LIGHT, ORANGE_DARK, ink=8, cel=34, grad=0.7,
          gloss=[(452, 386, 76, 36, -24), (394, 444, 13, 10, -24)], gloss_col=ORANGE_GLOSS, gloss_alpha=0.85)

    # Screwed-shut eyes under brows yanked down hard.
    ink_lines(cv, [[(436, 446), (488, 474), (436, 502)],
                   [(664, 446), (612, 474), (664, 502)]], 17)
    ink_lines(cv, [[(426, 398), (496, 434)], [(674, 398), (604, 434)]], 15)

    # Screaming mouth.
    mouth = cv.pen().ellipse(cx, 584, 74, 58).m
    paint(cv, mouth, MOUTH, ink=8)
    cv.over(rgb(60, 6, 14), cv.pen().ellipse(cx, 552, 70, 26).m * mouth)
    cv.over(TONGUE, cv.pen().ellipse(cx + 8, 626, 52, 30).m * mouth)

    # Sweat drop on the brow.
    paint(cv, cv.pen().poly(drop_pts(700, 384, 17)).m, SWEAT, SWEAT, SWEAT_DARK, ink=6, cel=5,
          gloss=[(694, 380, 5, 7, 0)], gloss_col=rgb(255, 255, 255))

    # Rubble kicked up in front of him.
    rock(cv, 300, 590, 24, seed=21, spin=10)
    rock(cv, 806, 590, 26, seed=22, spin=-20)
    rock(cv, 468, 702, 26, seed=23)
    rock(cv, 638, 706, 24, seed=24, spin=30)
    rock(cv, 552, 716, 17, seed=25, spin=12)

    star(cv, 162, 128, 44, spin=-12)
    star(cv, 940, 116, 50, spin=10)
    star(cv, 1014, 318, 24, spin=-20)

    finish(cv, out_path, cream_px=14, under=under, bg=rgb(0, 0, 0))


# ---------------------------------------------------------------- mini

def mini_art(out_path):
    w = h = 512
    cv = Canvas(w, h)
    cx = 256

    cy = 374
    paint(cv, cv.pen().poly(burst_pts(cx, cy, 218, 150, 40, 13, seed=7)).m,
          BURST, YELLOW, BURST_DARK, ink=6, grad=0.6, ldir=(0, -1))
    cv.over(YELLOW, cv.pen().poly(burst_pts(cx, cy, 168, 112, 30, 13, seed=8, spin=7)).m)
    cv.over(WHITE_HOT, cv.pen().poly(burst_pts(cx, cy, 104, 70, 20, 11, seed=9, spin=-5)).m)

    rock(cv, 92, 300, 13, seed=31, spin=10, ink=5)
    rock(cv, 422, 300, 13, seed=32, spin=-30, ink=5)

    slab(cv, 36, 368, 476, 466, 18,
         notch=[(192, 330), (200, 368), (220, 384), (238, 378), (256, 398), (274, 380),
                (292, 386), (312, 368), (320, 330)],
         cracks=[[(222, 384), (176, 408), (126, 400), (78, 430)],
                 [(290, 386), (338, 410), (390, 402), (438, 432)]],
         ink=6, crack_w=6)

    # Fat down arrow, tip buried in the crater.
    p = cv.pen().rrect(cx - 52, 34, cx + 52, 252, 24)
    p.poly([(cx - 124, 222), (cx + 124, 222), (cx, 396)])
    paint(cv, p.m, RED, RED_LIGHT, RED_DARK, ink=7, cel=18, grad=0.75, ldir=(-1, -0.3))
    gloss = cv.pen().rrect(cx - 34, 56, cx - 16, 206, 9).m
    cv.over(RED_GLOSS, ndimage.gaussian_filter(gloss, SS) * 0.8)
    gloss = cv.pen().poly([(cx - 96, 236), (cx - 70, 236), (cx - 14, 330), (cx - 26, 340)]).m
    cv.over(RED_GLOSS, ndimage.gaussian_filter(gloss, SS) * 0.55)

    rock(cv, 214, 396, 13, seed=33, ink=5)
    rock(cv, 300, 398, 12, seed=34, spin=20, ink=5)

    finish(cv, out_path, cream_px=10)


def main():
    out = sys.argv[1].rstrip("/\\")
    full_art(out + "/plummet.png")
    mini_art(out + "/plummet_mini.png")


if __name__ == "__main__":
    main()
