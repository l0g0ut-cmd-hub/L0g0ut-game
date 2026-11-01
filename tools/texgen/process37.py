"""Атлас 37: линии скорости тушью (2×2 варианта для дрожания) на пурпуре → UI/speed_lines.png (2×2 по 512)."""
import numpy as np
from PIL import Image
a = np.asarray(Image.open('atlas_src/atlas37_speedlines.png').convert('RGB')).astype(np.float32) / 255
E = [0, 627, 1254]
S = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
for i in range(4):
    r, c = divmod(i, 2); cc = a[E[r]:E[r + 1], E[c]:E[c + 1]]
    m = np.minimum(cc[..., 0], cc[..., 2]) - cc[..., 1]
    al = np.clip((0.86 - m) / 0.78, 0, 1)
    v = np.zeros_like(al)   # тушь — чёрная, альфа — покрытие
    S.paste(Image.fromarray((np.dstack([v, v, v, al]) * 255).astype(np.uint8), 'RGBA').resize((512, 512), Image.LANCZOS), (c * 512, r * 512))
S.save('../../Assets/_Project/Resources/UI/speed_lines.png'); print('ok')
