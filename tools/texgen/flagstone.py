"""Бесшовная каменная мостовая (исидатами): неправильные плиты по Вороному на торе, фаски, сколы, трещины,
мох и грязь в швах, влажность (лужи темнее и глаже). Выход: albedo (RGB), normal (RGB, OpenGL +Y), metal/smooth (RGBA: A — гладкость).
Запуск: python3 flagstone.py OUTDIR [size]"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

out = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 1024
rng = np.random.default_rng(7)
os.makedirs(out, exist_ok=True)

def tile_noise(n, cells, octaves=5, persistence=0.5, seed=0):
    """Бесшовный fBm из value-noise с периодической решёткой (бикубическая интерполяция через zoom с wrap)."""
    r = np.random.default_rng(seed)
    acc = np.zeros((n, n)); amp = 1.0; tot = 0.0
    c = cells
    for o in range(octaves):
        g = r.random((c, c))
        g3 = np.tile(g, (3, 3))
        z = ndimage.zoom(g3, n / c, order=3, mode='grid-wrap', grid_mode=True)
        acc += amp * z[n:2 * n, n:2 * n]
        tot += amp; amp *= persistence; c *= 2
        if c > n: break
    return acc / tot

# --- плиты: точки Вороного с «кладкой» (ряды, сдвиг) + дрожание — неправильные, но не хаотичные
rows = 6
pts = []
for j in range(rows):
    cols = rng.integers(5, 7)
    off = rng.random()
    for i in range(cols):
        x = (i + off + rng.uniform(-0.28, 0.28)) / cols
        y = (j + 0.5 + rng.uniform(-0.25, 0.25)) / rows
        pts.append((x % 1.0, y % 1.0))
pts = np.array(pts)
# 9 копий на торе
P = np.concatenate([pts + np.array([dx, dy]) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
idx_base = np.tile(np.arange(len(pts)), 9)
tree = cKDTree(P)
yy, xx = np.mgrid[0:N, 0:N]
q = np.stack([(xx + 0.5) / N, (yy + 0.5) / N], -1).reshape(-1, 2)
# лёгкое искажение границ (камень тёсаный вручную)
warp = 0.012
wx = tile_noise(N, 8, 4, 0.5, 11).reshape(-1) - 0.5
wy = tile_noise(N, 8, 4, 0.5, 12).reshape(-1) - 0.5
qw = q + np.stack([wx, wy], -1) * warp * 2
d, ii = tree.query(qw, k=2)
cell = idx_base[ii[:, 0]].reshape(N, N)
# расстояние до границы ≈ (d2 − d1)/2 в долях текстуры → пиксели
edge = ((d[:, 1] - d[:, 0]) * 0.5 * N).reshape(N, N)

gap = 5.0      # полуширина шва, px
bevel = 14.0   # ширина фаски (у каждой плиты своя, рваная)
bev = bevel * (0.6 + 0.8 * tile_noise(N, 12, 3, 0.5, 7)) * rng.uniform(0.7, 1.3, len(pts))[cell]
h_stone = np.clip((edge - gap) / bev, 0, 1)
h_stone = h_stone * h_stone * (3 - 2 * h_stone)
ncell = len(pts)
tilt_x = rng.uniform(-1, 1, ncell)[cell]; tilt_y = rng.uniform(-1, 1, ncell)[cell]
lift = rng.uniform(-0.25, 0.25, ncell)[cell]
u = (xx / N); v = (yy / N)
detail = tile_noise(N, 16, 6, 0.55, 3)
fine = tile_noise(N, 64, 4, 0.5, 4)
# сколы по краям: где шум низкий — фаска глубже
chip = np.clip((tile_noise(N, 24, 4, 0.5, 5) - 0.42) * 6, 0, 1)
h_stone = h_stone * (0.55 + 0.45 * chip) + (1 - chip) * h_stone ** 3 * 0.45
# наклон плиты в её собственной системе (плиты уложены не идеально ровно)
loc = (qw - P[ii[:, 0]]).reshape(N, N, 2) * N   # px от центра плиты
tilt = (tilt_x * loc[..., 0] + tilt_y * loc[..., 1]) * 0.0012
pit = tile_noise(N, 192, 3, 0.55, 6)            # раковины и зерно гранита
pits = np.clip((0.35 - pit) * 6, 0, 1)
height = h_stone * (0.75 + 0.1 * lift + tilt) + 0.16 * (detail - 0.5) * h_stone + 0.06 * (fine - 0.5) + 0.03 * (pit - 0.5) - 0.04 * pits * h_stone
in_gap = 1 - np.clip((edge - gap * 0.6) / (gap * 0.8), 0, 1)

# --- цвет: тёмный мокрый гранит (линейные значения), плиты разного оттенка, крупные пятна, мелкое зерно
base_lum = rng.uniform(0.045, 0.115, ncell)[cell]
warm = rng.uniform(-1, 1, ncell)[cell]
mott = tile_noise(N, 6, 4, 0.5, 14)
mid = tile_noise(N, 20, 4, 0.5, 15)
grain = tile_noise(N, 256, 2, 0.5, 16)
lum = base_lum * (0.75 + 0.5 * mott) * (0.85 + 0.3 * mid) * (0.92 + 0.16 * grain)
albedo = np.stack([lum * (1.0 + 0.10 * warm), lum * (1.0 + 0.03 * warm), lum * (1.04 - 0.08 * warm)], -1)
# грязь у швов (темнее к краю), истёртый верх чуть светлее
dirt_band = np.clip(1 - (edge - gap) / 26.0, 0, 1) ** 2 * (0.6 + 0.4 * mid)
albedo *= (1 - 0.45 * dirt_band[..., None])
albedo *= (1 + 1.2 * np.clip(h_stone - 0.9, 0, 1) * (mott > 0.5))[..., None]
# швы: тёмная земля, местами мох
moss_m = np.clip((tile_noise(N, 10, 5, 0.55, 21) - 0.5) * 4, 0, 1)
dirt = np.array([0.030, 0.024, 0.018]); moss = np.array([0.028, 0.045, 0.012])
gapcol = dirt[None, None] * (1 - moss_m[..., None]) + moss[None, None] * moss_m[..., None]
gapcol *= (0.7 + 0.6 * tile_noise(N, 96, 2, 0.5, 23)[..., None])
albedo = albedo * (1 - in_gap[..., None]) + gapcol * in_gap[..., None]
moss_edge = np.clip(1 - (edge - gap) / 9, 0, 1) * moss_m * (tile_noise(N, 48, 3, 0.5, 22) > 0.55)
albedo = albedo * (1 - 0.7 * moss_edge[..., None]) + moss[None, None] * 0.7 * moss_edge[..., None]

# --- влажность: лужицы в понижениях + общий мокрый блеск (рельеф в лужах — мельче)
puddle_n = tile_noise(N, 5, 4, 0.5, 31)
wet = np.clip((puddle_n - 0.6) * 6 + (0.55 - height) * 0.8, 0, 1)
albedo *= (1 - 0.3 * wet[..., None])
smooth = 0.42 + 0.18 * (mid - 0.5)
smooth = smooth * (1 - in_gap * 0.6) + 0.5 * wet * (1 - in_gap * 0.4)
smooth = np.clip(smooth, 0.08, 0.92)
height = height - 0.6 * wet * 0.05 * (fine - 0.5)

albedo = np.clip(albedo, 0, 1)

def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx = -gx * strength; ny = gy * strength; nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l, ny / l, nz / l], -1)

hs = ndimage.gaussian_filter(height, 0.7, mode='wrap')
n = normal_from_height(hs, N / 64.0)
def save(a, name, mode='RGB'):
    Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8), mode).save(os.path.join(out, name))
# sRGB для альбедо (значения выше задуманы в линейном → в гамму)
save(albedo ** (1 / 2.2), 'flagstone_albedo.png')
save(n * 0.5 + 0.5, 'flagstone_normal.png')
ms = np.zeros((N, N, 4)); ms[..., 3] = smooth
save(ms, 'flagstone_ms.png', 'RGBA')
save(np.stack([hs] * 3, -1), 'flagstone_height_preview.png')
print('ok', N, len(pts))
