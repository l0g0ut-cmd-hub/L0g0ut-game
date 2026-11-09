"""Кровавая волна (2026-11-09): листы владельца из ChatGPT → атласы игры.

Запуск: python3 tools/fx/prep_blood_wave.py <pillar.png> <sigil.png> <launch.png> <hit.png> <fly.png>

Что делает:
  * чёрный фон без серой дымки (порог), края каждой клетки мягко гасятся — соседние кадры не просвечивают;
  * кадры перекладываются в атлас со сторонами степени двойки без растяжения:
      bwave_fly     8×4 кадров 192×256 → клетки 256×256 (кадр по центру), 2048×1024
      bwave_launch  6×4 кадров 256×256 → сетка 8×4 (24 заняты), 2048×1024
      bwave_hit     6×4 кадров 256×256 → сетка 8×4 (24 заняты), 2048×1024
      pillar_loop_blood 4×4 кадров 256×384 → клетки 256×512 (низ кадра на низу клетки), 1024×2048
  * sigil_blood 1024² и sigil_blood_early (только ядро узора — первая ступень);
  * UI/btn_wave_icon — значок кнопки «ВОЛНА»: кадр полёта с прозрачностью по яркости (интерфейс не умеет сложение цвета).
"""
import sys
import numpy as np
from PIL import Image

OUT = "Assets/_Project/Resources/FX/"


def load(p):
    a = np.asarray(Image.open(p).convert("RGB")).astype(np.float32)
    # убрать серую дымку фона: всё темнее порога — в ноль, остальное растянуть
    lo = 7.0
    return np.clip((a - lo) * 255.0 / (255.0 - lo), 0, 255)


def feather(cell, px=6):
    h, w, _ = cell.shape
    y = np.minimum(np.arange(h), np.arange(h)[::-1]) / px
    x = np.minimum(np.arange(w), np.arange(w)[::-1]) / px
    m = np.clip(np.minimum(y[:, None], x[None, :]), 0, 1)
    m = m * m * (3 - 2 * m)
    return cell * m[..., None]


def repack(a, cols, rows, cw, ch, ocols, orows, ow, oh, anchor="center"):
    out = np.zeros((orows * oh, ocols * ow, 3), np.float32)
    for i in range(cols * rows):
        cx, cy = i % cols, i // cols
        cell = feather(a[cy * ch:(cy + 1) * ch, cx * cw:(cx + 1) * cw])
        ox, oy = i % ocols, i // ocols
        x0 = ox * ow + (ow - cw) // 2
        y0 = oy * oh + ((oh - ch) // 2 if anchor == "center" else oh - ch)
        out[y0:y0 + ch, x0:x0 + cw] = cell
    return out


def save(a, name):
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(OUT + name + ".png", optimize=True)
    print(name, a.shape[1], "x", a.shape[0])


def main():
    pillar, sigil, launch, hit, fly = sys.argv[1:6]
    flyp = repack(load(fly), 8, 4, 192, 256, 8, 4, 256, 256)
    save(flyp, "bwave_fly")
    fr = 12
    cell = flyp[(fr // 8) * 256:(fr // 8 + 1) * 256, (fr % 8) * 256:(fr % 8 + 1) * 256]
    al = np.clip(cell.max(2) / 255.0 * 1.6, 0, 1)
    rgb = np.where(al[..., None] > 1e-3, np.clip(cell / 255.0 / np.maximum(al[..., None], 1e-3), 0, 1), 0)
    icon = np.dstack([rgb, al]) * 255
    Image.fromarray(icon.astype(np.uint8), "RGBA").save("Assets/_Project/Resources/UI/btn_wave_icon.png", optimize=True)
    print("btn_wave_icon 256 x 256")
    save(repack(load(launch), 6, 4, 256, 256, 8, 4, 256, 256), "bwave_launch")
    save(repack(load(hit), 6, 4, 256, 256, 8, 4, 256, 256), "bwave_hit")
    save(repack(load(pillar), 4, 4, 256, 384, 4, 4, 256, 512, anchor="bottom"), "pillar_loop_blood")
    s = Image.fromarray(load(sigil).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS)
    sa = np.asarray(s).astype(np.float32)
    save(sa, "sigil_blood")
    # ядро: мечи, цветок и полумесяцы в середине, обод и лучи погашены
    yy, xx = np.mgrid[0:1024, 0:1024]
    r = np.hypot(xx - 511.5, yy - 511.5) / 512.0
    m = np.clip((0.5 - r) / 0.14, 0, 1)
    m = m * m * (3 - 2 * m)
    save(sa * m[..., None], "sigil_blood_early")


if __name__ == "__main__":
    main()
