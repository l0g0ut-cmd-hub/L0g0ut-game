"""Оценка боя кистью (как отметки в японской школе каллиграфии): 秀 (превосходно), 優 (отлично), 良 (хорошо), 可 (достойно).
Красная тушь с сухой кистью: знак шрифта Noto Serif CJK JP Bold, края рваные (шум + эрозия), просветы «сухой кисти»,
вокруг — красный круг-печать (энсо) для 秀. 256×256 RGBA. Запуск: python3 rank_glyphs.py OUTDIR [font.ttc]"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
FONT = sys.argv[2] if len(sys.argv) > 2 else '/usr/share/fonts/opentype/noto/NotoSerifCJK-Bold.ttc'
N, SS = 256, 3
for name, ch, ring in (('rank_s', '秀', True), ('rank_a', '優', False), ('rank_b', '良', False), ('rank_c', '可', False)):
    im = Image.new('L', (N * SS, N * SS), 0); d = ImageDraw.Draw(im)
    ft = ImageFont.truetype(FONT, int(N * SS * 0.62), index=0)
    l, t, r, b = d.textbbox((0, 0), ch, font=ft)
    d.text(((N * SS - (r - l)) / 2 - l, (N * SS - (b - t)) / 2 - t), ch, font=ft, fill=255)
    if ring:
        # энсо: незамкнутый мазок кистью вокруг знака, толще в начале
        for k in range(0, 330, 1):
            a = math.radians(k - 60); w = (34 - 22 * k / 330) * SS / 3
            x = N * SS / 2 + math.cos(a) * N * SS * 0.44; y = N * SS / 2 + math.sin(a) * N * SS * 0.44
            d.ellipse([x - w, y - w, x + w, y + w], fill=255)
    a = np.asarray(im.resize((N, N), Image.LANCZOS), dtype=np.float32) / 255
    rng = np.random.default_rng(len(name))
    grain = ndimage.gaussian_filter(rng.random((N, N)), [0.6, 2.5])               # сухая кисть: продольные просветы
    a = np.clip(a * (0.75 + 0.6 * grain) - 0.08, 0, 1)
    a = ndimage.gaussian_filter(a, 0.6)
    col = np.dstack([np.full((N, N), 0.72), np.full((N, N), 0.06), np.full((N, N), 0.05)])
    img = np.dstack([col, np.clip(a * 1.15, 0, 1)])
    Image.fromarray((img * 255).astype(np.uint8), 'RGBA').save(os.path.join(out, name + '.png'))
print('ok')
