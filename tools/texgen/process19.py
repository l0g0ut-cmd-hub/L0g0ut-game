"""Атласы 19 (столкновение, попадание, идеальное парирование, слом защиты) и 20 (рывок: ветер и пыль, лечение, уходящий дух).
Все на чёрном (аддитив), строки по 4 кадра → листы 4×1. usage: cd tools/texgen && python3 process19.py"""
import numpy as np
from PIL import Image
FX = '../../Assets/_Project/Resources/FX/'
X = [0, 313, 627, 940, 1254]
def load(n): return np.asarray(Image.open('atlas_src/' + n + '.png').convert('RGB')).astype(np.float32) / 255
def cut(a, c, y0, y1):
    v = np.clip((a[y0:y1, X[c]:X[c + 1]] - 0.03) / 0.97, 0, 1)
    h, w = v.shape[:2]; yy, xx = np.mgrid[0:h, 0:w]
    m = np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / 6.0, 0, 1)
    return v * m[..., None]
def sheet(a, y0, y1, name, cw=256, ch=256):
    S = Image.new('RGBA', (cw * 4, ch), (0, 0, 0, 0))
    for c in range(4):
        v = cut(a, c, y0, y1); o = np.dstack([v, v.max(-1)])
        S.paste(Image.fromarray((np.clip(o, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((cw, ch), Image.LANCZOS), (c * cw, 0))
    S.save(FX + name + '.png'); print(name, S.size)
a = load('atlas19_impacts')
sheet(a, 0, 313, 'fx_clash')
sheet(a, 313, 627, 'fx_slash_hit')
sheet(a, 600, 940, 'fx_perfect_parry')
sheet(a, 925, 1254, 'fx_guard_break')
b = load('atlas20_motion')
sheet(b, 0, 300, 'fx_dash_streak')
sheet(b, 300, 560, 'fx_dash_dust')
sheet(b, 560, 845, 'fx_heal')
sheet(b, 840, 1254, 'fx_spirit')
print('ok')
