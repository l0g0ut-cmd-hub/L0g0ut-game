"""Атласы 15–18: анимации накопления (на чёрном, аддитив).
15 — узор рисуется (12 кадров) + разряды по ободу (4, цикл); 16 — столбы ауры, 4 цвета × 4 кадра цикла;
17 — выплеск на отпускании (16 кадров, вид сбоку); 18 — втягивание (4), пыльная волна сверху (4), угли (4), молнии (4).
usage: cd tools/texgen && python3 process15.py"""
import numpy as np
from PIL import Image
FX = '../../Assets/_Project/Resources/FX/'
def load(n): return np.asarray(Image.open('atlas_src/' + n + '.png').convert('RGB')).astype(np.float32) / 255
G = [0, 313, 627, 940, 1254]
def cut(a, x0, x1, y0, y1):
    H, W = a.shape[:2]
    out = np.zeros((y1 - y0, x1 - x0, 3), np.float32)
    sy0, sy1, sx0, sx1 = max(0, y0), min(H, y1), max(0, x0), min(W, x1)
    out[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = a[sy0:sy1, sx0:sx1]
    v = np.clip((out - 0.03) / 0.97, 0, 1)
    h, w = v.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    m = np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / 6.0, 0, 1)
    return v * m[..., None]
def cell(a, i): r, c = divmod(i, 4); return cut(a, G[c], G[c + 1], G[r], G[r + 1])
def rgba(v): return np.dstack([v, v.max(-1)])
def tile(v, w, h): return Image.fromarray((np.clip(rgba(v), 0, 1) * 255).astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)
def sheet(frames, name, cols, cw=256, ch=256):
    rows = (len(frames) + cols - 1) // cols
    S = Image.new('RGBA', (cw * cols, ch * rows), (0, 0, 0, 0))
    for k, v in enumerate(frames): S.paste(tile(v, cw, ch), ((k % cols) * cw, (k // cols) * ch))
    S.save(FX + name + '.png'); print(name, S.size)
# 15: узор рисуется — 16 кадров 4×4 (порядок атласа)
a = load('atlas15_sigildraw')
sheet([cell(a, i) for i in range(16)], 'sigil_draw', 4)
# 16: столбы — по строке на цвет, основание (светящийся диск) на одной высоте
b = load('atlas16_pillars')
for r, n in enumerate(['ice', 'flame', 'sun', 'storm']):
    fr = []
    for c in range(4):
        x0, x1 = G[c], G[c + 1]
        lum = b[G[r] + 150:min(1254, G[r + 1] + 30), x0:x1].max(-1).mean(1)
        base = G[r] + 150 + int(np.argmax(lum))
        fr.append(cut(b, x0, x1, base + 14 - 330, base + 14))
    sheet(fr, 'pillar_loop_' + n, 4, 256, 256)
# 17: выплеск — 16 кадров
e = load('atlas17_eruption')
sheet([cell(e, i) for i in range(16)], 'eruption', 4)
# 18
m = load('atlas18_misc')
sheet([cell(m, i) for i in range(0, 4)], 'charge_inhale', 4)
dust = []
for i in range(4, 8):
    v = cell(m, i); h, w = v.shape[:2]
    img = Image.fromarray((v * 255).astype(np.uint8)).resize((w, int(h * 1.45)), Image.LANCZOS)   # эллипс → круг (вид сверху)
    s = np.asarray(img).astype(np.float32) / 255; hh = s.shape[0]; o = (hh - w) // 2
    s = s[o:o + w]
    yy, xx = np.mgrid[0:w, 0:w]; rr = np.hypot(xx - w / 2, yy - w / 2) / (w / 2)
    dust.append(s * np.clip((1.0 - rr) / 0.08, 0, 1)[..., None])
sheet(dust, 'shock_dust', 4)
sheet([cell(m, i) for i in range(8, 12)], 'embers_loop', 4)
sheet([cell(m, i) for i in range(12, 16)], 'lightning_burst', 4)
print('ok')
