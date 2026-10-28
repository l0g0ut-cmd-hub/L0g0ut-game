"""
Лист 2 (tools/ref_sheet2.png): 8 кнопок ударов и 9 текстур окружения.
Текстуры делаются бесшовными (смешивание со сдвигом на полпериода по маске) и получают карты нормалей из яркости.
python3 tools/extract_ref2.py
"""
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = np.asarray(Image.open('tools/ref_sheet2.png').convert('RGB')).astype(np.float32)
UI = 'Assets/_Project/Resources/UI/'
TX = 'Assets/_Project/Art/Textures/'

# ---------- кнопки
centers = [110, 292, 473, 656, 838, 1019, 1202, 1385]
names = ['btn_overhead', 'btn_rising', 'btn_legs', 'btn_thrust', 'btn_slash', 'btn_energy', 'btn_super', 'btn_base']
R = 84
for cx, name in zip(centers, names):
    c = SRC[125 - R:125 + R, cx - R:cx + R]
    h, w = c.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    # точный центр/радиус по яркой оправе
    lum = c.max(axis=2)
    ring = lum > 70
    ys, xs = np.where(ring)
    cy, cx2 = (ys.min() + ys.max()) / 2, (xs.min() + xs.max()) / 2
    rr = max(ys.max() - ys.min(), xs.max() - xs.min()) / 2 + 1
    d = np.hypot(yy - cy, xx - cx2)
    a = np.clip(rr - d, 0, 1)
    s = int(rr * 2) + 2
    y0, x0 = int(cy - rr) - 1, int(cx2 - rr) - 1
    img = np.dstack([c, a * 255]).astype(np.uint8)[max(0, y0):y0 + s, max(0, x0):x0 + s]
    Image.fromarray(img, 'RGBA').resize((192, 192), Image.LANCZOS).save(UI + name + '.png')

# ---------- текстуры
boxes = {
    'stone':      (15, 257, 315, 600),
    'roof':       (328, 257, 610, 600),
    'lacquer':    (622, 257, 906, 600),
    'wood':       (916, 257, 1207, 600),
    'stoneblock': (1218, 257, 1517, 600),
    'plaster':    (15, 614, 316, 963),
    'gravel':     (328, 614, 610, 963),
    'bark':       (622, 614, 906, 963),
    'blossom':    (916, 614, 1207, 963),
}

def seamless(img):
    h, w = img.shape[:2]
    sh = np.roll(np.roll(img, h // 2, 0), w // 2, 1)
    y = np.abs(np.linspace(-1, 1, h))[:, None]; x = np.abs(np.linspace(-1, 1, w))[None, :]
    m = np.clip(1 - np.maximum(x, y), 0, 1) ** 0.6           # 1 в центре, 0 у краёв
    m = ndimage.gaussian_filter(m, 4)[..., None]
    return img * m + sh * (1 - m)

def normal_from_height(hgt, strength):
    gx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * strength
    gy = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) * strength
    n = np.dstack([-gx, gy, np.ones_like(hgt)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return ((n * 0.5 + 0.5) * 255).astype(np.uint8)

for name, (x0, y0, x1, y1) in boxes.items():
    c = SRC[y0 + 6:y1 - 6, x0 + 6:x1 - 6]
    if name == 'wood':
        c = np.rot90(c)                     # в игре волокна идут вертикально (см. EnvMesh swap)
    c = np.asarray(Image.fromarray(c.astype(np.uint8)).resize((512, 512), Image.LANCZOS)).astype(np.float32)
    s = seamless(c)
    Image.fromarray(np.clip(s, 0, 255).astype(np.uint8)).save(TX + name + '_albedo.png')
    if name not in ('lacquer', 'blossom'):
        hgt = ndimage.gaussian_filter(s.mean(axis=2) / 255.0, 1.0)
        Image.fromarray(normal_from_height(hgt, 6.0 if name != 'gravel' else 4.0)).save(TX + name + '_normal.png')
print('done')
