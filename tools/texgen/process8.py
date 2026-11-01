"""Атлас 8 — детали окружения и живность (пурпурный фон).
usage: cd tools/texgen && python3 process8.py"""
import numpy as np
from PIL import Image
from scipy import ndimage
R = '../../Assets/_Project/'
TX, FX = R + 'Art/Textures/', R + 'Resources/FX/'
E = [(6, 310), (318, 622), (630, 934), (942, 1246)]
src = np.asarray(Image.open('atlas_src/atlas8_env.png').convert('RGB')).astype(np.float32) / 255
C = {}
for r, (y0, y1) in enumerate(E):
    for c, (x0, x1) in enumerate(E):
        C[r * 4 + c + 1] = src[y0 + 3:y1 - 3, x0 + 3:x1 - 3]

def key(c, lo=0.08, hi=0.86):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    a = np.clip((hi - m) / (hi - lo), 0, 1)
    rgb = np.clip((c - (1 - a[..., None]) * np.array([1, 0, 1])) / np.maximum(a[..., None], 1e-3), 0, 1)
    lim = np.minimum(rgb[..., 0], rgb[..., 2]); spill = np.clip(lim - rgb[..., 1] - 0.1, 0, 1)
    rgb[..., 0] -= spill * 0.85; rgb[..., 2] -= spill * 0.85
    return np.clip(rgb, 0, 1), a

def rgba(rgb, a): return np.dstack([rgb, a])
def img(o): return Image.fromarray((np.clip(o, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')
def bbox(o, pad=4, thr=0.04):
    ys, xs = np.nonzero(o[..., 3] > thr)
    return o[max(0, ys.min() - pad):ys.max() + pad + 1, max(0, xs.min() - pad):xs.max() + pad + 1]
def fit(o, W, H, anchor='c'):
    """Вписать по пропорциям в W×H; anchor: c — центр, t — к верху, b — к низу, tl — в левый верхний угол."""
    im = img(o); w, h = im.size; s = min(W / w, H / h)
    im = im.resize((max(1, int(w * s)), max(1, int(h * s))), Image.LANCZOS)
    out = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    x = (W - im.size[0]) // 2; y = (H - im.size[1]) // 2
    if anchor == 't': y = 0
    if anchor == 'b': y = H - im.size[1]
    if anchor == 'tl': x, y = 0, 0
    out.paste(im, (x, y)); return out

# 1 потёки на стене → маска умножения (белое — без изменений), низ растушёван
rgb, a = key(C[1], 0.02, 0.9)
lum = rgb.mean(-1)
mask = 1 - a * np.clip(1.05 - lum, 0, 1) * 0.95
h = mask.shape[0]; fade = np.clip((np.arange(h)[:, None] / h - 0.0) / 0.05, 0, 1) * np.clip((1 - np.arange(h)[:, None] / h) / 0.25, 0, 1)
mask = 1 - (1 - mask) * fade
Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8)).resize((512, 512), Image.LANCZOS).convert('RGB').save(TX + 'wall_stains.png')
# 2 мох у основания стены: полоса, бесшовно по ширине
o = rgba(*key(C[2])); o = o[int(o.shape[0] * 0.55):]   # нижняя часть ячейки
w = o.shape[1]; roll = np.roll(o, w // 2, 1); x = np.arange(w) / (w - 1); m = np.clip(np.minimum(x, 1 - x) / 0.2, 0, 1)[None, :, None]
o = o * m + roll * (1 - m)
img(o).resize((512, 128), Image.LANCZOS).save(TX + 'moss_strip.png')
# 3 плющ, 4 паутина (угол — левый верх), 15 офуда, 16 цветы
fit(bbox(rgba(*key(C[3]))), 256, 512, 't').save(TX + 'ivy.png')
rgb, a = key(C[4], 0.05, 0.8); g = np.repeat(rgb.mean(-1, keepdims=True), 3, -1) * 0.95 + 0.05   # нити — серо-белые
fit(np.dstack([g, a]), 512, 512, 'tl').save(TX + 'cobweb.png')
fit(bbox(rgba(*key(C[15]))), 128, 512).save(TX + 'ofuda.png')
rgb, a = key(C[16])
purp = np.clip(np.minimum(rgb[..., 0], rgb[..., 2]) - rgb[..., 1], 0, 1) * (1 - a)   # пурпур в тонких стеблях → зелень
rgb[..., 0] -= purp; rgb[..., 2] -= purp; rgb[..., 1] += purp * 0.3
fit(bbox(np.dstack([np.clip(rgb, 0, 1), a])), 512, 512, 'b').save(TX + 'wildflowers.png')
# вороны: 4 кадра взмаха (вправо) → 1024×256; сидящая; лист — одинаковый масштаб для всех кадров полёта
frames = [rgba(*key(C[k])) for k in (5, 6, 7, 8)]
sheet = Image.new('RGBA', (1024, 256), (0, 0, 0, 0))
for i, f in enumerate(frames): sheet.paste(img(f).resize((256, 256), Image.LANCZOS), (i * 256, 0))
sheet.save(FX + 'crow_flap.png')
fit(rgba(*key(C[9])), 256, 256).save(FX + 'crow_perch.png')
# бабочка: раскрытые / сложенные — 512×256 (одинаковый масштаб: из ячеек целиком)
bs = Image.new('RGBA', (512, 256), (0, 0, 0, 0))
for i, k in enumerate((10, 11)): bs.paste(img(rgba(*key(C[k]))).resize((256, 256), Image.LANCZOS), (i * 256, 0))
bs.save(FX + 'butterfly.png')
# стрекоза: прозрачные крылья (полупрозрачная альфа), тело вертикально
rgb, a = key(C[12], 0.08, 0.75)
wing = (a < 0.85)[..., None]; g = np.repeat(rgb.mean(-1, keepdims=True), 3, -1)
rgb = np.where(wing, g * 0.8 + 0.2, rgb)   # прозрачные крылья — дымчато-серые
fit(bbox(np.dstack([rgb, a * np.where(a < 0.85, 0.7, 1.0)])), 256, 256).rotate(-90, expand=True).save(FX + 'dragonfly.png')   # голова вправо, как у процедурной
# 13 след гэта: белая маска (тон в игре), 14 лужа: белое + мягкая альфа
rgb, a = key(C[13]); a = a * np.clip(1.1 - rgb.mean(-1), 0, 1)
fit(bbox(np.dstack([np.ones_like(rgb), a])), 128, 256).save(FX + 'geta_print.png')
rgb, a = key(C[14]); a = ndimage.gaussian_filter(a, 2.0)
fit(bbox(np.dstack([np.ones_like(rgb), a])), 256, 256).save(TX + 'puddle_mask.png')
print('ok')
