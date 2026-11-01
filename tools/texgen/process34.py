"""Атлас 34: «Ярость» выжигается в землю — 16 кадров сверху (4×4 → FX/rage_ignite.png). usage: python3 process34.py"""
import numpy as np
from PIL import Image
a = np.asarray(Image.open('atlas_src/atlas34_rage_ignite.png').convert('RGB')).astype(np.float32) / 255
G = [0, 313, 627, 940, 1254]
S = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
for i in range(16):
    r, c = divmod(i, 4)
    v = np.clip((a[G[r]:G[r + 1], G[c]:G[c + 1]] - 0.03) / 0.97, 0, 1)
    n = min(v.shape[:2]); v = v[:n, :n]
    yy, xx = np.mgrid[0:n, 0:n]; rr = np.hypot(xx - n / 2, yy - n / 2) / (n / 2)
    v = v * np.clip((1.0 - rr) / 0.08, 0, 1)[..., None]
    S.paste(Image.fromarray((np.dstack([v, v.max(-1)]) * 255).astype(np.uint8), 'RGBA').resize((256, 256), Image.LANCZOS), (c * 256, r * 256))
S.save('../../Assets/_Project/Resources/FX/rage_ignite.png'); print('ok')
# атлас 35 (удар накопления, 16 кадров) нарезан тем же способом с краевым затуханием → FX/charge_slash.png
