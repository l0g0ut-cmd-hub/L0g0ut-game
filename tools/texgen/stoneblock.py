"""Тёсаные гранитные блоки (бордюры, основания фонарей и тории): 2×2 блока со швами, фаски со сколами,
следы зубила (косые бороздки), лишайник пятнами, потёки. Бесшовно. Выход: albedo, normal, ms (A — гладкость).
Запуск: python3 stoneblock.py OUTDIR [size]"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage

out = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 1024
rng = np.random.default_rng(3)
os.makedirs(out, exist_ok=True)

def tile_noise(n, cells, octaves=5, persistence=0.5, seed=0):
    r = np.random.default_rng(seed)
    acc = np.zeros((n, n)); amp = 1.0; tot = 0.0; c = cells
    for o in range(octaves):
        g = r.random((c, c))
        z = ndimage.zoom(np.tile(g, (3, 3)), n / c, order=3, mode='grid-wrap', grid_mode=True)
        acc += amp * z[n:2 * n, n:2 * n]; tot += amp; amp *= persistence; c *= 2
        if c > n: break
    return acc / tot

yy, xx = np.mgrid[0:N, 0:N].astype(float)
bw = N / 2.0; bh = N / 2.0
# ряды со сдвигом (перевязка): нижний ряд смещён на полблока
row = (yy // bh).astype(int)
xo = (xx + (row % 2) * bw * 0.5) % N
col = (xo // bw).astype(int)
lx = xo - col * bw; ly = yy - row * bh
edge = np.minimum(np.minimum(lx, bw - lx), np.minimum(ly, bh - ly))   # px до шва
bid = row * 4 + col
nb = 16
warp = (tile_noise(N, 10, 4, 0.5, 1) - 0.5) * 6
edge = edge + warp
gap = 3.0
bev = 10.0 * (0.6 + 0.8 * tile_noise(N, 14, 3, 0.5, 2))
h = np.clip((edge - gap) / bev, 0, 1); h = h * h * (3 - 2 * h)
chip = np.clip((tile_noise(N, 20, 4, 0.5, 3) - 0.4) * 5, 0, 1)
h = h * (0.6 + 0.4 * chip) + (1 - chip) * h ** 3 * 0.4
# следы зубила: косые бороздки под своим углом на каждом блоке
angs = rng.uniform(0.3, 1.2, nb)[bid] * np.sign(rng.uniform(-1, 1, nb))[bid]
ph = rng.uniform(0, 6.28, nb)[bid]
stri = np.sin((xx * np.cos(angs) + yy * np.sin(angs)) * 0.55 + ph + 2.5 * tile_noise(N, 32, 3, 0.5, 4))
stri = np.clip(stri, -1, 1) * (0.4 + 0.6 * tile_noise(N, 16, 3, 0.5, 5))
detail = tile_noise(N, 24, 6, 0.55, 6); fine = tile_noise(N, 128, 3, 0.5, 7)
height = h * (0.8 + 0.06 * rng.uniform(-1, 1, nb)[bid]) + 0.025 * stri * h + 0.12 * (detail - 0.5) * h + 0.04 * (fine - 0.5)
in_gap = 1 - np.clip((edge - gap * 0.5) / (gap * 0.9), 0, 1)

base = rng.uniform(0.07, 0.13, nb)[bid]
warm = rng.uniform(-1, 1, nb)[bid]
mott = tile_noise(N, 5, 4, 0.5, 8); mid = tile_noise(N, 18, 4, 0.5, 9); grain = tile_noise(N, 256, 2, 0.5, 10)
lum = base * (0.75 + 0.5 * mott) * (0.85 + 0.3 * mid) * (0.9 + 0.2 * grain)
alb = np.stack([lum * (1 + 0.08 * warm), lum * (1 + 0.02 * warm), lum * (1.03 - 0.06 * warm)], -1)
# лишайник: светло-серо-зелёные пятна; потёки вниз от верхнего края блока
lich = np.clip((tile_noise(N, 12, 5, 0.6, 11) - 0.62) * 6, 0, 1) * (tile_noise(N, 64, 3, 0.5, 12) > 0.45)
alb = alb * (1 - 0.6 * lich[..., None]) + np.array([0.16, 0.17, 0.12]) * 0.6 * lich[..., None]
streak = np.clip(1 - ly / (bh * 0.8), 0, 1) * (tile_noise(N, 64, 2, 0.5, 13)[:, :] > 0.55)
streak = ndimage.gaussian_filter(streak.astype(float), (6, 0.8), mode='wrap')
alb *= (1 - 0.25 * streak[..., None])
alb *= (1 - 0.35 * np.clip(1 - (edge - gap) / 18, 0, 1)[..., None] ** 2)
dirt = np.array([0.028, 0.023, 0.018])
alb = alb * (1 - in_gap[..., None]) + dirt * in_gap[..., None]
alb = np.clip(alb, 0, 1)
smooth = np.clip(0.30 + 0.15 * (mid - 0.5) - 0.2 * lich - 0.2 * in_gap, 0.05, 0.8)

def normal(hh, strength):
    gx = (np.roll(hh, -1, 1) - np.roll(hh, 1, 1)) * 0.5; gy = (np.roll(hh, -1, 0) - np.roll(hh, 1, 0)) * 0.5
    nx = -gx * strength; ny = gy * strength; nz = np.ones_like(hh); l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l, ny / l, nz / l], -1)
hs = ndimage.gaussian_filter(height, 0.7, mode='wrap')
nrm = normal(hs, N / 64.0)
def save(a, name, mode='RGB'):
    Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8), mode).save(os.path.join(out, name))
save(alb ** (1 / 2.2), 'stoneblock_albedo.png'); save(nrm * 0.5 + 0.5, 'stoneblock_normal.png')
ms = np.zeros((N, N, 4)); ms[..., 3] = smooth; save(ms, 'stoneblock_ms.png', 'RGBA')
print('ok')
