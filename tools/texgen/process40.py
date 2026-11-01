"""Карта похода (атласы 40–43 + три карты областей):
карты → UI/map_0..2.jpg (1536×1024); значки узлов 4×4 → UI/map_icons.png; всадник и знамя 4×4 → UI/map_hero.png;
торговец/кузнец/мико/наставник → UI/npc_0..3.jpg; сцены событий → UI/event_0..3.jpg. usage: python3 process40.py"""
import numpy as np
from PIL import Image
UI = '../../Assets/_Project/Resources/UI/'
def load(n): return np.asarray(Image.open('atlas_src/' + n + '.png').convert('RGB')).astype(np.float32) / 255
for k, n in enumerate(['map_0_forest', 'map_1_demon', 'map_2_shogun']):
    Image.open('atlas_src/' + n + '.png').convert('RGB').resize((1536, 1024), Image.LANCZOS).save(UI + 'map_%d.jpg' % k, quality=90)
def key(c, mode='warm'):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    al = np.clip((0.86 - m) / 0.74, 0, 1)
    rgb = np.clip((c - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    if mode == 'neutral':
        g = 0.3 * rgb[..., 0:1] + 0.4 * rgb[..., 1:2] + 0.3 * rgb[..., 2:3]; rgb = g * np.array([1.0, 0.97, 0.94])
    else:
        lim = np.minimum(rgb[..., 0], rgb[..., 2]); sp = np.clip(lim - rgb[..., 1], 0, 1); rgb[..., 0] -= sp; rgb[..., 2] -= sp
        if mode == 'glow':   # светящееся кольцо: полупрозрачное — в золото
            w = np.clip((0.95 - al) / 0.6, 0, 1)[..., None]
            rgb = rgb * (1 - w) + np.array([1.0, 0.8, 0.4]) * np.clip(rgb.max(-1, keepdims=True) * 1.2, 0, 1) * w
    h, w = al.shape; yy, xx = np.mgrid[0:h, 0:w]
    al = al * np.clip(np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / 4.0, 0, 1)
    return np.dstack([np.clip(rgb, 0, 1), al])
def to_img(o, s): return Image.fromarray((np.clip(o, 0, 1) * 255).astype(np.uint8), 'RGBA').resize((s, s), Image.LANCZOS)
G = [0, 313, 627, 940, 1254]
ic = load('atlas43_map_icons')
S = Image.new('RGBA', (512, 512), (0, 0, 0, 0))
for i in range(16):
    r, c = divmod(i, 4)
    mode = 'neutral' if i == 11 else 'glow' if i == 12 else 'warm'
    S.paste(to_img(key(ic[G[r] + 3:G[r + 1] - 3, G[c] + 3:G[c + 1] - 3], mode), 128), (c * 128, r * 128))
S.save(UI + 'map_icons.png')
hr = load('atlas42_map_hero')
S = Image.new('RGBA', (768, 768), (0, 0, 0, 0))
for i in range(16):
    r, c = divmod(i, 4)
    S.paste(to_img(key(hr[G[r] + 3:G[r + 1] - 3, G[c] + 3:G[c + 1] - 3]), 192), (c * 192, r * 192))
S.save(UI + 'map_hero.png')
for src, pre in [('atlas40_npc', 'npc_'), ('atlas41_events', 'event_')]:
    a = Image.open('atlas_src/' + src + '.png').convert('RGB')
    W, H = a.size; h2, w2 = H // 2, W // 2
    for i in range(4):
        r, c = divmod(i, 2)
        a.crop((c * w2 + 8, r * h2 + 8, (c + 1) * w2 - 8, (r + 1) * h2 - 8)).resize((512, 512), Image.LANCZOS).save(UI + pre + '%d.jpg' % i, quality=90)
print('ok')
