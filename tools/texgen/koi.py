"""Карп кои сверху (64×128, голова вверху): веретено тела, хвостовой плавник-веер, грудные плавники, узор — белый с
алыми и рыжими пятнами (кохаку/тайсё), края мягкие. RGBA. Запуск: python3 koi.py OUTDIR"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
W, H = 64, 128
yy, xx = np.mgrid[0:H, 0:W]
u = (xx + 0.5) / W - 0.5; v = 1 - (yy + 0.5) / H        # v: 0 хвост … 1 голова
body_w = 0.22 * np.sin(np.clip((v - 0.18) / 0.82, 0, 1) * np.pi) ** 0.7
body = np.clip(1 - np.abs(u) / np.maximum(body_w, 1e-3), 0, 1) * (v > 0.17)
tail = np.clip(1 - np.abs(u) / (0.05 + 0.75 * (0.22 - v).clip(0)), 0, 1) * (v < 0.24) * (v > 0.02)
fins = np.clip(1 - ((np.abs(u) - 0.2) ** 2 / 0.006 + (v - 0.66) ** 2 / 0.003), 0, 1)
a = np.clip(np.maximum(body ** 0.5, np.maximum(tail * 0.75, fins * 0.7)), 0, 1)
rng = np.random.default_rng(int(sys.argv[2]) if len(sys.argv) > 2 else 2)
blot = ndimage.gaussian_filter(rng.random((H, W)), 6)
blot = (blot - blot.min()) / (blot.max() - blot.min())
red = np.clip((blot - 0.5) / 0.08, 0, 1)
col = np.ones((H, W, 3)) * np.array([0.95, 0.93, 0.88])
col = col * (1 - red[..., None]) + np.array([0.9, 0.25, 0.06]) * red[..., None]
col *= (0.75 + 0.25 * np.clip(1 - np.abs(u) / 0.22, 0, 1))[..., None]   # спинка светлее, бока темнее
img = np.dstack([col, ndimage.gaussian_filter(a, 0.8)])
Image.fromarray(np.clip(img * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(out, 'koi.png'))
print('ok')
