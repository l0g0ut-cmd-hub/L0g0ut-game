"""Атлас 14: узоры накопления (сверху), столбы ауры, искры, кольцо волны, светящиеся трещины, пятно света — на чёрном, аддитив.
usage: cd tools/texgen && python3 process14.py"""
import numpy as np
from PIL import Image
FX = '../../Assets/_Project/Resources/FX/'
a = np.asarray(Image.open('atlas_src/atlas14_sigils.png').convert('RGB')).astype(np.float32) / 255
N = a.shape[0]
xs = [(0, 313), (313, 627), (627, 940), (940, N)]
ys = [(0, 323), (323, 596), (596, 950), (950, N)]
def cell(r, c):
    x0, x1 = xs[c]; y0, y1 = ys[r]
    v = np.clip((a[y0:y1, x0:x1] - 0.03) / 0.97, 0, 1)
    h, w = v.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    m = np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / 5.0, 0, 1)
    return v * m[..., None]
def bbox(v, t=0.08, pad=4):
    yy, xx = np.nonzero(v.max(-1) > t)
    return v[max(0, yy.min() - pad):yy.max() + 1 + pad, max(0, xx.min() - pad):xx.max() + 1 + pad]
def save(v, name, w, h, alpha=True):
    v = np.clip(v, 0, 1)
    o = np.dstack([v, v.max(-1)]) if alpha else v
    Image.fromarray((o * 255).astype(np.uint8), 'RGBA' if alpha else 'RGB').resize((w, h), Image.LANCZOS).save(FX + name + '.png')
def square(v, out, radial=True, t=0.08):
    v = bbox(v, t)
    img = Image.fromarray((v * 255).astype(np.uint8), 'RGB').resize((out, out), Image.LANCZOS)   # эллипс → круг
    s = np.asarray(img).astype(np.float32) / 255
    if radial:
        yy, xx = np.mgrid[0:out, 0:out]; r = np.hypot(xx - out / 2 + 0.5, yy - out / 2 + 0.5) / (out / 2)
        s = s * np.clip((1.0 - r) / 0.06, 0, 1)[..., None]
    return s
names = ['ice', 'flame', 'sun', 'storm']
for c, n in enumerate(names):
    save(square(cell(0, c), 512), 'sigil_' + n, 512, 512)
    save(square(cell(1, c), 512), 'sigil_' + n + '_early', 512, 512)
    p = bbox(cell(2, c), 0.06); h, w = p.shape[:2]
    save(p, 'pillar_' + n, 256, int(256 * h / w) // 4 * 4)
save(bbox(cell(3, 0)), 'charge_flecks', 256, 256)
save(square(cell(3, 1), 512, t=0.35), 'shock_ring', 512, 512)
save(square(cell(3, 2), 512), 'crack_glow', 512, 512)
save(square(cell(3, 3), 256, t=0.12), 'glow_pool', 256, 256)
print('ok')
