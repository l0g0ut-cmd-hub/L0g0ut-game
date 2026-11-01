"""Атласы 9 (значки предметов) и 10 (интерфейс снаряжения): пурпурный фон → альфа.
usage: cd tools/texgen && python3 process9.py"""
import numpy as np
from PIL import Image
U = '../../Assets/_Project/Resources/UI/'
def grid(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32) / 255
    w = (a.min(2) > 0.92)
    def runs(v):
        r = []; s = None
        for i, x in enumerate(v > 0.97):
            if x and s is None: s = i
            if not x and s is not None: r.append((s, i - 1)); s = None
        if s is not None: r.append((s, len(v) - 1))
        return r
    def edges(v, n):
        g = [x for x in runs(v) if 5 < x[0] < n - 6]
        e = [0] + [x for gg in g for x in (gg[0], gg[1] + 1)] + [n]
        return [(e[2 * i], e[2 * i + 1]) for i in range(4)]
    xs, ys = edges(w.mean(0), a.shape[1]), edges(w.mean(1), a.shape[0])
    return a, xs, ys
def key(c, lo=0.08, hi=0.86):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    al = np.clip((hi - m) / (hi - lo), 0, 1)
    rgb = np.clip((c - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    lim = np.minimum(rgb[..., 0], rgb[..., 2]); sp = np.clip(lim - rgb[..., 1] - 0.1, 0, 1)
    rgb[..., 0] -= sp * 0.85; rgb[..., 2] -= sp * 0.85
    return np.clip(rgb, 0, 1), al
def cell(a, xs, ys, i, pad=9):
    x0, x1 = xs[i % 4]; y0, y1 = ys[i // 4]
    return a[y0 + pad:y1 - pad, x0 + pad:x1 - pad]
def img(rgb, al): return Image.fromarray((np.dstack([rgb, al]) * 255 + 0.5).astype(np.uint8), 'RGBA')
# значки предметов → лист 4×4 по 128
a, xs, ys = grid('atlas_src/atlas9_items.png')
sheet = Image.new('RGBA', (512, 512), (0, 0, 0, 0))
for i in range(16):
    sheet.paste(img(*key(cell(a, xs, ys, i))).resize((128, 128), Image.LANCZOS), ((i % 4) * 128, (i // 4) * 128))
sheet.save(U + 'items.png')
# интерфейс
a, xs, ys = grid('atlas_src/atlas10_ui.png')
for i, n in enumerate(['frame_0', 'frame_1', 'frame_2', 'frame_3']):
    rgb, al = key(cell(a, xs, ys, i, 9))
    h, w = al.shape; yy, xx = np.mgrid[0:h, 0:w]
    inner = (xx > w * 0.17) & (xx < w * 0.83) & (yy > h * 0.17) & (yy < h * 0.83)
    al = np.where(inner, 0.0, al)   # внутри рамки — пусто (свечение не заливает значок)
    img(rgb, al).resize((128, 128), Image.LANCZOS).save(U + n + '.png')
for i, n in zip(range(4, 9), ['stat_str', 'stat_vit', 'stat_spi', 'stat_agi', 'stat_tec']):
    rgb, al = key(cell(a, xs, ys, i)); al = al * np.clip(1.15 - rgb.mean(-1) * 1.1, 0, 1)
    img(np.ones_like(rgb), al).resize((128, 128), Image.LANCZOS).save(U + n + '.png')   # белая маска — тон в игре
for i, n, sz in [(9, 'chest_closed', (256, 256)), (10, 'chest_open', (256, 256)), (11, 'coins', (128, 128)), (14, 'levelup', (128, 128)), (15, 'enso_gold', (256, 256))]:
    img(*key(cell(a, xs, ys, i))).resize(sz, Image.LANCZOS).save(U + n + '.png')
for i, n in [(12, 'plank'), (13, 'plank_red')]:
    rgb, al = key(cell(a, xs, ys, i)); ys_, xs_ = np.nonzero(al > 0.1)
    c = img(rgb, al).crop((xs_.min(), ys_.min(), xs_.max() + 1, ys_.max() + 1)); c.resize((512, int(512 * c.size[1] / c.size[0])), Image.LANCZOS).save(U + n + '.png')
print('ok')

# атлас 11: ещё 36 значков (6×6) → UI/items2.png, ячейки по 128
a = np.asarray(Image.open('atlas_src/atlas11_items.png').convert('RGB')).astype(np.float32) / 255
w = (a.min(2) > 0.92)
def runs6(v, n):
    r = []; s = None
    for i, x in enumerate(v > 0.97):
        if x and s is None: s = i
        if not x and s is not None: r.append((s, i - 1)); s = None
    if s is not None: r.append((s, len(v) - 1))
    g = [x for x in r if 5 < x[0] < n - 6]
    e = [0] + [x for gg in g for x in (gg[0], gg[1] + 1)] + [n]
    cells = [(e[2 * i], e[2 * i + 1]) for i in range(len(e) // 2)]
    return [c for c in cells if c[1] - c[0] > 40]   # обрывки швов — не ячейки
xs6, ys6 = runs6(w.mean(0), a.shape[1]), runs6(w.mean(1), a.shape[0])
assert len(xs6) == 6 and len(ys6) == 6, (xs6, ys6)
sheet = Image.new('RGBA', (768, 768), (0, 0, 0, 0))
for i in range(36):
    x0, x1 = xs6[i % 6]; y0, y1 = ys6[i // 6]
    c = a[y0 + 6:y1 - 6, x0 + 6:x1 - 6]
    sheet.paste(img(*key(c)).resize((128, 128), Image.LANCZOS), ((i % 6) * 128, (i // 6) * 128))
sheet.save(U + 'items2.png')
print('items2 ok')
