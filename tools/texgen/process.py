import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage
src = np.asarray(Image.open('atlas.png').convert('RGB')).astype(np.float32) / 255
xs = [(3, 414), (423, 831), (839, 1250)]
ys = [(3, 413), (421, 830), (838, 1250)]
cells = {}
for r, (y0, y1) in enumerate(ys):
    for c, (x0, x1) in enumerate(xs):
        cells[r * 3 + c + 1] = src[y0 + 2:y1 - 2, x0 + 2:x1 - 2]

def resize(a, n):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(im.resize((n, n), Image.LANCZOS)).astype(np.float32) / 255

def seamless(a, frac):
    """Бесшовность: смешение со сдвинутой на половину копией у краёв (frac — ширина зоны смешения от края)."""
    h, w = a.shape[:2]
    b = np.roll(np.roll(a, h // 2, 0), w // 2, 1)
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.minimum(np.minimum(xx, w - 1 - xx) / (w * frac), np.minimum(yy, h - 1 - yy) / (h * frac))
    m = np.clip(d, 0, 1); m = m * m * (3 - 2 * m)
    return a * m[..., None] + b * (1 - m[..., None])

def normal_from(a, strength, blur=1.0):
    h = a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    h = ndimage.gaussian_filter(h, blur, mode='wrap')
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)   # Unity: +y вверх по текстуре (строки вниз — минус)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5

def save(a, path, mode='RGB'):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(path)

out = {}
# 1 камень арены (1024 не тянем — 512), 2 черепица, 3 штукатурка, 4 доски, 5 гравий, 6 гранит с лишайником
cfg = {1: ('flagstone', 0.14, 6.0), 2: ('roof', 0.12, 5.0), 3: ('plaster', 0.25, 3.0), 4: ('wood', 0.12, 4.0), 5: ('gravel', 0.25, 5.0), 6: ('rock', 0.22, 5.0)}
for k, (name, frac, ns) in cfg.items():
    a = resize(cells[k], 512)
    a = seamless(a, frac)
    save(a, f'{name}_albedo.png')
    save(normal_from(a, ns), f'{name}_normal.png')
    out[k] = a
# гладкость камня арены: камень — средне, швы с мхом — матовые (R — металл 0, A — гладкость)
a = out[1]
lum = a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11
green = np.clip((a[..., 1] - np.maximum(a[..., 0], a[..., 2])) * 6, 0, 1)
sm = np.clip(0.15 + 0.55 * np.clip((lum - 0.18) / 0.3, 0, 1) - 0.4 * green, 0.05, 0.75)
ms = np.zeros((512, 512, 4)); ms[..., 3] = ndimage.gaussian_filter(sm, 1.0, mode='wrap')
save(ms, 'flagstone_ms.png', 'RGBA')

def key(a):
    """Хромакей по зелёному: альфа по «зелёности», зелёный ореол с краёв убираем."""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    gr = g - np.maximum(r, b)
    alpha = np.clip(1 - (gr - 0.12) / 0.3, 0, 1)
    alpha = ndimage.grey_erosion(alpha, size=(2, 2))
    g2 = np.minimum(g, np.maximum(r, b) + 0.04)   # despill
    rgb = np.stack([r, g2, b], -1)
    return np.concatenate([rgb, alpha[..., None]], -1)

def save_rgba(a, path, n):
    im = Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').resize((n, n), Image.LANCZOS)
    im.save(path)

save_rgba(key(cells[7]), 'maple_cluster.png', 512)
save_rgba(key(cells[9]), 'sakura_cluster.png', 512)
fl = key(cells[8]); save_rgba(fl, 'fallen_leaves.png', 512)
# одиночный лист для падающих частиц — алый, верхний левый
h, w = fl.shape[:2]
save_rgba(fl[: h // 2, : w // 2], 'maple_leaf.png', 128)
print('ok')
