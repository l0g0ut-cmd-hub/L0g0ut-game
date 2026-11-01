"""Атлас 12 (эффекты на чёрном) и 13 (экран «Пути», карточки приёмов, враги в полный рост, на пурпуре).
usage: cd tools/texgen && python3 process12.py"""
import numpy as np
from PIL import Image
FX, UI = '../../Assets/_Project/Resources/FX/', '../../Assets/_Project/Resources/UI/'
def spans(cuts, n):
    e = [0] + [x for c in cuts for x in (c[0], c[1] + 1)] + [n]
    s = [(e[2 * i], e[2 * i + 1]) for i in range(len(e) // 2)]
    return [c for c in s if c[1] - c[0] > 40]
# ---------- 12: эффекты ----------
a = np.asarray(Image.open('atlas_src/atlas12_fx.png').convert('RGB')).astype(np.float32) / 255
xs = spans([(0, 4), (207, 213), (417, 422), (625, 631), (834, 840), (1043, 1048), (1251, 1253)], 1254)
ys = spans([(0, 4), (205, 211), (407, 413), (599, 604), (797, 803), (1005, 1010), (1217, 1253)], 1254)
assert len(xs) == 6 and len(ys) == 6, (xs, ys)
def C(i, pad=3):
    x0, x1 = xs[i % 6]; y0, y1 = ys[i // 6]
    c = a[y0 + pad:y1 - pad, x0 + pad:x1 - pad]
    c = np.clip((c - 0.035) / 0.965, 0, 1)
    # края ячейки — в ноль (без светлых швов)
    h, w = c.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    m = np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / 6.0, 0, 1)
    return c * m[..., None]
def rgba(c): return np.dstack([c, c.max(-1)])
def im(c, w, h): return Image.fromarray((np.clip(c, 0, 1) * 255).astype(np.uint8), 'RGBA' if c.shape[-1] == 4 else 'RGB').resize((w, h), Image.LANCZOS)
def sheet(ids, name, cell=256, cols=3):
    rows = (len(ids) + cols - 1) // cols
    S = Image.new('RGBA', (cell * cols, cell * rows), (0, 0, 0, 0))
    for k, i in enumerate(ids): S.paste(im(rgba(C(i)), cell, cell), ((k % cols) * cell, (k // cols) * cell))
    S.save(FX + name + '.png')
sheet([0, 1, 2, 3, 4, 5], 'orb_blue')        # 3×2 кадра, шар летит вправо
sheet([6, 7, 8, 9, 10, 11], 'orb_red')
sheet([18, 19, 20, 21, 22, 23], 'burst_blue')
sheet([24, 25, 26], 'aura_flame', cols=3)
sheet([27, 28, 29], 'vortex', cols=3)
for i, n in [(30, 'seal_circle'), (31, 'ring_glow'), (32, 'x_slash'), (33, 'star_glint'), (34, 'smoke_wisp')]:
    im(rgba(C(i)), 256, 256).save(FX + n + '.png')
# следы клинка: от яркой линии посередине — верхняя половина, перевёрнута (линия у верхнего края = острие ленты)
for i, n in [(12, 'trail_ice'), (13, 'trail_violet'), (14, 'trail_fire'), (15, 'trail_crimson'), (16, 'trail_lightning'), (17, 'trail_moon')]:
    c = C(i, 2); h = c.shape[0]
    lum = c.max(-1).mean(1); mid = int(np.argmax(lum[h // 4:3 * h // 4]) + h // 4)
    top = c[max(0, mid - h // 3):mid + 2][::-1]       # от линии вверх → перевёрнуто: линия внизу… разворачиваем ниже
    top = top[::-1]                                    # линия — последней строкой (низ картинки = v 0)
    t = im(rgba(top), 256, 64)
    t = t.transpose(Image.FLIP_TOP_BOTTOM)             # в Unity v=1 — верхняя строка: линия у v=1 (острие)
    t.save(FX + n + '.png')
# ---------- 13: экран «Пути» ----------
b = np.asarray(Image.open('atlas_src/atlas13_path.png').convert('RGB')).astype(np.float32) / 255
bx = spans([(0, 4), (310, 317), (623, 630), (936, 943), (1249, 1253)], 1254)
by = spans([(0, 5), (375, 382), (623, 630), (935, 942), (1245, 1253)], 1254)
assert len(bx) == 4 and len(by) == 4, (bx, by)
def key(c, lo=0.08, hi=0.86):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    al = np.clip((hi - m) / (hi - lo), 0, 1)
    rgb = np.clip((c - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    lim = np.minimum(rgb[..., 0], rgb[..., 2]); sp = np.clip(lim - rgb[..., 1] - 0.1, 0, 1)
    rgb[..., 0] -= sp * 0.85; rgb[..., 2] -= sp * 0.85
    return np.dstack([np.clip(rgb, 0, 1), al])
def B(i, pad=6):
    x0, x1 = bx[i % 4]; y0, y1 = by[i // 4]
    return b[y0 + pad:y1 - pad, x0 + pad:x1 - pad]
def crop_alpha(o):
    ys_, xs_ = np.nonzero(o[..., 3] > 0.05); return o[ys_.min():ys_.max() + 1, xs_.min():xs_.max() + 1]
for k, n in enumerate(['enemy_full_fallen', 'enemy_full_shadow', 'enemy_full_oni', 'enemy_full_shogun']):
    o = crop_alpha(key(B(k))); h, w = o.shape[:2]
    im(o, int(512 * w / h), 512).save(UI + n + '.png')
for k, n in zip(range(4, 8), ['medal_0', 'medal_1', 'medal_2', 'medal_3']):
    im(key(B(k)), 192, 192).save(UI + n + '.png')
for k, n in zip([8, 9, 10, 11, 12, 13], ['tech_0', 'tech_1', 'tech_2', 'tech_3', 'tech_4', 'tech_5']):
    o = crop_alpha(key(B(k))); im(o, 256, 256).save(UI + n + '.png')
im(B(14, 4), 640, 640).save(UI + 'path_panorama.png')
o = crop_alpha(key(B(15))); h, w = o.shape[:2]; im(o, int(256 * w / h), 256).save(UI + 'path_lantern.png')
print('ok')
