"""Атлас 38: 8 лепестков сакуры и 8 листьев клёна на пурпуре → FX/petals8.png и FX/leaves8.png (4×2 по 256).
Ключ строже обычного: розовое основание лепестка близко к пурпуру — непрозрачно всё, где пурпурности меньше 0,45."""
import numpy as np
from PIL import Image
from scipy import ndimage
a = np.asarray(Image.open('atlas_src/atlas38_petals_leaves.png').convert('RGB')).astype(np.float32) / 255
G = [0, 313, 627, 940, 1254]
def key(c):
    m = np.minimum(c[..., 0], c[..., 2]) - c[..., 1]
    al = np.clip((0.9 - m) / 0.45, 0, 1)
    al = ndimage.binary_erosion(al > 0.5, iterations=1) * 0.0 + al   # (оставлено мягким)
    rgb = np.clip((c - (1 - al[..., None]) * np.array([1, 0, 1])) / np.maximum(al[..., None], 1e-3), 0, 1)
    edge = (al < 0.98)[..., None]   # пурпурная кайма только на краю: подтянуть синий к зелёному
    fix = rgb.copy(); fix[..., 2] = np.minimum(rgb[..., 2], np.maximum(rgb[..., 1] * 1.25, rgb[..., 2] * 0.6))
    rgb = np.where(edge, fix, rgb)
    return np.dstack([rgb, al])
for name, rows in [('petals8', (0, 1)), ('leaves8', (2, 3))]:
    S = Image.new('RGBA', (1024, 512), (0, 0, 0, 0))
    for k, r in enumerate(rows):
        for c in range(4):
            o = key(a[G[r] + 4:G[r + 1] - 4, G[c] + 4:G[c + 1] - 4])
            if name == 'petals8': o[..., :3] = o[..., :3] * 0.78 + 0.22   # лепестки сакуры — бледнее, почти белые к краю
            S.paste(Image.fromarray((o * 255).astype(np.uint8), 'RGBA').resize((256, 256), Image.LANCZOS), (c * 256, k * 256))
    S.save('../../Assets/_Project/Resources/FX/' + name + '.png'); print(name)
