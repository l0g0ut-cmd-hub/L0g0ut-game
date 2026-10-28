"""Карточка кроны японского клёна (момидзи), RGBA с альфа-срезом: гроздь семилопастных листьев с зубчиками,
от тёмно-багрового до алого (редкие оранжевые), с жилками и черешками, под ними тонкие веточки.
Рисуется в 4× и сглаживается вниз. Запуск: python3 tools/texgen/maple_cluster.py OUT.png [size]"""
import sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 512
S = N * 4
rng = np.random.default_rng(23)
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
C = S / 2

def twig(x0, y0, ang, length, w, depth, tips):
    x1 = x0 + math.cos(ang) * length; y1 = y0 + math.sin(ang) * length
    d.line([(x0, y0), (x1, y1)], fill=(40, 24, 22, 255), width=max(2, int(w)))
    tips.append((x1, y1))
    if depth > 0:
        for s in (-1, 1):
            twig(x1, y1, ang + s * rng.uniform(0.35, 0.8), length * rng.uniform(0.5, 0.72), w * 0.65, depth - 1, tips)

def leaf_poly(cx, cy, size, rot, squash):
    """Лист момидзи: 7 узких заострённых лопастей с зубчиками, глубокие вырезы, без лопасти у черешка."""
    pts = []
    lobes = 7
    lens = [0.55, 0.78, 0.95, 1.0, 0.95, 0.78, 0.55]
    span = math.radians(250)
    a0 = -span / 2
    for i in range(lobes):
        ac = a0 + span * (i + 0.5) / lobes
        L = lens[i] * size
        hw = math.radians(13)
        # вырез между лопастями (у основания)
        av = ac - span / lobes / 2
        pts.append((0.32 * size * math.cos(av), 0.32 * size * math.sin(av)))
        # левый край лопасти с зубчиками → кончик → правый край
        for k in range(1, 6):
            t = k / 6
            r = 0.32 * size + (L - 0.32 * size) * t
            w = hw * (1 - t) * (1.15 if k % 2 else 0.85)
            pts.append((r * math.cos(ac - w), r * math.sin(ac - w)))
        pts.append((L * math.cos(ac), L * math.sin(ac)))
        for k in range(5, 0, -1):
            t = k / 6
            r = 0.32 * size + (L - 0.32 * size) * t
            w = hw * (1 - t) * (1.15 if k % 2 else 0.85)
            pts.append((r * math.cos(ac + w), r * math.sin(ac + w)))
    av = a0 + span
    pts.append((0.32 * size * math.cos(av), 0.32 * size * math.sin(av)))
    pts.append((0.12 * size * math.cos(math.pi), 0.0))   # у черешка
    cr, sr = math.cos(rot), math.sin(rot)
    return [(cx + (x * cr - y * sr), cy + (x * sr + y * cr) * squash) for (x, y) in pts], lens, span, a0

tips = []
for k in range(6):
    twig(C + rng.uniform(-0.05, 0.05) * S, C + 0.32 * S, -math.pi / 2 + rng.uniform(-1.1, 1.1), S * 0.2, S * 0.011, 3, tips)

leaves = []
for i in range(165):
    rr = 0.44 * math.sqrt(rng.random()) ** 0.85
    a = rng.random() * 2 * math.pi
    leaves.append((C + math.cos(a) * rr * S, C + math.sin(a) * rr * S * 0.92, rng.uniform(0.05, 0.078) * S))
leaves.sort(key=lambda f: f[1])
palette = [np.array([110, 10, 16]), np.array([150, 16, 20]), np.array([185, 28, 24]), np.array([205, 50, 26]), np.array([214, 92, 30])]
weights = np.array([0.28, 0.34, 0.26, 0.10, 0.02])
for (fx, fy, fr) in leaves:
    rot = rng.random() * 2 * math.pi
    squash = rng.uniform(0.6, 1.0)
    base = palette[rng.choice(len(palette), p=weights)] * rng.uniform(0.85, 1.08)
    poly, lens, span, a0 = leaf_poly(fx, fy, fr, rot, squash)
    # черешок к ближней веточке
    ang = rot + math.pi
    d.line([(fx, fy), (fx + math.cos(ang) * fr * 0.9, fy + math.sin(ang) * fr * 0.9 * squash)], fill=(70, 20, 18, 255), width=max(1, int(fr * 0.05)))
    col = tuple(int(min(255, c)) for c in base) + (255,)
    d.polygon(poly, fill=col, outline=tuple(int(c * 0.75) for c in base) + (255,))
    # жилки — темнее, от центра к кончикам
    for i in range(7):
        ac = a0 + span * (i + 0.5) / 7 + rot
        L = lens[i] * fr * 0.92
        d.line([(fx, fy), (fx + math.cos(ac) * L, fy + math.sin(ac) * L * squash)], fill=tuple(int(c * 0.7) for c in base) + (255,), width=max(1, int(fr * 0.025)))
    # блик на половине листа (лист чуть изогнут)
    hl = poly[: len(poly) // 2] + [(fx, fy)]
    d.polygon(hl, fill=tuple(int(min(255, c * 1.12)) for c in base) + (255,))

small = img.resize((N, N), Image.LANCZOS)
arr = np.asarray(small).astype(float)
yy, xx = np.mgrid[0:N, 0:N]
r = np.sqrt((xx - N / 2) ** 2 + (yy - N / 2) ** 2) / (N / 2)
arr[..., :3] *= (0.72 + 0.28 * np.clip(r / 0.8, 0, 1))[..., None]   # самозатенение к центру грозди
rgb = Image.fromarray(np.clip(arr[..., :3], 0, 255).astype(np.uint8))
dil = np.asarray(rgb.filter(ImageFilter.MaxFilter(9))).astype(float)
a = arr[..., 3:4] / 255.0
mix = arr[..., :3] * (a > 0.02) + dil * (a <= 0.02)
Image.fromarray(np.clip(np.concatenate([mix, arr[..., 3:4]], -1), 0, 255).astype(np.uint8), 'RGBA').save(out)
print('ok', out)
