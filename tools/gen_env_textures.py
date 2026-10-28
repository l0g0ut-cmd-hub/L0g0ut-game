"""Текстуры окружения Blade Path (храм, двор, деревья). Запуск: python3 tools/gen_env_textures.py
Использует помощники из gen_textures.py. Все карты бесшовные, 512x512 (шодзи — 256x320, не тайлится)."""
import os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
from gen_textures import pnoise, normal_from_height, save, smoothstep, rng
from scipy.ndimage import gaussian_filter

N = 512


def wood():
    """Тёмные вертикальные доски с волокном и щелями."""
    planks = 8
    pw = N // planks
    h = np.zeros((N, N)); alb = np.zeros((N, N, 3))
    for p in range(planks):
        x0 = p * pw
        base = np.array([66, 42, 30]) * rng.uniform(0.82, 1.15)
        grain = pnoise(N, 2.2, seed=100 + p, aniso=(9, 1))
        grain2 = pnoise(N, 7, seed=140 + p, aniso=(14, 1))
        for x in range(pw):
            xs = x0 + x
            edge = min(x, pw - 1 - x)
            gap = smoothstep(edge / 4.0)
            g = 0.55 + 0.75 * grain[:, xs] * 0.6 + 0.4 * grain2[:, xs] * 0.5
            alb[:, xs] = base[None, :] * g[:, None] * (0.35 + 0.65 * gap)
            h[:, xs] = (0.5 + 0.35 * grain[:, xs] * 0.5) * (0.3 + 0.7 * gap)
    weather = pnoise(N, 22, seed=160)
    alb *= (0.8 + 0.35 * weather)[..., None]
    save('wood_albedo.png', alb); save('wood_normal.png', normal_from_height(h, 3.0))


def roof():
    """Чёрно-синяя черепица кавара: столбики-полукруги в рядах со смещением."""
    cols, rows = 16, 8
    cw, rh = N // cols, N // rows
    ys, xs = np.mgrid[0:N, 0:N]
    row = ys // rh; fy = (ys % rh) / rh
    xoff = (row % 2) * (cw / 2)
    cx = ((xs + xoff) % cw) / cw
    prof = np.sin(np.pi * cx)                                     # выпуклость черепицы
    slope = smoothstep(fy * 1.3)                                  # нижний край налезает на следующий ряд
    h = 0.25 + 0.55 * prof * (0.4 + 0.6 * slope) - 0.25 * (1 - smoothstep(fy * 6))
    n = pnoise(N, 3, seed=170)
    tone = 0.55 + 0.6 * h + 0.25 * n
    alb = np.stack([tone * 52, tone * 60, tone * 74], -1)
    moss = smoothstep((pnoise(N, 14, seed=171) - 0.66) * 7)
    alb = alb * (1 - 0.5 * moss[..., None]) + np.array([30, 48, 28]) * 0.5 * moss[..., None]
    save('roof_albedo.png', alb); save('roof_normal.png', normal_from_height(h + 0.05 * n, 3.5))


def plaster():
    n1 = pnoise(N, 4, seed=180); n2 = pnoise(N, 14, seed=181); n3 = pnoise(N, 1.5, seed=182)
    tone = 0.72 + 0.28 * n2 + 0.12 * (n3 - 0.5)
    stain = smoothstep((pnoise(N, 30, seed=183) - 0.55) * 4)
    tone *= 1 - 0.35 * stain
    alb = np.stack([tone * 118, tone * 112, tone * 100], -1)
    save('plaster_albedo.png', alb); save('plaster_normal.png', normal_from_height(0.5 * n1 + 0.5 * n3, 1.6))


def stoneblock():
    """Каменная кладка со смещением рядов и раствором."""
    rows, cols = 6, 3
    rh, cw = N // rows, N // cols
    h = np.zeros((N, N)); alb = np.zeros((N, N, 3))
    for r in range(rows):
        for c in range(cols):
            y0 = r * rh; x0 = (c * cw + (cw // 2 if r % 2 else 0))
            ys, xs = np.mgrid[0:rh, 0:cw]
            edge = np.minimum.reduce([xs, cw - 1 - xs, ys, rh - 1 - ys]).astype(float)
            bevel = smoothstep(edge / 7.0)
            nz = pnoise(max(rh, cw), 4, seed=190 + r * 5 + c)[:rh, :cw]
            tone = (58 + rng.normal(0, 6) + 22 * (nz - 0.5)) * (0.45 + 0.55 * bevel)
            blk = np.stack([tone * 0.97, tone * 0.99, tone * 1.06], -1)
            hb = 0.3 + 0.7 * bevel + 0.1 * nz
            for dy in range(rh):
                for dx in range(cw):
                    yy = (y0 + dy) % N; xx = (x0 + dx) % N
                    alb[yy, xx] = blk[dy, dx]; h[yy, xx] = hb[dy, dx]
    wet = pnoise(N, 24, seed=199)
    alb *= (0.8 + 0.35 * wet)[..., None]
    moss = smoothstep((pnoise(N, 12, seed=198) - 0.64) * 7)
    alb = alb * (1 - 0.45 * moss[..., None]) + np.array([26, 44, 24]) * 0.45 * moss[..., None]
    save('stoneblock_albedo.png', alb); save('stoneblock_normal.png', normal_from_height(h, 4.0))


def gravel():
    """Тёмный гравий с граблями дзен-сада (параллельные борозды)."""
    n = pnoise(N, 1.0, seed=210); big = pnoise(N, 5, seed=211)
    ys = np.arange(N)[:, None]
    rake = 0.5 + 0.5 * np.sin(ys * 2 * np.pi / 16.0)
    rake = np.repeat(rake, N, 1)
    rake_mask = smoothstep((pnoise(N, 40, seed=212) - 0.3) * 4)    # борозды только на части площади
    h = 0.55 * n + 0.15 * big + 0.35 * rake * rake_mask
    tone = 0.55 + 0.7 * n + 0.2 * big
    alb = np.stack([tone * 48, tone * 46, tone * 50], -1)
    save('gravel_albedo.png', alb); save('gravel_normal.png', normal_from_height(h, 4.5))


def bark():
    g = pnoise(N, 2.5, seed=220, aniso=(10, 1)); g2 = pnoise(N, 9, seed=221, aniso=(6, 1))
    h = 0.6 * g + 0.4 * g2
    ridge = np.abs(g - 0.5) * 2
    tone = 0.4 + 0.9 * ridge
    alb = np.stack([tone * 52, tone * 40, tone * 36], -1)
    save('bark_albedo.png', alb); save('bark_normal.png', normal_from_height(h, 5.0))


def blossom():
    """Кроны сакуры: кластеры цветков розового оттенка."""
    n1 = pnoise(N, 2.0, seed=230); n2 = pnoise(N, 6, seed=231); n3 = pnoise(N, 1.0, seed=232)
    clump = smoothstep((n1 - 0.45) * 3.0)
    base = np.array([226, 140, 168]); light = np.array([250, 205, 218]); dark = np.array([150, 72, 104])
    alb = dark[None, None] * (1 - clump[..., None]) + base[None, None] * clump[..., None]
    alb = alb * (0.8 + 0.4 * n2[..., None]) + (light - base)[None, None] * (smoothstep((n3 - 0.72) * 6))[..., None] * 0.6
    save('blossom_albedo.png', alb)


def shoji():
    """Бумажная ширма с решёткой; эмиссия — тёплый свет изнутри там, где бумага."""
    W, H = 256, 320
    ys, xs = np.mgrid[0:H, 0:W]
    paper = np.stack([np.full((H, W), 236.), np.full((H, W), 214.), np.full((H, W), 170.)], -1)
    fib = gaussian_filter(rng.normal(size=(H, W)), (0.6, 2.5))
    fib = (fib - fib.min()) / (np.ptp(fib) + 1e-9)
    paper *= (0.88 + 0.18 * fib)[..., None]
    lat = np.zeros((H, W))
    cw, ch = W // 3, H // 4
    th = 5
    lat[(xs % cw) < th] = 1; lat[(ys % ch) < th] = 1
    lat[:th + 3, :] = 1; lat[-th - 3:, :] = 1; lat[:, :th + 3] = 1; lat[:, -th - 3:] = 1
    lat = gaussian_filter(lat, 0.7)
    wood_c = np.array([40, 26, 20])
    alb = paper * (1 - lat[..., None]) + wood_c[None, None] * lat[..., None]
    emi = np.array([255, 160, 70])[None, None] * (1 - lat[..., None]) * (0.7 + 0.5 * fib[..., None])
    save('shoji_albedo.png', alb); save('shoji_emission.png', emi)


if __name__ == '__main__':
    for f in (wood, roof, plaster, stoneblock, gravel, bark, blossom, shoji):
        f(); print('ok', f.__name__)
