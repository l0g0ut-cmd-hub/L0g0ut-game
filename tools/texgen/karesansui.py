"""Сухой сад карэсансуй перед храмом: маска умножения для гравия (BladePath/AO; белое — без изменений).
Грабли оставляют бороздки: вдоль сада — прямые параллельные, вокруг камней — концентрические кольца, переходящие в прямые.
Поле 4,0 × 2,0 м → 1024×512; камни (в метрах от центра) совпадают с EnvironmentBuilder.AddKaresansui. Края мягко гаснут.
Запуск: python3 karesansui.py OUTDIR"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage

out = sys.argv[1]; os.makedirs(out, exist_ok=True)
W, H = 1024, 512
SX, SZ = 4.0, 2.0
rocks = [(-1.05, 0.15, 0.32), (0.55, -0.35, 0.24), (1.25, 0.42, 0.18)]   # x, z, радиус камня (м)
xs = (np.arange(W) + 0.5) / W * SX - SX / 2
zs = (0.5 - (np.arange(H) + 0.5) / H) * SZ          # строка 0 — дальний край (+z)
X, Z = np.meshgrid(xs, zs)
spacing = 0.07
# прямые бороздки вдоль X: по координате Z
phase = Z / spacing
# кольца вокруг камней: по расстоянию до ближайшего камня; вес колец гаснет к радиусу ~0.55 м от края камня
best = np.full(X.shape, 1e9); ringw = np.zeros(X.shape)
for rx, rz, rr in rocks:
    dist = np.sqrt((X - rx) ** 2 + (Z - rz) ** 2) - rr
    w = np.clip(1 - (dist - 0.38) / 0.18, 0, 1)
    sel = dist < best
    best = np.where(sel, dist, best)
    ringw = np.maximum(ringw, w)
ring_phase = best / spacing
grooveS = (0.5 + 0.5 * np.cos(2 * np.pi * phase)) ** 3
grooveR = (0.5 + 0.5 * np.cos(2 * np.pi * ring_phase)) ** 3
groove = grooveS * (1 - ringw) + grooveR * ringw
groove = np.where(best < 0.0, 0.0, groove)            # под камнем — без бороздок (там сам камень)
# неровность грабель и мягкий край поля
wob = ndimage.gaussian_filter(np.random.default_rng(3).random((H, W)), 6) - 0.5
groove *= 0.85 + 0.5 * wob
edge = np.clip(np.minimum.reduce([X + SX / 2, SX / 2 - X, Z + SZ / 2, SZ / 2 - Z]) / 0.12, 0, 1)
mask = 1.0 - 0.55 * groove * edge
# лёгкое затенение у камней (камень стоит в гравии)
for rx, rz, rr in rocks:
    dist = np.sqrt((X - rx) ** 2 + (Z - rz) ** 2) - rr
    mask *= 1 - 0.35 * np.clip(1 - dist / 0.12, 0, 1) * (dist > -0.02)
mask = ndimage.gaussian_filter(mask, 0.8)
Image.fromarray(np.clip(mask * 255 + 0.5, 0, 255).astype(np.uint8)).save(os.path.join(out, 'karesansui_mask.png'))
print('ok')
