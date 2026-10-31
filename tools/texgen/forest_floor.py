"""Бесшовная лесная подстилка под криптомериями за стенами храма: тёмная земля, подушки мха (крупные пятна с мягкой кромкой
и «ворсом»), рыжая опавшая хвоя (тонкие штрихи по всем направлениям), редкие камешки и корешки. Ровнее и темнее гравия двора —
чтобы за стеной читался лес, а не бетонная площадка. Выход: albedo (sRGB), normal (OpenGL +Y).
Запуск: python3 forest_floor.py OUTDIR [size]"""
import sys, os
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

out = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 1024
rng = np.random.default_rng(23)
os.makedirs(out, exist_ok=True)


def tile_noise(n, cells, octaves=5, persistence=0.5, seed=0):
    r = np.random.default_rng(seed)
    acc = np.zeros((n, n)); amp = 1.0; tot = 0.0
    c = cells
    for o in range(octaves):
        g = r.random((c, c))
        z = ndimage.zoom(np.tile(g, (3, 3)), n / c, order=3, mode='grid-wrap', grid_mode=True)
        acc += amp * z[n:2 * n, n:2 * n]
        tot += amp; amp *= persistence; c *= 2
        if c > n: break
    return acc / tot


# --- земля: тёмно-бурая, пятнами светлее/темнее
soil_n = tile_noise(N, 6, 6, 0.55, 1)
soil = np.array([0.07, 0.052, 0.036])[None, None] * (0.7 + 0.7 * soil_n[..., None]) * (0.85 + 0.3 * tile_noise(N, 40, 3, 0.5, 9)[..., None])
height = soil_n * 0.25

# --- мох: крупные подушки (порог по fBm) с рваной кромкой, внутри — ворс (мелкий шум) и оттенки
moss_n = tile_noise(N, 5, 6, 0.55, 2) + (tile_noise(N, 24, 4, 0.5, 3) - 0.5) * 0.18
moss = np.clip((moss_n - 0.5) / 0.1, 0, 1)
moss = moss * moss * (3 - 2 * moss)
fuzz = tile_noise(N, 128, 3, 0.6, 4)
tone = tile_noise(N, 10, 4, 0.5, 5)
moss_dark = np.array([0.035, 0.055, 0.022]); moss_lite = np.array([0.085, 0.115, 0.04])
moss_col = moss_dark[None, None] * (1 - tone[..., None]) + moss_lite[None, None] * tone[..., None]
moss_col = moss_col * (0.7 + 0.6 * fuzz[..., None])
height += moss * (0.35 + 0.15 * fuzz)

albedo = soil * (1 - moss[..., None]) + moss_col * moss[..., None]

# --- опавшая хвоя: тонкие рыжевато-бурые штрихи (рисуем в 3× холсте и берём центр — бесшовно), гуще там, где нет мха
S = N * 3
needles = Image.new('L', (S, S), 0)
ndraw = ImageDraw.Draw(needles)
hue = Image.new('L', (S, S), 0)
hdraw = ImageDraw.Draw(hue)
count = int(11000 * (N / 1024) ** 2)
for i in range(count):
    x = rng.random() * N; y = rng.random() * N
    if moss[int(y) % N, int(x) % N] > 0.6 and rng.random() < 0.85: continue
    a = rng.random() * np.pi
    L = rng.uniform(10, 26) * N / 1024
    dx = np.cos(a) * L * 0.5; dy = np.sin(a) * L * 0.5
    w = max(1, int(round(rng.uniform(1.2, 2.4) * N / 1024)))
    v = int(rng.uniform(120, 255)); h = int(rng.uniform(0, 255))
    for ox in (0, N, 2 * N):
        for oy in (0, N, 2 * N):
            ndraw.line([(x + ox - dx, y + oy - dy), (x + ox + dx, y + oy + dy)], fill=v, width=w)
            hdraw.line([(x + ox - dx, y + oy - dy), (x + ox + dx, y + oy + dy)], fill=h, width=w)
nd = np.asarray(needles, dtype=np.float32)[N:2 * N, N:2 * N] / 255.0
nh = np.asarray(hue, dtype=np.float32)[N:2 * N, N:2 * N] / 255.0
needle_col = np.array([0.20, 0.10, 0.045])[None, None] * (1 - nh[..., None]) + np.array([0.12, 0.075, 0.04])[None, None] * nh[..., None]
nk = np.clip(nd * 1.2, 0, 1)
albedo = albedo * (1 - nk[..., None]) + needle_col * nk[..., None]
height += nd * 0.12

# --- камешки: редкие округлые, серые, с тенью у основания
yy, xx = np.mgrid[0:N, 0:N]
for i in range(int(26 * (N / 1024) ** 2)):
    cx = rng.random() * N; cy = rng.random() * N
    r = rng.uniform(7, 18) * N / 1024
    ddx = (xx - cx + N / 2) % N - N / 2; ddy = (yy - cy + N / 2) % N - N / 2
    d = np.sqrt((ddx / rng.uniform(0.7, 1.3)) ** 2 + ddy ** 2) / r
    m = np.clip(1 - d, 0, 1)
    if m.max() <= 0: continue
    dome = np.sqrt(np.clip(1 - d * d, 0, 1))
    g = rng.uniform(0.09, 0.16)
    sel = d < 1
    albedo[sel] = albedo[sel] * 0.15 + np.array([g, g * 0.98, g * 0.94]) * (0.8 + 0.3 * dome[sel, None]) * 0.85
    height = np.maximum(height, dome * 0.55 * (d < 1))
    shadow = np.clip(1 - (d - 1) / 0.5, 0, 1) * (d >= 1)
    albedo *= (1 - 0.35 * shadow)[..., None]

albedo = np.clip(albedo, 0, 1)
hs = ndimage.gaussian_filter(height, 0.8, mode='wrap')
gx = (np.roll(hs, -1, 1) - np.roll(hs, 1, 1)) * 0.5
gy = (np.roll(hs, -1, 0) - np.roll(hs, 1, 0)) * 0.5
s = N / 60.0
nx = -gx * s; ny = gy * s; nz = np.ones_like(hs)
l = np.sqrt(nx * nx + ny * ny + nz * nz)
nrm = np.stack([nx / l, ny / l, nz / l], -1)


def save(a, name):
    Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(out, name))


save(albedo ** (1 / 2.2), 'forest_floor_albedo.png')
save(nrm * 0.5 + 0.5, 'forest_floor_normal.png')
print('ok')
