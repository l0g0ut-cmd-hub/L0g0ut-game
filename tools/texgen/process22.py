"""Атлас 22 (супер-удар: молния с неба 12 кадров + полумесяц 4, на чёрном) и 23 (добыча на пурпуре: сундук, блеск рамки,
монеты, герб уровня — по 4 кадра). usage: cd tools/texgen && python3 process22.py"""
import numpy as np
from PIL import Image
FX, UI = '../../Assets/_Project/Resources/FX/', '../../Assets/_Project/Resources/UI/'
G = [0, 313, 627, 940, 1254]
def load(n): return np.asarray(Image.open('atlas_src/' + n + '.png').convert('RGB')).astype(np.float32) / 255
def edge(v, e=6.0):
    h, w = v.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    return np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / e, 0, 1)
def to_img(o, w, h): return Image.fromarray((np.clip(o, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)
# 22: аддитив
a = load('atlas22_super')
def addcell(i):
    r, c = divmod(i, 4); v = np.clip((a[G[r]:G[r + 1], G[c]:G[c + 1]] - 0.03) / 0.97, 0, 1)
    v = v * edge(v)[..., None]; return np.dstack([v, v.max(-1)])
S = Image.new('RGBA', (256 * 4, 256 * 3))
for i in range(12): S.paste(to_img(addcell(i), 256, 256), ((i % 4) * 256, (i // 4) * 256))
S.save(FX + 'super_strike.png')
C = Image.new('RGBA', (256 * 4, 256))
for i in range(12, 16): C.paste(to_img(addcell(i), 256, 256), ((i - 12) * 256, 0))
C.save(FX + 'super_crescent.png')
# 23: пурпурный ключ (как в process12)
b = load('atlas23_loot')
def key(c, lo=0.08, hi=0.86):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    al = np.clip((hi - m) / (hi - lo), 0, 1)
    rgb = np.clip((c - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    lim = np.minimum(rgb[..., 0], rgb[..., 2]); sp = np.clip(lim - rgb[..., 1], 0, 1)
    rgb[..., 0] -= sp; rgb[..., 2] -= sp
    # полупрозрачное свечение (лучи, ореол) — в тёплое золото, без розового отлива от фона
    gold = np.array([1.0, 0.8, 0.42]); w = np.clip((0.95 - al) / 0.6, 0, 1)[..., None]
    rgb = rgb * (1 - w) + gold * np.clip(rgb.max(-1, keepdims=True) * 1.1, 0, 1) * w
    # всё сине-розовое (b > g) — в золото: у этой палитры r ≥ g ≥ b
    pk = np.clip((rgb[..., 2] - 0.8 * rgb[..., 1]) * 8, 0, 1)
    rgb[..., 1] = rgb[..., 1] * (1 - pk) + np.maximum(rgb[..., 1], rgb[..., 0] * 0.8) * pk
    rgb[..., 2] = rgb[..., 2] * (1 - pk) + rgb[..., 1] * 0.55 * pk
    return np.dstack([np.clip(rgb, 0, 1), al * edge(c)])
for r, n in enumerate(['chest_anim', 'shine_frame', 'coin_burst', 'levelup_anim']):
    T = Image.new('RGBA', (256 * 4, 256), (0, 0, 0, 0))
    for c in range(4): T.paste(to_img(key(b[G[r] + 4:G[r + 1] - 4, G[c] + 4:G[c + 1] - 4]), 256, 256), (c * 256, 0))
    T.save(UI + n + '.png')
print('ok')
