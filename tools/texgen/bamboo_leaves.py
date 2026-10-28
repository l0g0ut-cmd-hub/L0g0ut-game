"""Карточка листвы бамбука (RGBA, альфа-срез): веера узких ланцетных листьев на тонких веточках, листья свисают,
оттенки от тёмно-зелёного до жёлто-зелёного, у каждого листа светлая средняя жилка.
Запуск: python3 tools/texgen/bamboo_leaves.py OUT.png [size]"""
import sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 512
S = N * 4
rng = np.random.default_rng(31)
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

def leaf(x0, y0, ang, L, W, col):
    pts = []
    for i in range(21):
        t = i / 20
        w = W * math.sin(math.pi * t ** 0.8) * (1 - 0.15 * t)
        pts.append((t * L, w))
    pts += [(t * L, -w) for (t, w) in [(p[0] / L, p[1]) for p in reversed(pts)]]
    ca, sa = math.cos(ang), math.sin(ang)
    poly = [(x0 + x * ca - y * sa, y0 + x * sa + y * ca) for (x, y) in pts]
    d.polygon(poly, fill=col + (255,), outline=tuple(int(c * 0.8) for c in col) + (255,))
    d.line([(x0, y0), (x0 + ca * L * 0.95, y0 + sa * L * 0.95)], fill=tuple(int(min(255, c * 1.25)) for c in col) + (255,), width=max(1, int(W * 0.12)))

greens = [np.array([34, 62, 26]), np.array([48, 84, 32]), np.array([66, 104, 40]), np.array([96, 120, 48]), np.array([120, 126, 56])]
gw = np.array([0.2, 0.3, 0.28, 0.15, 0.07])
# веточки: несколько дуг по карточке, на каждой — веера листьев
for b in range(7):
    x = S * rng.uniform(0.3, 0.7); y = S * rng.uniform(0.22, 0.45)
    ang = rng.uniform(-0.3, 0.3) + (math.pi * 0.5 if rng.random() < 0.5 else 0)
    ang = rng.uniform(0.2, math.pi - 0.2)
    pts = [(x, y)]
    for k in range(6):
        ang += rng.uniform(-0.25, 0.25)
        x += math.cos(ang) * S * 0.06; y += math.sin(ang) * S * 0.06
        pts.append((x, y))
    d.line(pts, fill=(70, 78, 40, 255), width=int(S * 0.004))
    for (px, py) in pts[1:]:
        fan = rng.integers(2, 5)
        base = rng.uniform(0.4, 2.6)   # листья свисают вниз-в стороны
        for f in range(fan):
            a = base + rng.uniform(-0.5, 0.5)
            col = greens[rng.choice(len(greens), p=gw)] * rng.uniform(0.85, 1.1)
            leaf(px, py, a, S * rng.uniform(0.13, 0.2), S * rng.uniform(0.014, 0.02), tuple(int(min(255, c)) for c in col))

small = img.resize((N, N), Image.LANCZOS)
arr = np.asarray(small).astype(float)
rgb = Image.fromarray(np.clip(arr[..., :3], 0, 255).astype(np.uint8))
dil = np.asarray(rgb.filter(ImageFilter.MaxFilter(9))).astype(float)
a = arr[..., 3:4] / 255.0
mix = arr[..., :3] * (a > 0.02) + dil * (a <= 0.02)
Image.fromarray(np.clip(np.concatenate([mix, arr[..., 3:4]], -1), 0, 255).astype(np.uint8), 'RGBA').save(out)
print('ok', out)
