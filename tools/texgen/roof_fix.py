import numpy as np
from PIL import Image
from scipy import ndimage
exec(open('process.py').read().split("out = {}")[0])   # функции и ячейки
a = resize(cells[2], 512)
lum = a.mean(-1)
def period(sig, lo, hi):
    s = sig - sig.mean(); ac = np.correlate(s, s, 'full')[len(s) - 1:]
    return lo + int(np.argmax(ac[lo:hi]))
P = period(lum.mean(0), 30, 140); Q = period(lum.mean(1), 30, 160)
print('period x', P, 'y', Q)
def seamless2(a, frac, dx, dy):
    h, w = a.shape[:2]
    b = np.roll(np.roll(a, dy, 0), dx, 1)
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.minimum(np.minimum(xx, w - 1 - xx) / (w * frac), np.minimum(yy, h - 1 - yy) / (h * frac))
    m = np.clip(d, 0, 1); m = m * m * (3 - 2 * m)
    return a * m[..., None] + b * (1 - m[..., None])
dx = int(round(256 / P)) * P; dy = int(round(256 / Q)) * Q
r = seamless2(a, 0.12, dx, dy)
save(r, 'roof_albedo.png'); save(normal_from(r, 5.0), 'roof_normal.png')
im = Image.open('roof_albedo.png').resize((256, 256)); t = Image.new('RGB', (512, 512))
for i in range(2):
    for j in range(2): t.paste(im, (i * 256, j * 256))
t.save('../roof_check.jpg')
