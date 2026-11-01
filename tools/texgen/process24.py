"""Атласы 24–27: огонь жаровни (8 кадров цикла) и пламя свечи (4) — чёрный фон, аддитив; дым Тени и камни Они (по 8) —
пурпур, альфа; цифры кистью 0–9, «×», «!» и клякса туши (4) — пурпур; ещё 36 предметов (6×6) — пурпур.
usage: cd tools/texgen && python3 process24.py"""
import numpy as np
from PIL import Image
FX, UI = '../../Assets/_Project/Resources/FX/', '../../Assets/_Project/Resources/UI/'
G = [0, 313, 627, 940, 1254]
def load(n): return np.asarray(Image.open('atlas_src/' + n + '.png').convert('RGB')).astype(np.float32) / 255
def edge(v, e=6.0):
    h, w = v.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    return np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / e, 0, 1)
def to_img(o, w, h): return Image.fromarray((np.clip(o, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)
def add(c):
    v = np.clip((c - 0.03) / 0.97, 0, 1) * edge(c)[..., None]; return np.dstack([v, v.max(-1)])
def key(c, lo=0.08, hi=0.86, neutral=False):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    al = np.clip((hi - m) / (hi - lo), 0, 1)
    rgb = np.clip((c - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    if neutral:   # дым и пыль — без розового и зелёного отлива: яркость в тёплый серо-коричневый
        g = (0.3 * rgb[..., 0:1] + 0.4 * rgb[..., 1:2] + 0.3 * rgb[..., 2:3])
        rgb = g * np.array([1.0, 0.93, 0.85])
    else:
        lim = np.minimum(rgb[..., 0], rgb[..., 2]); sp = np.clip(lim - rgb[..., 1], 0, 1)
        rgb[..., 0] -= sp; rgb[..., 2] -= sp
    return np.dstack([np.clip(rgb, 0, 1), al * edge(c)])
def cell(a, i, pad=0):
    r, c = divmod(i, 4); return a[G[r] + pad:G[r + 1] - pad, G[c] + pad:G[c + 1] - pad]
def sheet(frames, cols, path, cw=256, ch=256):
    rows = (len(frames) + cols - 1) // cols
    S = Image.new('RGBA', (cw * cols, ch * rows), (0, 0, 0, 0))
    for k, o in enumerate(frames): S.paste(to_img(o, cw, ch), ((k % cols) * cw, (k // cols) * ch))
    S.save(path); print(path.split('/')[-1], S.size)
# 24: огонь
f = load('atlas24_fire')
sheet([add(cell(f, i)) for i in range(8)], 4, FX + 'brazier_fire.png')
sheet([add(cell(f, i)) for i in range(8, 12)], 4, FX + 'candle_flame.png')
sheet([add(cell(f, i)) for i in range(12, 16)], 4, FX + 'fireflies_loop.png')
# 25: дым и камни (альфа)
s = load('atlas25_smoke')
sheet([key(cell(s, i, 3), neutral=True) for i in range(8)], 4, FX + 'smoke_teleport.png')
sheet([key(cell(s, i, 3), neutral=True) for i in range(8, 16)], 4, FX + 'rock_burst.png')
# 26: цифры кистью (0–9, ×, !) и клякса
d = load('atlas26_digits')
sheet([key(cell(d, i, 4)) for i in range(12)], 4, UI + 'brush_digits.png')
sheet([key(cell(d, i, 2)) for i in range(12, 16)], 4, UI + 'ink_splash.png')
# 27: ещё 36 предметов → UI/items3.png (6×6 по 128)
it = load('atlas27_items3'); E = [round(i * 1254 / 6) for i in range(7)]
T = Image.new('RGBA', (768, 768), (0, 0, 0, 0))
for i in range(36):
    r, c = divmod(i, 6); cc = it[E[r] + 5:E[r + 1] - 5, E[c] + 5:E[c + 1] - 5]
    T.paste(to_img(key(cc), 128, 128), (c * 128, r * 128))
T.save(UI + 'items3.png'); print('items3 ok')
