import numpy as np
from PIL import Image
from scipy import ndimage
src = np.asarray(Image.open('atlas.png').convert('RGB')).astype(np.float32) / 255
xs = [(0, 411), (421, 832), (842, 1254)]
ys = [(0, 411), (422, 831), (842, 1254)]
cells = {}
for r, (y0, y1) in enumerate(ys):
    for c, (x0, x1) in enumerate(xs):
        cells[r * 3 + c + 1] = src[y0 + 3:y1 - 3, x0 + 3:x1 - 3]
def save(a, path, mode='RGB'):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(path)
def resize(a, w, h):
    return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
def blend_axis(a, frac, axis):
    """Бесшовность только по одной оси (0 — верх/низ, 1 — лево/право)."""
    n = a.shape[axis]; b = np.roll(a, n // 2, axis)
    idx = np.arange(n); d = np.minimum(idx, n - 1 - idx) / (n * frac)
    m = np.clip(d, 0, 1); m = m * m * (3 - 2 * m)
    shape = [1, 1, 1]; shape[axis] = n; m = m.reshape(shape)
    return a * m + b * (1 - m)
def seamless(a, frac):
    return blend_axis(blend_axis(a, frac, 0), frac, 1)
def normal_from(a, strength, blur=1.0):
    h = a[..., 0] * 0.3 + a[..., 1] * 0.59 + a[..., 2] * 0.11
    h = ndimage.gaussian_filter(h, blur, mode='wrap')
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength; dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nn = np.stack([-dx, dy, np.ones_like(h)], -1); nn /= np.linalg.norm(nn, axis=-1, keepdims=True)
    return nn * 0.5 + 0.5
def period(sig, lo, hi):
    s = sig - sig.mean(); ac = np.correlate(s, s, 'full')[len(s) - 1:]
    return lo + int(np.argmax(ac[lo:hi]))
# 1 железо, 2 бронза с патиной
for k, name, ns in [(1, 'iron_old', 5.0), (2, 'bronze_patina', 4.0)]:
    a = seamless(resize(cells[k], 512, 512), 0.22); save(a, name + '_albedo.png'); save(normal_from(a, ns), name + '_normal.png')
# 3 бамбук: средний ствол (по ширине — вся окружность), по высоте — бесшовно
c3 = cells[3]; w = c3.shape[1]
col = c3.mean(axis=(0, 2))
x0, x1 = int(w * 0.36), int(w * 0.66)   # середина — центральный ствол
stalk = resize(c3[:, x0:x1], 128, 512)
stalk = blend_axis(stalk, 0.12, 0)
stalk = blend_axis(stalk, 0.2, 1)   # шов вокруг ствола
save(stalk, 'bamboo_albedo.png'); save(normal_from(stalk, 4.0), 'bamboo_normal.png')
# 4 верёвка: пряди поперёк → поворот на 90°, чтобы шли вдоль оси цилиндра (v); бесшовно вдоль
rope = resize(cells[4], 512, 512)
P = period(rope.mean(-1).mean(1), 40, 200)
rope = rope[: (512 // P) * P] if (512 // P) * P > 200 else rope
rope = resize(rope, 512, 512)
rope = blend_axis(blend_axis(rope, 0.12, 1), 0.1, 0)
rope = np.rot90(rope, 1).copy()
save(rope, 'rope_albedo.png'); save(normal_from(rope, 5.0), 'rope_normal.png')
# 5 створка ворот — как есть (растягивается на грань)
door = resize(cells[5], 512, 512); save(door, 'gate_door_albedo.png'); save(normal_from(door, 5.0), 'gate_door_normal.png')
# 6 фонарь-тётин: бесшовно по горизонтали (вокруг), сверху/снизу как есть
ch = resize(cells[6], 512, 512); ch = blend_axis(ch, 0.12, 1); save(ch, 'chochin.png')
# 7 бумага для меню, 8 грабли (пока в запас), 9 кора криптомерии
paper = seamless(resize(cells[7], 512, 512), 0.25); save(paper, 'paper_ui.png')
rake = seamless(resize(cells[8], 512, 512), 0.15); save(rake, 'raked_gravel.png')
cb = seamless(resize(cells[9], 512, 512), 0.18); save(cb, 'cedar_bark_albedo.png'); save(normal_from(cb, 6.0), 'cedar_bark_normal.png')
print('ok rope period', P)
