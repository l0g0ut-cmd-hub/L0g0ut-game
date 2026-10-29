"""Текстуры клинка катаны под развёртку KatanaBuilder.BladeMesh (u — 5 граней по 0,2: обух, синоги-дзи, хира, хира, синоги-дзи;
v — от хабаки к острию): хамон (белая «изморозь» закалки вдоль кромки с волнистой границей нотарэ + гуномэ и светлой линией ниои),
дзигане (тёмная полированная сталь с древесным узором хада), синоги-дзи (воронёная, продольные риски), ёкотэ и боси у острия.

    python3 tools/texgen/katana_blade.py <out_dir>

Выход: katana_blade_albedo.png (RGB), katana_blade_ms.png (R — металл, A — гладкость).
"""
import sys, os
import numpy as np
from PIL import Image

OUT = sys.argv[1]
W, H = 512, 1024
rng = np.random.default_rng(11)
u = (np.arange(W) + 0.5) / W
v = (np.arange(H) + 0.5) / H
U, V = np.meshgrid(u, v)          # строки — вдоль клинка


def smooth_noise_1d(n, cells, seed):
    r = np.random.default_rng(seed).random(cells + 3)
    x = np.linspace(0, cells, n)
    i = np.floor(x).astype(int); f = x - i
    f = f * f * (3 - 2 * f)
    return r[i] * (1 - f) + r[i + 1] * f


def grain(h, w, sx, sy, seed):
    """Шум, вытянутый вдоль клинка (хада): крупный по v, мелкий по u."""
    g = np.random.default_rng(seed).random((max(2, int(h / sy)), max(2, int(w / sx))))
    return np.array(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255.0


face = np.minimum((U * 5).astype(int), 4)
fu = U * 5 - face                  # 0…1 поперёк грани (направление — как обход сечения)
# расстояние от кромки поперёк хиры (0 у кромки, 1 у ребра синоги): грань 2 идёт ребро→кромка, грань 3 — кромка→ребро
d_edge = np.where(face == 2, 1 - fu, np.where(face == 3, fu, 1.0))
# граница хамона: нотарэ (длинные волны) + гуномэ (полукруглые бугры) + дрожь; к острию (боси) уходит к кромке и заворачивает
notare = smooth_noise_1d(H, 5, 3)[:, None]
gunome = np.abs(np.sin(V * np.pi * 26 + smooth_noise_1d(H, 12, 4)[:, None] * 2.5)) ** 0.7
jit = smooth_noise_1d(H, 160, 5)[:, None]
line = 0.34 + 0.10 * notare + 0.10 * gunome + 0.03 * jit
tipk = np.clip((V - 0.9) / 0.1, 0, 1)
line = line * (1 - 0.55 * tipk)
line = np.where(V < 0.03, 0.26, line)   # у хабаки хамон начинается спокойно
soft = 0.035
ham = np.clip((line - d_edge) / soft + 0.5, 0, 1)                       # 1 — закалённая кромка
nioi = np.exp(-((d_edge - line) / 0.03) ** 2) * (0.6 + 0.4 * grain(H, W, 4, 6, 6))   # светлая туманная линия ниои
hada = grain(H, W, 6, 90, 7) * 0.6 + grain(H, W, 3, 30, 8) * 0.4                        # древесный узор стали
scratch = grain(H, W, 1.5, 400, 9)

albedo = np.zeros((H, W, 3), np.float32)
ms_m = np.zeros((H, W), np.float32); ms_s = np.zeros((H, W), np.float32)
jigane = np.array([0.50, 0.52, 0.56]) * (0.86 + 0.22 * hada[..., None])
hamon_c = np.array([0.88, 0.89, 0.90]) * (0.93 + 0.1 * grain(H, W, 2, 3, 10)[..., None])
hira = jigane * (1 - ham[..., None]) + hamon_c * ham[..., None]
hira = np.clip(hira + nioi[..., None] * 0.22, 0, 1)
shinogi = np.array([0.36, 0.38, 0.42]) * (0.85 + 0.25 * scratch[..., None])
spine = np.array([0.46, 0.47, 0.51]) * (0.9 + 0.15 * scratch[..., None])
is_hira = (face == 2) | (face == 3)
albedo = np.where(is_hira[..., None], hira, np.where((face == 0)[..., None], spine, shinogi))
# ёкотэ: поперечная линия у начала острия
yok = np.exp(-((V - 0.905) / 0.0025) ** 2)[..., None] * is_hira[..., None]
albedo = np.clip(albedo + yok * 0.25, 0, 1)

ms_m = np.where(is_hira, 0.95 - 0.22 * ham, np.where(face == 0, 0.9, 0.95))
ms_s = np.where(is_hira, 0.88 - 0.30 * ham + 0.04 * hada, np.where(face == 0, 0.7, 0.72 + 0.08 * scratch))

os.makedirs(OUT, exist_ok=True)
Image.fromarray((albedo * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "katana_blade_albedo.png"))
ms = np.stack([ms_m, ms_m, ms_m, ms_s], -1)
Image.fromarray((np.clip(ms, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, "katana_blade_ms.png"))
print("ok")
