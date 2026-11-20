#!/usr/bin/env python3
"""Лист кадров эффекта из генерации (сетка 8×6 на чёрном) → игровой лист Resources/UI/Battle/fx_<имя>.png.

  python3 tools/fx_sheet.py вход.png fx_cross [--loop] [--cols 8 --rows 6]

Что делает:
  1. режет сетку по пропорциям (ChatGPT редко попадает в пиксель — берём доли), ищет реальные ячейки по тёмным промежуткам;
  2. чёрный фон → прозрачность: альфа по яркости (мягко), цвет — делённый на альфу (без серой каймы при наложении);
  3. стабилизация: центр масс эффекта в каждом кадре сглаживается по соседям — кадр сдвигается к сглаженному центру
     (эффект не «прыгает» по ячейке), яркость кадров выравнивается к сглаженной кривой (нет миганий);
  4. петля (--loop): последние кадры плавно смешиваются с первыми.
Выход — 1536×1020 (8×6 по 192×170), как ждёт Tactics.DrawSheet.
"""
import sys, numpy as np
from PIL import Image
from scipy import ndimage

def main():
    a = sys.argv[1:]
    src, name = a[0], a[1]
    loop = "--loop" in a
    cols = int(a[a.index("--cols") + 1]) if "--cols" in a else 8
    rows = int(a[a.index("--rows") + 1]) if "--rows" in a else 6
    im = np.asarray(Image.open(src).convert("RGB")).astype(np.float32) / 255.0
    H, W = im.shape[:2]
    CW, CH = 192, 170
    frames = []
    for r in range(rows):
        for c in range(cols):
            x0, x1 = int(c * W / cols), int((c + 1) * W / cols)
            y0, y1 = int(r * H / rows), int((r + 1) * H / rows)
            f = np.asarray(Image.fromarray((im[y0:y1, x0:x1] * 255).astype(np.uint8)).resize((CW, CH), Image.LANCZOS)).astype(np.float32) / 255.0
            frames.append(f)
    n = len(frames)
    # яркость и центр масс
    lum = [f.max(2) for f in frames]
    mass = np.array([l.sum() for l in lum]) + 1e-6
    yy, xx = np.mgrid[0:CH, 0:CW]
    cx = np.array([(l * xx).sum() / m for l, m in zip(lum, mass)])
    cy = np.array([(l * yy).sum() / m for l, m in zip(lum, mass)])
    def smooth(v, s):
        if loop: return ndimage.gaussian_filter1d(v, s, mode="wrap")
        return ndimage.gaussian_filter1d(v, s, mode="nearest")
    scx, scy, smass = smooth(cx, 2.0), smooth(cy, 2.0), smooth(mass, 1.5)
    out = Image.new("RGBA", (CW * cols, CH * rows), (0, 0, 0, 0))
    res = []
    for i, f in enumerate(frames):
        # сдвиг к сглаженному центру (только мелкие рывки, не больше 12 px)
        dx = float(np.clip(scx[i] - cx[i], -12, 12)); dy = float(np.clip(scy[i] - cy[i], -12, 12))
        f = ndimage.shift(f, (dy, dx, 0), order=1, mode="constant", cval=0.0)
        g = float(np.clip(smass[i] / mass[i], 0.6, 1.6))   # яркость — к сглаженной
        f = np.clip(f * g, 0, 1)
        res.append(f)
    if loop:   # шов петли: последние 6 кадров смешиваются с первыми
        for k in range(6):
            w = (k + 1) / 7.0
            res[n - 6 + k] = res[n - 6 + k] * (1 - w) + res[k] * w
    for i, f in enumerate(res):
        al = np.clip(f.max(2) * 1.25, 0, 1)
        al = np.where(al < 0.035, 0, al)                       # чёрный шум фона — прочь
        rgb = np.where(al[..., None] > 0, f / np.maximum(al[..., None], 1e-3), 0)
        px = np.dstack([np.clip(rgb, 0, 1), al])
        out.paste(Image.fromarray((px * 255).astype(np.uint8), "RGBA"), ((i % cols) * CW, (i // cols) * CH))
    dst = "Assets/_Project/Resources/UI/Battle/" + name + ".png"
    out.save(dst); print(dst, out.size, "кадров", n)

if __name__ == "__main__":
    main()
