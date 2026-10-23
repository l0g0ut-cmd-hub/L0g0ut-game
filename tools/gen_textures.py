"""Процедурные текстуры для Blade Path. Запуск: python3 tools/gen_textures.py
Палитра взята с концепт-арта: чёрный лак, тёмно-красная шнуровка, бронза, вымокший камень, плоть врага."""
import os
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Project', 'Art', 'Textures')
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(2026)


def pnoise(n, sigma, seed=None, aniso=(1, 1)):
    """Бесшовный (периодический) шум 0..1."""
    r = np.random.default_rng(seed) if seed is not None else rng
    a = gaussian_filter(r.normal(size=(n, n)), (sigma * aniso[0], sigma * aniso[1]), mode='wrap')
    a = (a - a.min()) / (a.max() - a.min() + 1e-9)
    return a


def normal_from_height(h, strength=3.0):
    dx = np.roll(h, -1, 1) - np.roll(h, 1, 1)
    dy = np.roll(h, -1, 0) - np.roll(h, 1, 0)
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / ln, ny / ln, nz / ln], -1)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)


def save(name, arr):
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).save(os.path.join(OUT, name))


def smoothstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


# ---------------------------------------------------------------- броня: пластинчатая (кодзанэ) с красной шнуровкой
def armor():
    N, cols, rows = 512, 16, 16
    cw, rh = N // cols, N // rows
    alb = np.zeros((N, N, 3))
    h = np.zeros((N, N))
    alb[:] = (14, 12, 15)
    for r in range(rows):
        for c in range(cols):
            x0, y0 = c * cw, r * rh
            base = np.array([30, 28, 34]) + rng.normal(0, 3, 3)
            ph, pw = rh - 3, cw - 6
            grad = np.linspace(1.45, 0.78, ph)[:, None, None]
            plate = base[None, None, :] * grad * np.ones((ph, pw, 1))
            # блик по верхнему ребру и лёгкий скос по краям
            plate[0:2] += 14
            plate[:, 0:2] += 6
            ys, xs = np.mgrid[0:ph, 0:pw]
            edge = np.minimum.reduce([xs, pw - 1 - xs, ys, ph - 1 - ys]).astype(float)
            hh = smoothstep(edge / 4.0)
            alb[y0 + 2:y0 + 2 + ph, x0 + 3:x0 + 3 + pw] = plate
            h[y0 + 2:y0 + 2 + ph, x0 + 3:x0 + 3 + pw] = 0.55 + 0.45 * hh
    # вертикальная красная шнуровка по границам колонок
    for c in range(cols):
        xc = c * cw
        for dx in range(-2, 3):
            x = (xc + dx) % N
            wgt = 1.0 - abs(dx) / 3.0
            col = np.array([128, 22, 28]) * (0.75 + 0.5 * pnoise(N, 2, seed=c + 100)[:, 0])[:, None] * wgt
            alb[:, x] = alb[:, x] * (1 - wgt) + col * wgt + (0 if dx else 0)
            h[:, x] = np.maximum(h[:, x], 0.62 * wgt)
    # горизонтальные красные «косы» через каждые 4 ряда
    for r in range(0, rows, 4):
        y0 = r * rh
        for dy in range(0, 4):
            y = (y0 + dy) % N
            stripe = 0.8 + 0.3 * np.sin(np.arange(N) * 0.9 + dy)
            alb[y] = alb[y] * 0.25 + np.array([112, 18, 24])[None, :] * stripe[:, None] * 0.75
            h[y] = np.maximum(h[y], 0.66)
    # износ: царапины и следы бронзы
    scr = pnoise(N, 1.2, seed=7)
    alb += ((scr > 0.78) * 38)[..., None]
    wear = pnoise(N, 3, seed=8)
    alb[..., 0] += (wear > 0.86) * 36
    alb[..., 1] += (wear > 0.86) * 24
    # потёки крови
    blood = smoothstep((gaussian_filter(pnoise(N, 14, seed=9), 2, mode='wrap') - 0.62) * 6)
    alb = alb * (1 - 0.55 * blood[..., None]) + np.array([76, 8, 12]) * 0.55 * blood[..., None]
    save('armor_albedo.png', alb)
    save('armor_normal.png', normal_from_height(h, 5.0))
    return alb


# ---------------------------------------------------------------- ткань (твилл)
def cloth():
    N = 256
    ys, xs = np.mgrid[0:N, 0:N]
    twill = ((xs + ys) % 8 < 4).astype(float)
    fine = pnoise(N, 0.8, seed=21)
    h = 0.5 * twill + 0.25 * fine
    base = np.array([42, 38, 40])
    alb = base[None, None, :] * (0.78 + 0.35 * twill + 0.25 * fine)[..., None]
    mottle = pnoise(N, 9, seed=22)
    alb *= (0.85 + 0.3 * mottle)[..., None]
    save('cloth_albedo.png', alb)
    save('cloth_normal.png', normal_from_height(h, 2.5))


# ---------------------------------------------------------------- сталь клинка
def steel():
    N = 256
    streak = pnoise(N, 1.0, seed=31, aniso=(0.15, 6))   # волокна вдоль U
    base = 150 + 70 * streak
    alb = np.stack([base, base * 1.01, base * 1.04], -1)
    v = np.linspace(0, 1, N)[:, None]
    # линия хамона у кромки и тёмная спинка
    hamon = np.exp(-((v - 0.78) / 0.035) ** 2)
    alb += (hamon * 55)[..., None]
    alb *= (1.0 - 0.38 * smoothstep((0.3 - v) / 0.3))[..., None]
    save('steel_albedo.png', alb)


# ---------------------------------------------------------------- мокрый камень арены
def stone():
    N, tiles = 512, 4
    ts = N // tiles
    alb = np.zeros((N, N, 3))
    h = np.zeros((N, N))
    for ty in range(tiles):
        for tx in range(tiles):
            b = 62 + rng.normal(0, 7)
            y0, x0 = ty * ts, tx * ts
            ys, xs = np.mgrid[0:ts, 0:ts]
            edge = np.minimum.reduce([xs, ts - 1 - xs, ys, ts - 1 - ys]).astype(float)
            bevel = smoothstep(edge / 9.0)
            n = pnoise(ts, 5, seed=tx * 10 + ty + 50)
            tile = (b + 20 * (n - 0.5)) * (0.55 + 0.45 * bevel)
            alb[y0:y0 + ts, x0:x0 + ts] = np.stack([tile * 0.96, tile * 0.98, tile * 1.06], -1)
            h[y0:y0 + ts, x0:x0 + ts] = 0.35 + 0.65 * bevel + 0.1 * n
    cracks = pnoise(N, 2.2, seed=61)
    ridge = 1 - np.abs(cracks - 0.5) * 2
    crack = (ridge > 0.965).astype(float)
    alb *= (1 - 0.6 * crack)[..., None]
    h -= 0.4 * crack
    wet = pnoise(N, 24, seed=62)
    alb *= (0.82 + 0.3 * wet)[..., None]
    blood = smoothstep((pnoise(N, 30, seed=63) - 0.72) * 8)
    alb = alb * (1 - 0.5 * blood[..., None]) + np.array([60, 8, 10]) * 0.5 * blood[..., None]
    save('stone_albedo.png', alb)
    save('stone_normal.png', normal_from_height(h, 4.0))


# ---------------------------------------------------------------- красный лак (ворота, подставки)
def lacquer():
    N = 256
    grain = pnoise(N, 1.0, seed=71, aniso=(10, 0.5))
    base = np.array([108, 20, 24])
    alb = base[None, None, :] * (0.65 + 0.7 * grain)[..., None]
    alb *= (0.9 + 0.2 * pnoise(N, 12, seed=72))[..., None]
    save('lacquer_albedo.png', alb)


# ---------------------------------------------------------------- плоть щупальца
def flesh():
    N = 256
    base = pnoise(N, 4, seed=81)
    ridge = 1 - np.abs(pnoise(N, 2.5, seed=82) - 0.5) * 2
    veins = smoothstep((ridge - 0.9) * 14)
    bumps = pnoise(N, 1.4, seed=83)
    h = 0.5 * base + 0.35 * bumps + 0.4 * veins
    alb = np.zeros((N, N, 3))
    alb[..., 0] = 78 + 40 * base + 40 * veins
    alb[..., 1] = 20 + 12 * base + 10 * veins
    alb[..., 2] = 38 + 22 * base + 18 * veins
    save('flesh_albedo.png', alb)
    save('flesh_normal.png', normal_from_height(h, 3.5))


if __name__ == '__main__':
    armor(); cloth(); steel(); stone(); lacquer(); flesh()
    print('готово:', sorted(os.listdir(OUT)))
