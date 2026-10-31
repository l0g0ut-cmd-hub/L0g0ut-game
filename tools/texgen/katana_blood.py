"""Маска крови на клинке под развёртку KatanaBuilder.BladeMesh (u — 5 граней по 0,2: обух, синоги-дзи, хира, хира, синоги-дзи;
v — от хабаки к острию). Значение — «порог»: шейдер BladePath/Blade заливает кровью точки, где маска меньше текущего количества
крови. Поэтому с каждым попаданием кровь растёт от острия к хабаки, сначала по кромке и хире, потом по синоги-дзи и обуху,
неровными потёками вдоль клинка (кровь стекает по стали) с каплями на концах; после стряхивания (тибури) остаются только
самые «низкие» места — тонкие полосы у кромки и у острия.

    python3 tools/texgen/katana_blood.py <out_dir>

Выход: katana_blood_mask.png (L, 256×1024).
"""
import sys, os
import numpy as np
from PIL import Image, ImageFilter

OUT = sys.argv[1]
W, H = 256, 1024
rng = np.random.default_rng(23)
u = (np.arange(W) + 0.5) / W
v = (np.arange(H) + 0.5) / H
U, V = np.meshgrid(u, v)

face = np.minimum((U * 5).astype(int), 4)
fu = U * 5 - face
d_edge = np.where(face == 2, 1 - fu, np.where(face == 3, fu, 1.0))   # 0 у кромки на хире


def noise(h, w, sy, sx, seed):
    g = np.random.default_rng(seed).random((max(2, int(h / sy)), max(2, int(w / sx))))
    return np.array(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255.0


# потёки: шум, сильно вытянутый вдоль клинка, + мелкая рябь
streak = noise(H, W, 90, 6, 1) * 0.65 + noise(H, W, 30, 3, 2) * 0.35
fine = noise(H, W, 6, 4, 3)

# основа: от острия к хабаки
base = (1.0 - V) ** 0.85
# по грани: хира у кромки — первой, хира у ребра и синоги-дзи — позже, обух — последним
side = np.where(face == 0, 0.32, np.where((face == 1) | (face == 4), 0.16, 0.10 * d_edge))
m = base * 0.78 + side + (streak - 0.5) * 0.42 + (fine - 0.5) * 0.08

# капли на концах потёков: несколько круглых «бусин», чуть ниже порога соседей
for i in range(46):
    cu = rng.random(); cv = rng.random() ** 0.6
    r = rng.uniform(0.004, 0.011)
    du = (U - cu) / (r * 1.0); dv = (V - cv) / (r * 0.35 * W / H * 4.0)
    blob = np.exp(-(du * du + dv * dv))
    m -= blob * rng.uniform(0.06, 0.14)

# у острия (боси) кровь держится всегда, когда она есть: «низкая» зона
m -= np.clip((V - 0.93) / 0.07, 0, 1) * 0.22

m = ((m - m.min()) / (m.max() - m.min())) ** 1.4   # первые попадания уже видны: «низких» мест больше
img = Image.fromarray((m * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(0.6))
os.makedirs(OUT, exist_ok=True)
img.save(os.path.join(OUT, "katana_blood_mask.png"))
print("katana_blood_mask.png", img.size, "mean", round(float(m.mean()), 3))
