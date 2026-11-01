"""Портреты врагов (2×2 из ChatGPT) → круглые 256×256 с кольцом-оправой, как у прежних (кольцо снято с portrait_undead).
usage: cd tools/texgen && python3 portraits.py"""
import numpy as np
from PIL import Image
U = '../../Assets/_Project/Resources/UI/'
src = Image.open('atlas_src/portraits_enemies.png').convert('RGB')
a = np.asarray(src).astype(int); W = a.shape[1]
white = a.min(2) > 235
def runs(v):
    r = []; s = None
    for i, x in enumerate(v > 0.97):
        if x and s is None: s = i
        if not x and s is not None: r.append((s, i - 1)); s = None
    if s is not None: r.append((s, len(v) - 1))
    return r
cs = runs(white.mean(0)); rs = runs(white.mean(1))
def spans(rr, n):
    inner = [x for x in rr if 5 < x[0] < n - 5]
    g = inner[0]; return [(0, g[0]), (g[1] + 1, n)]
xs, ys = spans(cs, W), spans(rs, a.shape[0])
# центр (доли ячейки) и размер квадрата — лицо в середине круга
crops = {'portrait_fallen': (0, 0, 0.48, 0.44, 0.9), 'portrait_shadow': (1, 0, 0.6, 0.42, 0.78),
         'portrait_oni': (0, 1, 0.53, 0.45, 0.84), 'portrait_shogun': (1, 1, 0.5, 0.46, 0.9)}
N = 256
old = np.asarray(Image.open(U + 'portrait_undead.png').convert('RGBA').resize((N, N), Image.LANCZOS)).astype(np.float32) / 255
yy, xx = np.mgrid[0:N, 0:N]; r = np.hypot(xx - (N - 1) / 2, yy - (N - 1) / 2) / (N / 2)
ring = np.clip((r - 0.86) / 0.03, 0, 1)                   # кольцо прежнего портрета (внешние 14 %)
inside = np.clip((0.97 - r) / 0.02, 0, 1)
for name, (cx, cy, fx, fy, fs) in crops.items():
    x0, x1 = xs[cx]; y0, y1 = ys[cy]; w = min(x1 - x0, y1 - y0)
    s = int(w * fs); ccx, ccy = x0 + int((x1 - x0) * fx), y0 + int((y1 - y0) * fy)
    box = (max(x0, ccx - s // 2), max(y0, ccy - s // 2)); box = (box[0], box[1], box[0] + s, box[1] + s)
    face = np.asarray(src.crop(box).resize((N, N), Image.LANCZOS)).astype(np.float32) / 255
    out = np.zeros((N, N, 4), np.float32)
    out[..., :3] = face * (1 - ring[..., None]) + old[..., :3] * ring[..., None]
    out[..., 3] = np.where(r < 0.86, inside, old[..., 3])
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), 'RGBA').save(U + name + '.png')
print('ok', xs, ys)
