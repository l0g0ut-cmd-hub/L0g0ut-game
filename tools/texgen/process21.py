"""Атлас 21: IV ступень «ЯРОСТЬ» (багровое, на чёрном): узор с пульсом (4), столб в цикле (4), молнии по земле (4),
кольцо волны, трещины, угли, звезда удара. usage: cd tools/texgen && python3 process21.py"""
import numpy as np
from PIL import Image
FX = '../../Assets/_Project/Resources/FX/'
X = [0, 313, 627, 940, 1254]
a = np.asarray(Image.open('atlas_src/atlas21_rage.png').convert('RGB')).astype(np.float32) / 255
def cut(c, y0, y1, edge=6.0):
    H = a.shape[0]
    out = np.zeros((y1 - y0, X[c + 1] - X[c], 3), np.float32)
    s0, s1 = max(0, y0), min(H, y1)
    out[s0 - y0:s1 - y0] = a[s0:s1, X[c]:X[c + 1]]
    v = np.clip((out - 0.03) / 0.97, 0, 1)
    h, w = v.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    m = np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / edge, 0, 1)
    return v * m[..., None]
def img(v, w, h): return Image.fromarray((np.clip(np.dstack([v, v.max(-1)]), 0, 1) * 255).astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)
def radial(v, stretch=1.0, t=0.08):
    h, w = v.shape[:2]
    if stretch != 1.0:
        v = np.asarray(Image.fromarray((v * 255).astype(np.uint8)).resize((w, int(h * stretch)), Image.LANCZOS)).astype(np.float32) / 255
        hh = v.shape[0]; o = (hh - w) // 2
        v = v[o:o + w] if hh >= w else np.pad(v, (((w - hh) // 2, w - hh - (w - hh) // 2), (0, 0), (0, 0)))
    n = min(v.shape[0], v.shape[1]); oy = (v.shape[0] - n) // 2; ox = (v.shape[1] - n) // 2; v = v[oy:oy + n, ox:ox + n]
    yy, xx = np.mgrid[0:n, 0:n]; r = np.hypot(xx - n / 2, yy - n / 2) / (n / 2)
    return v * np.clip((1.0 - r) / 0.08, 0, 1)[..., None]
# узор: кадры пульса + отдельная текстура (2-й кадр)
S = Image.new('RGBA', (512 * 4, 512))
for c in range(4):
    v = radial(cut(c, 0, 313)); S.paste(img(v, 512, 512), (c * 512, 0))
    if c == 1: img(v, 512, 512).save(FX + 'sigil_rage.png')
S.save(FX + 'sigil_rage_pulse.png')
# столб: основание по яркому диску
P = Image.new('RGBA', (256 * 4, 256))
for c in range(4):
    lum = a[520:632, X[c]:X[c + 1]].max(-1).mean(1); base = 520 + int(np.argmax(lum))
    P.paste(img(cut(c, base + 14 - 330, base + 14), 256, 256), (c * 256, 0))
P.save(FX + 'pillar_loop_rage.png')
L = Image.new('RGBA', (256 * 4, 256))
for c in range(4): L.paste(img(radial(cut(c, 625, 925)), 256, 256), (c * 256, 0))
L.save(FX + 'rage_lightning.png')
img(radial(cut(0, 918, 1254), 1.45), 512, 512).save(FX + 'rage_ring.png')
img(radial(cut(1, 918, 1254)), 512, 512).save(FX + 'rage_cracks.png')
img(cut(2, 918, 1254), 256, 256).save(FX + 'rage_embers.png')
img(radial(cut(3, 918, 1254)), 256, 256).save(FX + 'rage_star.png')
print('ok')
