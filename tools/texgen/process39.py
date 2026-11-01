"""Атлас 39: удар кистью по диагонали, 16 кадров (тушь на пурпуре) → UI/brush_slash.png (4×4 по 256)."""
import numpy as np
from PIL import Image
a = np.asarray(Image.open('atlas_src/atlas39_brush_slash.png').convert('RGB')).astype(np.float32) / 255
G = [0, 313, 627, 940, 1254]
S = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
for i in range(16):
    r, c = divmod(i, 4); cc = a[G[r] + 2:G[r + 1] - 2, G[c] + 2:G[c + 1] - 2]
    m = np.minimum(cc[..., 0], cc[..., 2]) - cc[..., 1]
    al = np.clip((0.86 - m) / 0.7, 0, 1)
    rgb = np.clip((cc - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    lim = np.minimum(rgb[..., 0], rgb[..., 2]); sp = np.clip(lim - rgb[..., 1], 0, 1); rgb[..., 0] -= sp; rgb[..., 2] -= sp
    S.paste(Image.fromarray((np.dstack([np.clip(rgb, 0, 1), al]) * 255).astype(np.uint8), 'RGBA').resize((256, 256), Image.LANCZOS), (c * 256, r * 256))
S.save('../../Assets/_Project/Resources/UI/brush_slash.png'); print('ok')
