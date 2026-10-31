"""Кнопка «тыква-горлянка» (лечение) в стиле остальных кнопок: основа btn_base.png + светлая потёртая иконка тыквы
(две доли с перехватом, пробка, шнурок с узлом) — как рисованные иконки рывка и блока.

    python3 tools/texgen/btn_heal.py <btn_base.png> <out.png>
"""
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

base = Image.open(sys.argv[1]).convert("RGBA")
S = base.size[0]
ss = 4; W = S * ss
m = Image.new("L", (W, W), 0)
d = ImageDraw.Draw(m)
cx = W * 0.5
def circ(x, y, r): d.ellipse([x - r, y - r, x + r, y + r], fill=255)
# нижняя доля, верхняя доля (меньше), узкий перехват, длинное горлышко, пробка
circ(cx, W * 0.63, W * 0.18)
circ(cx, W * 0.375, W * 0.105)
d.polygon([(cx - W * 0.075, W * 0.44), (cx + W * 0.075, W * 0.44), (cx + W * 0.04, W * 0.48), (cx - W * 0.04, W * 0.48)], fill=255)
d.rectangle([cx - W * 0.03, W * 0.22, cx + W * 0.03, W * 0.29], fill=255)
d.rounded_rectangle([cx - W * 0.048, W * 0.175, cx + W * 0.048, W * 0.235], radius=W * 0.012, fill=255)
d.rectangle([cx - W * 0.06, W * 0.235, cx + W * 0.06, W * 0.25], fill=0)   # щель под пробкой
# шнурок на перехвате и кисточка, свисающая сбоку
d.rectangle([cx - W * 0.09, W * 0.462, cx + W * 0.09, W * 0.48], fill=0)
d.line([(cx + W * 0.07, W * 0.47), (cx + W * 0.2, W * 0.5), (cx + W * 0.25, W * 0.6)], fill=255, width=int(W * 0.024))
d.polygon([(cx + W * 0.23, W * 0.6), (cx + W * 0.28, W * 0.6), (cx + W * 0.3, W * 0.73), (cx + W * 0.21, W * 0.73)], fill=255)
# блик на нижней доле — прорезь
d.arc([cx - W * 0.13, W * 0.5, cx + W * 0.05, W * 0.76], 150, 230, fill=0, width=int(W * 0.022))
m = m.rotate(-14, resample=Image.BICUBIC, center=(cx, W * 0.5))
m = m.resize((S, S), Image.LANCZOS)
a = np.array(m, np.float32) / 255.0
rng = np.random.default_rng(5)
g = rng.random((S // 6 + 2, S // 6 + 2)).astype(np.float32)
grain = np.array(Image.fromarray((g * 255).astype(np.uint8)).resize((S, S), Image.BICUBIC), np.float32) / 255.0
fine = rng.random((S, S)).astype(np.float32)
a = a * np.clip(0.55 + 0.6 * grain, 0, 1) * (0.85 + 0.15 * fine)   # потёртость краски
lum = 0.78 + 0.12 * grain
icon = np.zeros((S, S, 4), np.float32)
icon[..., 0] = lum; icon[..., 1] = lum * 0.98; icon[..., 2] = lum * 0.94; icon[..., 3] = np.clip(a, 0, 1)
ic = Image.fromarray((icon * 255).astype(np.uint8), "RGBA")
out = base.copy(); out.alpha_composite(ic)
out.save(sys.argv[2]); print("btn_heal", out.size)
