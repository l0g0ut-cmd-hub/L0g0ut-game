"""Атлас 7 — тушь на зелёном (в игре красится в кровь): пятна на земле (4 варианта), покадровые брызги удара (2×2),

usage: cd tools/texgen && python3 process7.py"""
import numpy as np
from PIL import Image
from scipy import ndimage
R = '../../Assets/_Project/'
E = [(6, 310), (318, 622), (630, 934), (943, 1247)]
src = np.asarray(Image.open('atlas_src/atlas7_ink.png').convert('RGB')).astype(np.float32) / 255
cells = {}
for r, (y0, y1) in enumerate(E):
    for c, (x0, x1) in enumerate(E):
        cells[r * 4 + c + 1] = src[y0 + 3:y1 - 3, x0 + 3:x1 - 3]

def key(c):
    m = c[..., 1] - np.maximum(c[..., 0], c[..., 2])
    a = np.clip((0.8 - m) / (0.8 - 0.12), 0, 1)
    rgb = np.clip((c - (1 - a[..., None]) * np.array([0, 1, 0])) / np.maximum(a[..., None], 1e-3), 0, 1)
    rgb[..., 1] = np.minimum(rgb[..., 1], np.maximum(rgb[..., 0], rgb[..., 2]))   # зелёная кайма
    return rgb, a

def gloss_mask(rgb, a):
    """Серый для тона в игре: тело пятна 0,55, блики туши светлее (влажный блеск), альфа — покрытие."""
    lum = rgb.mean(-1)
    v = np.clip(0.55 + 0.9 * lum, 0, 1)
    return np.dstack([v, v, v, a])

def to_img(o, W, H, rot=0):
    im = Image.fromarray((np.clip(o, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')
    if rot: im = im.rotate(rot, expand=True)
    return im.resize((W, H), Image.LANCZOS)

def crop_sq(o, pad=6):
    a = o[..., 3]; ys, xs = np.nonzero(a > 0.04)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    cy, cx = (y0 + y1) // 2, (x0 + x1) // 2; s = max(y1 - y0, x1 - x0) // 2 + pad
    out = np.zeros((2 * s, 2 * s, 4), np.float32)
    yy0, xx0 = cy - s, cx - s
    sy0, sx0 = max(0, yy0), max(0, xx0); sy1, sx1 = min(o.shape[0], cy + s), min(o.shape[1], cx + s)
    out[sy0 - yy0:sy1 - yy0, sx0 - xx0:sx1 - xx0] = o[sy0:sy1, sx0:sx1]
    return out

# --- пятна на земле: круг, полоса (поток вдоль +v: густо внизу, капли уходят вверх), лужа, россыпь капель
for k, name, rot in [(1, 'blood_splat', 0), (2, 'blood_streak', 90), (4, 'blood_pool', 0)]:  # россыпь капель (ячейка 3) — глянцевые шарики, на земле читались как бусины:
    o = crop_sq(gloss_mask(*key(cells[k])))
    to_img(o, 256, 256, rot).save(R + 'Resources/FX/' + name + '.png')

# --- брызги удара, 4 кадра (вылет вправо из левого нижнего угла) → лист 2×2
sheet = Image.new('RGBA', (512, 512), (0, 0, 0, 0))
for i, k in enumerate([13, 14, 15, 16]):
    o = gloss_mask(*key(cells[k]))
    sheet.paste(to_img(o, 256, 256), ((i % 2) * 256, (i // 2) * 256))
sheet.save(R + 'Resources/FX/blood_burst.png')

# маску ран из атласа пробовал — прежняя (tools/texgen/wound_mask.py: круглые пятна с потёками вниз) читается лучше, оставлена
