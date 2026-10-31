"""Флипбук пламени 4×4 (512×512, кадр 128×128) для частиц жаровен: язык огня — капля, сужающаяся кверху, рваный
восходящим шумом (шум «плывёт» вверх от кадра к кадру — пламя живое), белое ядро у основания → жёлтый → оранжевый →
тёмно-красный край. RGB — цвет, A — плотность (аддитивные частицы). Кадр 0 — язык рождается, 15 — гаснет и отрывается.
Запуск: python3 flame_sheet.py OUTDIR"""
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
F, N = 16, 128
rng = np.random.default_rng(4)
# объём шума (y растянут — по нему «едем» вверх), бесшовный по x
base = rng.random((96, 48))
noise = ndimage.zoom(base, (N * 3 / 96, N / 48), order=3, mode='grid-wrap')
fine = ndimage.zoom(rng.random((192, 96)), (N * 3 / 192, N / 96), order=3, mode='grid-wrap')
yy, xx = np.mgrid[0:N, 0:N] / N
sheet = np.zeros((N * 4, N * 4, 4))
ramp_pos = np.array([0.0, 0.25, 0.5, 0.75, 1.0])
ramp_col = np.array([[0.35, 0.03, 0.01], [0.8, 0.18, 0.03], [1.0, 0.5, 0.1], [1.0, 0.82, 0.38], [1.0, 0.97, 0.85]])
for f in range(F):
    u = f / (F - 1)
    off = int(f * N * 2 / F)                            # шум поднимается
    n = ndimage.gaussian_filter(noise[off:off + N][::-1], 3)
    h = 1 - yy                                          # 0 внизу … 1 вверху (строка 0 — верх картинки)
    grow = min(1.0, u / 0.25)                           # язык вырастает
    top = 0.25 + 0.6 * grow - 0.25 * max(0.0, (u - 0.7) / 0.3)   # к концу оседает
    lift = 0.35 * max(0.0, (u - 0.55) / 0.45)           # и отрывается от основания
    hh = (h - lift) / max(0.05, top)
    # язык: широкое основание → острый кончик (профиль sqrt), края мягкие; изгиб — плавным шумом, сильнее к кончику
    width = 0.24 * np.clip(1 - hh, 0, 1) ** 0.85 + 0.02
    sway = (n - 0.5) * 0.32 * np.clip(hh, 0, 1) ** 1.3 + 0.04 * np.sin(f * 0.8 + hh * 4)
    dx = (xx - 0.5) - sway
    edge = np.clip(1 - np.abs(dx) / width, 0, 1)
    shape = edge ** 0.6 * (hh > -0.1) * (hh < 1)
    dens = shape * np.clip(0.85 + 0.35 * (n - 0.5), 0, 1.2)
    dens *= np.clip(1 - (hh - 0.55) / 0.45, 0, 1) ** 1.2 * np.clip((hh + 0.1) / 0.18, 0, 1)
    fade = 1 - max(0.0, (u - 0.75) / 0.25)
    dens = np.clip(dens * fade, 0, 1)
    dens = ndimage.gaussian_filter(dens, 1.6)
    t = np.clip(dens * (1.35 - 0.7 * hh) * (0.6 + 0.4 * edge), 0, 1)          # горячее у основания и в сердцевине
    col = np.stack([np.interp(t, ramp_pos, ramp_col[:, k]) for k in range(3)], -1)
    fy, fx = divmod(f, 4)
    sheet[fy * N:(fy + 1) * N, fx * N:(fx + 1) * N, :3] = col
    sheet[fy * N:(fy + 1) * N, fx * N:(fx + 1) * N, 3] = dens
Image.fromarray(np.clip(sheet * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(out, 'flame_sheet.png'))
print('ok')
