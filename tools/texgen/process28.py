"""Атласы 28–33 — больше кадров (на чёрном, аддитив): столб «Ярости» 16 кадров (4×4), дух 16 (4×4), пламя ауры вокруг
фигуры 16 (4×4), столбы лёд/огонь/солнце/гроза по 8 (4×2), лечение 8 и втягивание 8 (4×2).
Столбы выровнены по светящемуся диску у земли — основание не прыгает. usage: cd tools/texgen && python3 process28.py"""
import numpy as np
from PIL import Image
FX = '../../Assets/_Project/Resources/FX/'
G = [0, 313, 627, 940, 1254]
def load(n): return np.asarray(Image.open('atlas_src/' + n + '.png').convert('RGB')).astype(np.float32) / 255
def edge(h, w, e=6.0):
    yy, xx = np.mgrid[0:h, 0:w]; return np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / e, 0, 1)
def cut(a, x0, x1, y0, y1):
    H = a.shape[0]; out = np.zeros((y1 - y0, x1 - x0, 3), np.float32)
    s0, s1 = max(0, y0), min(H, y1); out[s0 - y0:s1 - y0] = a[s0:s1, x0:x1]
    v = np.clip((out - 0.03) / 0.97, 0, 1) * edge(y1 - y0, x1 - x0)[..., None]
    return np.dstack([v, v.max(-1)])
def to_img(o, w=256, h=256): return Image.fromarray((np.clip(o, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((w, h), Image.LANCZOS)
def sheet(frames, cols, name):
    rows = (len(frames) + cols - 1) // cols
    S = Image.new('RGBA', (256 * cols, 256 * rows), (0, 0, 0, 0))
    for k, o in enumerate(frames): S.paste(to_img(o), ((k % cols) * 256, (k // cols) * 256))
    S.save(FX + name + '.png'); print(name, S.size)
def grid(a, r, c): return cut(a, G[c], G[c + 1], G[r], G[r + 1])
def pillar(a, r, c, up=320):
    """кадр столба: основание (яркая полоса диска в нижней трети ячейки) на одной высоте во всех кадрах"""
    x0, x1 = G[c], G[c + 1]
    lo = G[r] + int((G[r + 1] - G[r]) * 0.6)
    lum = a[lo:min(a.shape[0], G[r + 1] + 6), x0:x1].max(-1).mean(1)
    on = np.nonzero(lum > 0.3 * lum.max())[0]
    base = lo + int(on[-1])   # нижний край светящегося диска — устойчивее яркой полосы (у эллипса их несколько)
    return cut(a, x0, x1, base + 6 - up, base + 6)
r28 = load('atlas28_rage_pillar16')
sheet([pillar(r28, i // 4, i % 4) for i in range(16)], 4, 'pillar_loop_rage')
sheet([grid(load('atlas29_spirit16'), i // 4, i % 4) for i in range(16)], 4, 'fx_spirit')
sheet([grid(load('atlas30_aura16'), i // 4, i % 4) for i in range(16)], 4, 'aura_body')
p1 = load('atlas31_pillars_ice_fire'); p2 = load('atlas32_pillars_sun_storm')
sheet([pillar(p1, i // 4, i % 4) for i in range(8)], 4, 'pillar_loop_ice')
sheet([pillar(p1, 2 + i // 4, i % 4) for i in range(8)], 4, 'pillar_loop_flame')
sheet([pillar(p2, i // 4, i % 4) for i in range(8)], 4, 'pillar_loop_sun')
sheet([pillar(p2, 2 + i // 4, i % 4) for i in range(8)], 4, 'pillar_loop_storm')
h = load('atlas33_heal_inhale')
sheet([grid(h, i // 4, i % 4) for i in range(8)], 4, 'fx_heal')
sheet([grid(h, 2 + i // 4, i % 4) for i in range(8)], 4, 'charge_inhale')
print('ok')
