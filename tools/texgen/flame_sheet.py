"""Флипбук пламени 4×4 (512×512, кадр 128×128) для частиц жаровен, v2: в кадре — 2–3 тонких рваных языка разной
высоты (не одна толстая капля), шум «плывёт» вверх от кадра к кадру, мелкий шум выедает дыры и рвёт кончики — сверху
пламя распадается на лоскуты. Цвет: маленькое бело-жёлтое ядро у основания → оранжевый → тёмно-красный край и кончики.
RGB — цвет, A — плотность (аддитивные частицы). Кадр 0 — язык рождается, 15 — гаснет и отрывается.
Запуск: python3 flame_sheet.py OUTDIR"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
F, N = 16, 128
rng = np.random.default_rng(4)
def vol(sy, sx, s):
    return ndimage.zoom(rng.random((sy, sx)), (N * 3 / sy, N / sx), order=3, mode='grid-wrap')
noise = vol(60, 24, 0); fine = ndimage.gaussian_filter(vol(54, 30, 1), (2.0, 0.6))   # вытянут по вертикали — струи, а не сыр
yy, xx = np.mgrid[0:N, 0:N] / N
sheet = np.zeros((N * 4, N * 4, 4))
ramp_pos = np.array([0.0, 0.2, 0.45, 0.72, 0.9, 1.0])
ramp_col = np.array([[0.3, 0.02, 0.0], [0.7, 0.12, 0.02], [1.0, 0.36, 0.06], [1.0, 0.62, 0.18], [1.0, 0.85, 0.5], [1.0, 0.96, 0.82]])
licks = [(-0.07, 1.0, 0.11, 0.0), (0.08, 0.72, 0.085, 1.7), (0.0, 0.5, 0.07, 3.1)]   # смещение x, высота, ширина, фаза
for f in range(F):
    u = f / (F - 1)
    off = int(f * N * 2 / F)
    n = ndimage.gaussian_filter(noise[off:off + N][::-1], 2)
    fn = fine[off * 2 % (N * 2):off * 2 % (N * 2) + N][::-1]
    h = 1 - yy
    grow = min(1.0, u / 0.22)
    lift = 0.3 * max(0.0, (u - 0.6) / 0.4)
    fade = 1 - max(0.0, (u - 0.72) / 0.28)
    dens = np.zeros((N, N))
    for (ox, ht, wd, ph) in licks:
        top = (0.2 + 0.65 * grow) * ht * (1 - 0.3 * max(0.0, (u - 0.7) / 0.3))
        hh = (h - lift) / max(0.05, top)
        width = wd * np.clip(1 - hh, 0, 1) ** 0.9 + 0.012
        sway = (n - 0.5) * 0.36 * np.clip(hh, 0, 1) ** 1.2 + 0.05 * np.sin(f * 0.7 + ph + hh * 5)
        dx = (xx - 0.5 - ox * (1 - 0.4 * hh)) - sway
        edge = np.clip(1 - np.abs(dx) / width, 0, 1)
        d = edge ** 0.7 * (hh > -0.08) * (hh < 1)
        d *= np.clip(1 - (hh - 0.5) / 0.5, 0, 1) ** 1.1 * np.clip((hh + 0.08) / 0.14, 0, 1)
        dens = np.maximum(dens, d)
    # мелкий шум рвёт пламя: у основания плотное, к верху — лоскуты и дыры
    hk = np.clip(h / 0.85, 0, 1)
    erode = np.clip((fn - (0.12 + 0.42 * hk)) / 0.3 + 0.7 * (1 - hk) ** 2, 0, 1)
    dens = dens * erode * fade
    dens = ndimage.gaussian_filter(np.clip(dens, 0, 1), 1.1)
    t = np.clip(dens * (1.25 - 0.95 * np.clip(h - lift, 0, 1)) * 1.05, 0, 1)   # горячее у основания
    col = np.stack([np.interp(t, ramp_pos, ramp_col[:, k]) for k in range(3)], -1)
    fy, fx = divmod(f, 4)
    sheet[fy * N:(fy + 1) * N, fx * N:(fx + 1) * N, :3] = col
    sheet[fy * N:(fy + 1) * N, fx * N:(fx + 1) * N, 3] = dens * 0.9
Image.fromarray(np.clip(sheet * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(out, 'flame_sheet.png'))
print('ok')
