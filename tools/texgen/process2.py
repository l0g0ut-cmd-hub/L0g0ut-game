import numpy as np
from PIL import Image
from scipy import ndimage
src = np.asarray(Image.open('atlas.png').convert('RGB')).astype(np.float32) / 255
xs = [(3, 415), (421, 833), (839, 1251)]
ys = [(3, 415), (420, 833), (839, 1251)]
cells = {}
for r, (y0, y1) in enumerate(ys):
    for c, (x0, x1) in enumerate(xs):
        cells[r * 3 + c + 1] = src[y0 + 3:y1 - 3, x0 + 3:x1 - 3]
def save(a, path, mode='RGB'):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(path)
def resize(a, w, h):
    mode = 'RGBA' if a.shape[-1] == 4 else 'RGB'
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), mode)
    return np.asarray(im.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255

# ---------- тени монахов (L: 255 — бумага, 56 — тень; полутень — размытие) ----------
def sil(cell):
    return (cell.mean(-1) < 0.5).astype(np.float32)
SH = 56 / 255
def to_shadow(mask, blur):
    m = ndimage.gaussian_filter(mask, blur)
    return 1 - (1 - SH) * np.clip(m, 0, 1)
# сидящий: квадрат 512, фигура по росту ~64% (сидящий ~0,9 м в наклейке 1,4 м), низ — у нижнего края (пол веранды)
m = sil(cells[1]); lab, n = ndimage.label(m); sizes = ndimage.sum(m, lab, range(1, n + 1)); m = (lab == 1 + int(np.argmax(sizes))).astype(np.float32)
ys_, xs_ = np.nonzero(m); y0, y1, x0, x1 = ys_.min(), ys_.max(), xs_.min(), xs_.max()
fig = m[y0:y1 + 1, x0:x1 + 1]
H = 512; th = int(0.64 * H); sc = th / fig.shape[0]; tw = int(fig.shape[1] * sc)
figr = resize(np.repeat(fig[..., None], 3, -1), tw, th)[..., 0]
canvas = np.zeros((H, H), np.float32); ox = (H - tw) // 2
canvas[H - 8 - th:H - 8, ox:ox + tw] = figr
save(to_shadow(canvas, 2.5), 'shoji_monk.png', 'L')
# идущий: 8 кадров 256×512 в ряд, общий масштаб, ступни на одной линии, по горизонтали — по центру плеч/головы
frames = []
for cid in (2, 3):
    mm = sil(cells[cid]); lab, n = ndimage.label(mm)
    objs = ndimage.find_objects(lab)
    big = [(s, i + 1) for i, s in enumerate(objs) if (s[0].stop - s[0].start) > 100]
    big.sort(key=lambda t: (round(t[0][0].start / 150), t[0][1].start))
    for s, li in big[:4]:
        frames.append((lab[s] == li).astype(np.float32))
print('walk frames', len(frames), [f.shape for f in frames])
hmed = np.median([f.shape[0] for f in frames])
FW, FH = 256, 512
sc = 0.88 * FH / hmed
strip = np.zeros((FH, FW * 8), np.float32)
for k, f in enumerate(frames):
    th, tw = int(f.shape[0] * sc), int(f.shape[1] * sc)
    fr = resize(np.repeat(f[..., None], 3, -1), tw, th)[..., 0]
    top = fr[: int(th * 0.35)]                     # голова и плечи — опора по горизонтали (ноги не дёргают кадр)
    cx = np.nonzero(top.sum(0) > 0.5)[0]; cxm = int(cx.mean()) if len(cx) else tw // 2
    x0 = FW * k + FW // 2 - cxm; y0 = FH - 10 - th
    xa, xb = max(FW * k, x0), min(FW * (k + 1), x0 + tw)
    strip[y0:y0 + th, xa:xb] = np.maximum(strip[y0:y0 + th, xa:xb], fr[:, xa - x0:xb - x0])
save(to_shadow(strip, 2.5), 'shoji_walker.png', 'L')

# ---------- хромакей (по расстоянию до чистого зелёного — трава и бамбук сами зелёные) ----------
def key(a, lo=0.28, hi=0.52):
    d = np.sqrt(a[..., 0] ** 2 + (1 - a[..., 1]) ** 2 + a[..., 2] ** 2)
    alpha = np.clip((d - lo) / (hi - lo), 0, 1)
    alpha = ndimage.grey_erosion(alpha, size=(2, 2))
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    edge = (alpha < 0.98)
    g2 = np.where(edge, np.minimum(g, np.maximum(r, b) * 1.35 + 0.05), g)   # с краёв — зелёный ореол
    return np.concatenate([np.stack([r, g2, b], -1), alpha[..., None]], -1)
# карпы: три рыбы → атлас 3 × (128×256), голова вверху
k4 = key(cells[4]); al = k4[..., 3]
lab, n = ndimage.label(al > 0.5); objs = ndimage.find_objects(lab)
fish = sorted([s for s in objs if (s[0].stop - s[0].start) > 150], key=lambda s: s[1].start)
print('koi', len(fish))
kat = np.zeros((256, 384, 4), np.float32)
for i, s in enumerate(fish[:3]):
    pad = 4
    sl = k4[max(0, s[0].start - pad):s[0].stop + pad, max(0, s[1].start - pad):s[1].stop + pad]
    h, w = sl.shape[:2]; scf = min(248 / h, 124 / w)
    fr = resize(sl, max(1, int(w * scf)), max(1, int(h * scf)))
    yy = (256 - fr.shape[0]) // 2; xx = 128 * i + (128 - fr.shape[1]) // 2
    kat[yy:yy + fr.shape[0], xx:xx + fr.shape[1]] = fr
save(kat, 'koi.png', 'RGBA')
save(resize(key(cells[5]), 512, 512), 'bamboo_leaves.png', 'RGBA')
g6 = key(cells[6]); save(resize(g6, 256, 256), 'grass_tuft.png', 'RGBA')

# ---------- луна (альфа — диск) и облако (альфа — яркость) ----------
mo = cells[7]; lum = mo.mean(-1)
disc = ndimage.gaussian_filter((lum > 0.06).astype(np.float32), 1.2)
moon = np.concatenate([mo, disc[..., None]], -1)
save(resize(moon, 512, 512), 'moon.png', 'RGBA')
cl = cells[8]; cl_l = cl.mean(-1)
ca = np.clip((cl_l - 0.03) / 0.6, 0, 1)
cloud = np.concatenate([np.ones_like(cl) * 0.92, ca[..., None]], -1)
# облако занимает середину ячейки — обрезаем по альфе
ys_, xs_ = np.nonzero(ca > 0.02); cloud = cloud[ys_.min():ys_.max() + 1, xs_.min():xs_.max() + 1]
save(resize(cloud, 512, 256), 'cloud.png', 'RGBA')

# ---------- кора: бесшовная + нормали ----------
def seamless(a, frac):
    h, w = a.shape[:2]; b = np.roll(np.roll(a, h // 2, 0), w // 2, 1)
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
bark = seamless(resize(cells[9], 512, 512), 0.18)
save(bark, 'bark_albedo.png'); save(normal_from(bark, 6.0), 'bark_normal.png')
print('ok')
