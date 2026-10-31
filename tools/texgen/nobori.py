"""Знамёна-нобори вдоль ограды (256×1024): некрашеный хлопок, индиговая кайма сверху и вдоль древка, крупные чёрные
кандзи сверху вниз — «武運長久» (воинской удачи навеки) и «八幡大菩薩» (Хатиман, бог воинов). Ткань выцветшая, с
пятнами и потёртостями, низ слегка измочален (альфа — рваный край). Шрифт: Noto Serif CJK JP Bold (SIL OFL).
Запуск: python3 nobori.py OUTDIR [font.ttc]"""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
FONT = sys.argv[2] if len(sys.argv) > 2 else '/usr/share/fonts/opentype/noto/NotoSerifCJK-Bold.ttc'
W, H = 256, 1024
for name, text, band in (('nobori_buun', '武運長久', (0.12, 0.16, 0.32)), ('nobori_hachiman', '八幡大菩薩', (0.38, 0.06, 0.05))):
    rng = np.random.default_rng(len(text))
    cloth = np.ones((H, W, 3)) * np.array([0.86, 0.82, 0.72])
    stain = ndimage.gaussian_filter(rng.random((H, W)), 14); stain = (stain - stain.min()) / (stain.max() - stain.min())
    fib = ndimage.gaussian_filter(rng.random((H, W)), [0.5, 3])                        # нити ткани
    cloth *= (0.86 + 0.18 * stain[..., None]) * (0.95 + 0.1 * fib[..., None])
    # кайма: сверху и вдоль древка (левый край)
    yy, xx = np.mgrid[0:H, 0:W]
    bandm = ((yy < 70) | (xx < 22)).astype(float)
    cloth = cloth * (1 - bandm[..., None]) + np.array(band) * (0.8 + 0.3 * stain[..., None]) * bandm[..., None]
    # кольца-петли на древко
    for y in range(110, H - 60, 140):
        cloth[y:y + 18, 0:22] *= 0.55
    # кандзи
    n = len(text)
    size = int(min(190, (H - 150) / n * 0.92))
    txt = Image.new('L', (W, H), 0); d = ImageDraw.Draw(txt)
    ft = ImageFont.truetype(FONT, size, index=0)
    y0 = 95 + ((H - 150) - n * size * 1.04) / 2
    for i, ch in enumerate(text):
        l, t, r, b = d.textbbox((0, 0), ch, font=ft)
        d.text(((W + 22 - (r - l)) / 2 - l, y0 + i * size * 1.04 - t), ch, font=ft, fill=255)
    ink = np.asarray(txt.filter(ImageFilter.GaussianBlur(0.8)), dtype=np.float32) / 255
    ink *= 0.85 + 0.25 * fib                                                          # тушь впиталась неровно
    cloth = cloth * (1 - ink[..., None] * 0.92) + np.array([0.04, 0.035, 0.03]) * ink[..., None] * 0.92
    # рваный низ
    edge = H - 18 - (ndimage.gaussian_filter1d(rng.random(W), 3) * 40)
    alpha = (yy < edge[None, :]).astype(float)
    img = np.dstack([np.clip(cloth, 0, 1), alpha])
    Image.fromarray((img * 255).astype(np.uint8), 'RGBA').save(os.path.join(out, name + '.png'))
print('ok')
