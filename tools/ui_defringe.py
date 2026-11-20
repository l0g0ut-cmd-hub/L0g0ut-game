"""Снимает «красную кайму» (запечённый чистый красный ореол вокруг объектов прозрачных генераций ChatGPT)."""
import sys, numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage
def defringe(im, bg=None):
    a = np.asarray(im.convert('RGBA')).astype(float)
    rgb, al = a[..., :3], a[..., 3]
    if bg is not None:   # непрозрачный фон (белый) → альфа
        d = np.abs(rgb - np.array(bg)).max(2); m = d > 24
        m = ndimage.binary_opening(m, iterations=1)
        lab, n = ndimage.label(m); sz = ndimage.sum(m, lab, range(1, n + 1)); m = np.isin(lab, 1 + np.where(sz > 1500)[0]); m = ndimage.binary_fill_holes(m)
        al = m * 255.0
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    halo = (r > 90) & (g < 0.22 * r) & (b < 0.22 * r)         # чистый красный
    halo |= (r > 120) & (g > 0.75 * r) & (b < 0.25 * r) & (al > 0)   # и жёлто-зелёный ореол
    # ореол — только у края силуэта (внутренние красные детали не трогаем)
    solid = (al > 128) & ~halo
    core = ndimage.binary_erosion(solid, iterations=3)
    near_edge = ~ndimage.binary_dilation(core, iterations=1) | ~ndimage.binary_erosion(al > 128, iterations=6)
    kill = halo & near_edge
    # распространяем по связным областям ореола, касающимся прозрачности
    lab, n = ndimage.label(halo)
    touch = np.unique(lab[ndimage.binary_dilation(al < 20, iterations=2) & halo])
    kill |= np.isin(lab, touch[touch > 0])
    al = np.where(kill, 0, al)
    m = al > 128
    m = ndimage.binary_opening(m, iterations=1)
    al2 = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))).astype(float)
    al = np.minimum(al, al2 + 0)
    return Image.fromarray(np.dstack([rgb, al]).astype(np.uint8), 'RGBA')
if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]; bg = tuple(map(int, sys.argv[3].split(','))) if len(sys.argv) > 3 else None
    defringe(Image.open(src), bg).save(dst)
