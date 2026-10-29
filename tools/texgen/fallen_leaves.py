"""Атлас опавших листьев клёна 2×2 (256×256): из листа референса (maple_leaf.png) — четыре оттенка опавшей листвы:
алый, оранжево-красный, тёмно-винный (намокший), бурый (сухой). Обрезки соседних листьев убираются (одна связная область).

    python3 tools/texgen/fallen_leaves.py <maple_leaf.png> <out.png>
"""
import sys, colorsys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

src = Image.open(sys.argv[1]).convert("RGBA")
a = np.array(src, np.float32) / 255.0
lab, n = ndimage.label(a[..., 3] > 0.2)
if n > 1:
    sizes = ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))
    keep = 1 + int(np.argmax(sizes))
    mask = ndimage.binary_dilation(lab == keep, iterations=2)
    a[..., 3] *= mask
leaf = Image.fromarray((a * 255).astype(np.uint8), "RGBA").resize((128, 128), Image.LANCZOS)
leaf = leaf.filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=1))
L = np.array(leaf, np.float32) / 255.0


def variant(hue_shift, sat_mul, val_mul):
    rgb = L[..., :3].reshape(-1, 3)
    out = np.empty_like(rgb)
    for i, (r, g, b) in enumerate(rgb):
        h, s, v = colorsys.rgb_to_hsv(r, g, b)
        out[i] = colorsys.hsv_to_rgb((h + hue_shift) % 1.0, min(1.0, s * sat_mul), min(1.0, v * val_mul))
    return np.concatenate([out.reshape(L.shape[0], L.shape[1], 3), L[..., 3:]], -1)


cells = [variant(0.0, 1.0, 0.95), variant(0.035, 1.05, 1.1), variant(-0.01, 0.9, 0.62), variant(0.06, 0.6, 0.72)]
atlas = np.zeros((256, 256, 4), np.float32)
for k, c in enumerate(cells):
    y, x = (k // 2) * 128, (k % 2) * 128
    atlas[y:y + 128, x:x + 128] = c
# цвет под прозрачными краями — как у листа (без тёмной каймы на мипах)
rgb = atlas[..., :3]; al = atlas[..., 3]
filled = rgb.copy()
for ch in range(3):
    m = al > 0.3
    idx = ndimage.distance_transform_edt(~m, return_distances=False, return_indices=True)
    filled[..., ch] = rgb[..., ch][tuple(idx)]
atlas[..., :3] = filled
Image.fromarray((np.clip(atlas, 0, 1) * 255).astype(np.uint8), "RGBA").save(sys.argv[2])
print("ok", sys.argv[2])
