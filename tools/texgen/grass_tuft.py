"""Пучок травы для карточек (256×256, RGBA): изогнутые сужающиеся травинки от основания, тёмно-зелёные с оливковыми
и соломенными кончиками, у земли темнее (затенение пучка). Цвет под прозрачными краями растянут — без каймы на мипах.

    python3 tools/texgen/grass_tuft.py <out.png>
"""
import sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

S, SS = 256, 4
W = S * SS
rng = np.random.default_rng(23)
col = Image.new("RGB", (W, W), (0, 0, 0)); alpha = Image.new("L", (W, W), 0)
dc, da = ImageDraw.Draw(col), ImageDraw.Draw(alpha)
blades = []
for i in range(46):
    x0 = W * (0.5 + rng.normal(0, 0.11))
    h = W * rng.uniform(0.45, 0.95)
    lean = rng.normal(0, 0.35)
    width = W * rng.uniform(0.012, 0.024)
    tone = rng.uniform(0, 1)
    blades.append((h, x0, lean, width, tone))
blades.sort(key=lambda b: -b[0])   # высокие сзади
for h, x0, lean, width, tone in blades:
    n = 24
    pts_l, pts_r = [], []
    for k in range(n + 1):
        t = k / n
        bend = lean * h * (t ** 1.8)
        cx = x0 + bend
        cy = W - 2 - h * t
        w = width * (1 - t) ** 0.85
        # нормаль к травинке
        dx = lean * h * 1.8 * t ** 0.8 / n + 1e-6
        nx, ny = 1.0, dx / (h / n)
        ln = (nx * nx + ny * ny) ** 0.5
        nx, ny = nx / ln, ny / ln
        pts_l.append((cx - nx * w, cy - ny * w)); pts_r.append((cx + nx * w, cy + ny * w))
    poly = pts_l + pts_r[::-1]
    base = np.array([0.10, 0.16, 0.06]) + tone * np.array([0.06, 0.06, 0.0])
    tip = np.array([0.34, 0.33, 0.14]) if tone > 0.6 else np.array([0.18, 0.26, 0.09])
    # травинка — градиент от основания к кончику: рисуем полосами
    for k in range(n):
        t = (k + 0.5) / n
        c = base * (1 - t) + tip * t
        c = c * (0.55 + 0.45 * min(1.0, t * 2.2))   # у земли темнее
        seg = [pts_l[k], pts_l[k + 1], pts_r[k + 1], pts_r[k]]
        dc.polygon(seg, fill=tuple(int(v * 255) for v in np.clip(c, 0, 1)))
        da.polygon(seg, fill=255)
img = Image.merge("RGBA", (*col.split(), alpha)).resize((S, S), Image.LANCZOS)
a = np.array(img, np.float32) / 255.0
m = a[..., 3] > 0.3
idx = ndimage.distance_transform_edt(~m, return_distances=False, return_indices=True)
for c in range(3):
    a[..., c] = a[..., c][tuple(idx)]
Image.fromarray((a * 255).astype(np.uint8), "RGBA").save(sys.argv[1])
print("ok")
