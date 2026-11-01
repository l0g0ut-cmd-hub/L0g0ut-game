"""Атлас 36: брызги удара, 16 кадров — тушь на зелёном (в игре красится в кровь), вылет вправо из левого края по центру.
→ FX/blood_spray.png (4×4 по 256, серая маска с глянцем, как в process7). usage: python3 process36.py"""
import numpy as np
from PIL import Image
src = np.asarray(Image.open('atlas_src/atlas36_ink_spray.png').convert('RGB')).astype(np.float32) / 255
G = [0, 313, 627, 940, 1254]
def key(c):
    m = c[..., 1] - np.maximum(c[..., 0], c[..., 2])
    a = np.clip((0.8 - m) / (0.8 - 0.12), 0, 1)
    rgb = np.clip((c - (1 - a[..., None]) * np.array([0, 1, 0])) / np.maximum(a[..., None], 1e-3), 0, 1)
    rgb[..., 1] = np.minimum(rgb[..., 1], np.maximum(rgb[..., 0], rgb[..., 2]))
    return rgb, a
S = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
for i in range(16):
    r, c = divmod(i, 4)
    cc = src[G[r]:G[r + 1], G[c]:G[c + 1]]
    rgb, a = key(cc)
    h, w = a.shape; yy, xx = np.mgrid[0:h, 0:w]
    a = a * np.clip(np.minimum.reduce([xx + 3, yy, w - 1 - xx, h - 1 - yy]) / 4.0, 0, 1)   # левый край не гасим — там исток
    v = np.clip(0.55 + 0.9 * rgb.mean(-1), 0, 1)
    S.paste(Image.fromarray((np.dstack([v, v, v, a]) * 255 + 0.5).astype(np.uint8), 'RGBA').resize((256, 256), Image.LANCZOS), (c * 256, r * 256))
S.save('../../Assets/_Project/Resources/FX/blood_spray.png'); print('ok')
