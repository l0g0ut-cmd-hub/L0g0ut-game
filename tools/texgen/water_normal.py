"""Бесшовная карта нормалей мелкой ряби пруда (OpenGL +Y): сумма наклонных синусоид с целыми частотами (бесшовно) и мелкий шум.
Запуск: python3 water_normal.py OUTDIR"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
N = 512
rng = np.random.default_rng(11)
y, x = np.mgrid[0:N, 0:N] / N
h = np.zeros((N, N))
for k in range(14):
    fx, fy = rng.integers(-9, 10), rng.integers(-9, 10)
    if fx == 0 and fy == 0: continue
    a = 1.0 / np.hypot(fx, fy)
    h += a * np.sin(2 * np.pi * (fx * x + fy * y) + rng.random() * 6.28)
n = rng.random((N, N)); n = ndimage.gaussian_filter(n, 3, mode='wrap')
h = h / np.abs(h).max() + (n - n.mean()) * 1.5
gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
s = 6.0
nx, ny, nz = -gx * s, gy * s, np.ones_like(h)
l = np.sqrt(nx * nx + ny * ny + nz * nz)
img = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
Image.fromarray(np.clip(img * 255 + 0.5, 0, 255).astype(np.uint8)).save(os.path.join(out, 'water_normal.png'))
print('ok')
