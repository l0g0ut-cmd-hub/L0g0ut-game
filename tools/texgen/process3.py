import numpy as np
from PIL import Image
from scipy import ndimage
src = np.asarray(Image.open('atlas.png').convert('RGB')).astype(np.float32) / 255
xs = [(3, 415), (421, 833), (839, 1251)]
ys = [(3, 415), (420, 833), (838, 1252)]
cells = {}
for r, (y0, y1) in enumerate(ys):
    for c, (x0, x1) in enumerate(xs):
        cells[r * 3 + c + 1] = src[y0 + 3:y1 - 3, x0 + 3:x1 - 3]
def save(a, path, mode='RGB'):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(path)
def resize(a, w, h):
    mode = 'RGBA' if a.shape[-1] == 4 else 'RGB'
    return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), mode).resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
def seamless(a, frac, dx=None, dy=None):
    h, w = a.shape[:2]; b = np.roll(np.roll(a, dy if dy is not None else h // 2, 0), dx if dx is not None else w // 2, 1)
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.minimum(np.minimum(xx, w - 1 - xx) / (w * frac), np.minimum(yy, h - 1 - yy) / (h * frac))
    m = np.clip(d, 0, 1); m = m * m * (3 - 2 * m)
    return a * m[..., None] + b * (1 - m[..., None])
def normal_from(a, strength, blur=1.0):
    h = a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    h = ndimage.gaussian_filter(h, blur, mode='wrap')
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength; dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nn = np.stack([-dx, dy, np.ones_like(h)], -1); nn /= np.linalg.norm(nn, axis=-1, keepdims=True)
    return nn * 0.5 + 0.5
def period(sig, lo, hi):
    s = sig - sig.mean(); ac = np.correlate(s, s, 'full')[len(s) - 1:]
    return lo + int(np.argmax(ac[lo:hi]))

# 1 кладка: сдвиг на целое число рядов по вертикали (ряды ~ горизонтальные), по горизонтали — середина
a = resize(cells[1], 512, 512); lum = a.mean(-1)
Q = period(lum.mean(1), 60, 200); dy = int(round(256 / Q)) * Q
sb = seamless(a, 0.1, None, dy)
save(sb, 'stoneblock_albedo.png'); save(normal_from(sb, 6.0), 'stoneblock_normal.png')
l2 = sb.mean(-1); joints = np.clip((0.3 - l2) / 0.15, 0, 1)
ms = np.zeros((512, 512, 4)); ms[..., 3] = np.clip(0.35 - 0.25 * joints + 0.1 * (l2 - 0.4), 0.05, 0.6)
save(ms, 'stoneblock_ms.png', 'RGBA')
# 3 лак тории, 4 лесная подстилка
lq = seamless(resize(cells[3], 512, 512), 0.12); save(lq, 'lacquer_albedo.png')
ff = seamless(resize(cells[4], 512, 512), 0.2); save(ff, 'forest_floor_albedo.png'); save(normal_from(ff, 5.0), 'forest_floor_normal.png')
# 2 сёдзи: решётка из прежней текстуры (тёмные рамки), бумага — фото васи
old = np.asarray(Image.open('../bladepath/Assets/_Project/Art/Textures/shoji_albedo.png').convert('RGB')).astype(np.float32) / 255
lat = (old.mean(-1) < 0.35).astype(np.float32)
lat = np.asarray(Image.fromarray((lat * 255).astype(np.uint8)).resize((512, 640), Image.NEAREST)).astype(np.float32) / 255
lat = ndimage.gaussian_filter(lat, 0.7)
paper = resize(seamless(resize(cells[2], 512, 512), 0.2), 512, 640)
wood = np.array([0.16, 0.11, 0.08])
alb = paper * (1 - lat[..., None]) + wood * lat[..., None]
save(alb, 'shoji_albedo.png')
pl = paper.mean(-1); fib = (pl - pl.mean()) / (pl.std() + 1e-4)
em = np.stack([np.full_like(pl, 1.0), np.full_like(pl, 0.62), np.full_like(pl, 0.3)], -1) * np.clip(0.92 + 0.045 * fib, 0.72, 1.05)[..., None]
em *= (1 - lat[..., None])
save(em, 'shoji_emission.png')

def key_green(a, lo=0.28, hi=0.52):
    d = np.sqrt(a[..., 0] ** 2 + (1 - a[..., 1]) ** 2 + a[..., 2] ** 2)
    al = ndimage.grey_erosion(np.clip((d - lo) / (hi - lo), 0, 1), size=(2, 2))
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    g2 = np.where(al < 0.98, np.minimum(g, np.maximum(r, b) * 1.4 + 0.05), g)
    return np.concatenate([np.stack([r, g2, b], -1), al[..., None]], -1)
def key_white(a, lo=0.06, hi=0.22):
    d = np.sqrt(((1 - a) ** 2).sum(-1))
    al = ndimage.grey_erosion(np.clip((d - lo) / (hi - lo), 0, 1), size=(2, 2))
    return np.concatenate([a, al[..., None]], -1)
save(resize(key_green(cells[5]), 512, 512), 'cedar_cluster.png', 'RGBA')
save(resize(key_green(cells[6]), 512, 512), 'lotus.png', 'RGBA')
save(resize(key_white(cells[7]), 512, 512), 'sakura_ground.png', 'RGBA')
save(resize(key_white(cells[8]), 512, 512), 'moss_patches.png', 'RGBA')
# дым на белом: альфа — темнота, цвет — светло-серый (тонирует материал частиц)
sm = cells[9]; dk = 1 - sm.mean(-1)
al = np.clip((dk - 0.02) / 0.45, 0, 1)
smoke = np.concatenate([np.ones_like(sm) * 0.85, al[..., None]], -1)
save(resize(smoke, 512, 512), 'smoke.png', 'RGBA')
print('ok', Q)
