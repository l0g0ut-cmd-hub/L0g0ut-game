"""Атласы 5 (оружие/эффекты/кошка) и 6 (интерфейс): нарезка по белым швам, кейинг пурпура, маски, бесшовность.
usage: cd tools/texgen && python3 process5.py (пишет в Assets/_Project/...). После него: scroll.png обрезан по бумаге, mountains.png — растушёвка краёв, blood_splat.png — серый для тона, cat_fur_* в Resources/FX, оружие — в reserve/"""
import numpy as np, os
from PIL import Image
from scipy import ndimage
R = '../../Assets/_Project/'
UI, FX, TX = R + 'Resources/UI/', R + 'Resources/FX/', R + 'Art/Textures/'

def cells_of(path, edges):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32) / 255
    out = {}
    for r, (y0, y1) in enumerate(edges):
        for c, (x0, x1) in enumerate(edges):
            out[r * 4 + c + 1] = a[y0 + 4:y1 - 4, x0 + 4:x1 - 4]
    return out

def key(c):
    """Пурпурный фон → альфа; цвет освобождается от примеси фона."""
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    a = np.clip((0.86 - m) / (0.86 - 0.08), 0, 1)
    mag = np.array([1.0, 0.0, 1.0])
    rgb = (c - (1 - a[..., None]) * mag) / np.maximum(a[..., None], 1e-3)
    rgb = np.clip(rgb, 0, 1)
    # вдоль кромки остаточный пурпур — гасим: G не ниже min(R,B) − 0,1
    lim = np.minimum(rgb[..., 0], rgb[..., 2])
    spill = np.clip(lim - rgb[..., 1] - 0.12, 0, 1)
    rgb[..., 0] -= spill * 0.8; rgb[..., 2] -= spill * 0.8
    return np.clip(rgb, 0, 1), a

def bbox_crop(rgb, a, pad=6, thr=0.04):
    ys, xs = np.nonzero(a > thr)
    y0, y1 = max(0, ys.min() - pad), min(a.shape[0], ys.max() + pad + 1)
    x0, x1 = max(0, xs.min() - pad), min(a.shape[1], xs.max() + pad + 1)
    return rgb[y0:y1, x0:x1], a[y0:y1, x0:x1]

def fit(rgb, a, W, H, margin=0.04):
    """Вписать по центру в W×H с полями, сохраняя пропорции."""
    h, w = a.shape
    s = min(W * (1 - 2 * margin) / w, H * (1 - 2 * margin) / h)
    nw, nh = max(1, int(w * s)), max(1, int(h * s))
    im = np.dstack([rgb, a])
    im = np.asarray(Image.fromarray((im * 255).astype(np.uint8), 'RGBA').resize((nw, nh), Image.LANCZOS)).astype(np.float32) / 255
    out = np.zeros((H, W, 4), np.float32)
    oy, ox = (H - nh) // 2, (W - nw) // 2
    out[oy:oy + nh, ox:ox + nw] = im
    return out

def save_rgba(o, path):
    Image.fromarray((np.clip(o, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').save(path)
def save_rgb(o, path):
    Image.fromarray((np.clip(o[..., :3], 0, 1) * 255 + 0.5).astype(np.uint8), 'RGB').save(path)

def white_mask(o):
    """Тушь → белая маска (цвет задаётся в игре оттенком): RGB = 1, альфа = покрытие × тёмность."""
    lum = o[..., :3].mean(-1)
    out = o.copy(); out[..., :3] = 1.0
    out[..., 3] = o[..., 3] * np.clip(1.15 - lum * 1.1, 0, 1)
    return out

def resize(a, w, h):
    return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
def blend_axis(a, frac, axis):
    n = a.shape[axis]; b = np.roll(a, n // 2, axis)
    idx = np.arange(n); d = np.minimum(idx, n - 1 - idx) / (n * frac)
    m = np.clip(d, 0, 1); m = m * m * (3 - 2 * m)
    shape = [1, 1, 1]; shape[axis] = n; m = m.reshape(shape)
    return a * m + b * (1 - m)
def seamless(a, frac): return blend_axis(blend_axis(a, frac, 0), frac, 1)

# ---------- атлас 6: интерфейс ----------
e6 = [(3, 309), (316, 623), (630, 936), (943, 1250)]
c6 = cells_of('atlas_src/atlas6_ui.png', e6)
def ui(k, name, W, H, mask=False, margin=0.04, split=None):
    rgb, a = key(c6[k])
    if split is not None:
        w = a.shape[1]; x0, x1 = (0, w // 2) if split == 0 else (w // 2, w)
        rgb, a = rgb[:, x0:x1], a[:, x0:x1]
    rgb, a = bbox_crop(rgb, a)
    o = fit(rgb, a, W, H, margin)
    if mask: o = white_mask(o)
    save_rgba(o, UI + name + '.png')
ui(1, 'enso', 512, 512, mask=True)
ui(2, 'seal_sho', 256, 256); ui(3, 'seal_hai', 256, 256); ui(4, 'seal_owari', 256, 256)
ui(5, 'brush_stroke', 1024, 256, margin=0.02)          # новый мазок вместо старого (тот же путь — все места подхватят)
ui(6, 'brush_stroke_red', 1024, 256, margin=0.02)
ui(7, 'ink_r1', 256, 256, mask=True); ui(8, 'ink_r2', 256, 256, mask=True); ui(9, 'ink_r3', 256, 256, mask=True)
ui(10, 'ink_zan', 256, 256, mask=True); ui(11, 'ink_hajime', 256, 256, mask=True)
ui(12, 'scroll', 1024, 512, margin=0.01)
ui(13, 'corner', 256, 256, margin=0.0)
ui(14, 'kamon_feather', 256, 256, split=0); ui(14, 'kamon_tomoe', 256, 256, split=1)
ui(15, 'mountains', 1024, 256, margin=0.0)
ui(16, 'sakura_branch', 512, 512, margin=0.0)

# ---------- атлас 5: оружие, эффекты, кошка ----------
e5 = [(0, 301), (318, 618), (635, 935), (952, 1253)]
c5 = cells_of('atlas_src/atlas5_fx.png', e5)
def fx_black(k, name, W, H, crop=None):
    """Чёрный фон (аддитивные): яркость как есть, плюс альфа = яркость (на случай альфа-шейдера)."""
    c = c5[k]
    if crop: c = c[crop[0]:crop[1], crop[2]:crop[3]]
    c = np.clip((c - 0.03) / 0.97, 0, 1)
    o = resize(c, W, H)
    a = o.max(-1)
    save_rgba(np.dstack([o, a]), FX + name + '.png')
def fx_key(k, name, W, H, mask=False, margin=0.03):
    rgb, a = key(c5[k]); rgb, a = bbox_crop(rgb, a)
    o = fit(rgb, a, W, H, margin)
    if mask: o = white_mask(o)
    save_rgba(o, FX + name + '.png')
fx_black(5, 'slash_arc', 256, 256)
fx_black(6, 'spark_burst', 256, 256)
fx_key(7, 'ink_slash', 512, 512, mask=True)
fx_key(8, 'blood_splat', 256, 256)                       # новая брызга вместо старой
# пыль: из яркости — белая с альфой (для альфа-смешения)
d = resize(np.clip((c5[9] - 0.03) / 0.97, 0, 1), 256, 256); da = d.mean(-1)
yy, xx = np.mgrid[0:256, 0:256]; rr = np.hypot(xx - 128, yy - 128) / 128
da = da * np.clip((1.0 - rr) / 0.12, 0, 1)
save_rgba(np.dstack([np.ones_like(d) * 0.92, np.clip(da * 1.3, 0, 1)]), FX + 'dust_puff.png')
fx_key(10, 'ground_crack', 512, 512, mask=True, margin=0.0)
fx_black(13, 'embers_sheet', 256, 256)
fx_black(14, 'ripple', 256, 256)
fx_black(15, 'splash_crown', 256, 256)
fx_black(16, 'lightning', 256, 512)
# оружие и шерсть
rgb, a = key(c5[1]); rgb, a = bbox_crop(rgb, a, pad=2)
save_rgba(fit(rgb, a, 1024, 128, 0.0), TX + 'katana_blade.png')
save_rgb(seamless(resize(c5[2][20:-20, :], 256, 256), 0.2), TX + 'tsuka_ito.png')
save_rgb(seamless(resize(c5[3][40:-40, :], 256, 256), 0.2), TX + 'saya_lacquer.png')
rgb, a = key(c5[4]); rgb, a = bbox_crop(rgb, a); save_rgba(fit(rgb, a, 256, 256, 0.0), TX + 'tsuba.png')
save_rgb(seamless(resize(c5[11], 512, 512), 0.22), TX + 'cat_fur_ginger.png')
save_rgb(seamless(resize(c5[12], 512, 512), 0.22), TX + 'cat_fur_white.png')
print('ok')
