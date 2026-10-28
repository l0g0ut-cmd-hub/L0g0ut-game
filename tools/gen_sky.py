"""Ночное небо-панорама (equirect 2048x1024): градиент, звёзды, слои гор. Запуск: python3 tools/gen_sky.py"""
import numpy as np
from PIL import Image
W, H = 2048, 1024
rng = np.random.default_rng(7)
y = np.linspace(0, 1, H)[:, None]            # 0 = зенит, 0.5 = горизонт, 1 = надир
x = np.linspace(0, 2 * np.pi, W, endpoint=False)[None, :]
up = np.clip((0.5 - y) / 0.5, 0, 1)           # 1 в зените, 0 на горизонте
top = np.array([0.012, 0.022, 0.07]); hor = np.array([0.16, 0.18, 0.30])
img = (hor[None, None, :] * (1 - up[..., None] ** 0.55) + top[None, None, :] * (up[..., None] ** 0.55)) * np.ones((H, W, 1))
# тёплое зарево у горизонта
glow = np.exp(-((y - 0.5) / 0.05) ** 2)[..., None] * np.array([0.10, 0.05, 0.04])
img += glow
# звёзды
n = 2600
sx = rng.integers(0, W, n); sy = (rng.random(n) ** 0.7 * 0.46 * H).astype(int)
for a, b in zip(sx, sy):
    br = rng.random() ** 2 * 0.9 + 0.1
    col = np.array([0.8, 0.85, 1.0]) * br * (1 - 0.6 * (1 - (0.5 * H - b) / (0.5 * H)) ** 3)
    img[b, a] += col
    if br > 0.7 and b > 0 and a + 1 < W: img[b, a + 1] += col * 0.4; img[b, a - 1] += col * 0.4
# горы: слои (дальний светлее, туманнее)
def ridge(seed, base, amp, harmonics):
    r = np.random.default_rng(seed)
    s = np.zeros(W)
    xs = x[0]
    for k in range(1, harmonics + 1):
        s += r.random() * np.sin(xs * (k + r.integers(1, 4)) + r.random() * 6.28) / (k ** 0.85)
    s = (s - s.min()) / (s.max() - s.min() + 1e-6)
    return base + amp * s
layers = [(11, 0.455, 0.045, 9, np.array([0.10, 0.12, 0.20])), (23, 0.475, 0.035, 12, np.array([0.06, 0.07, 0.13])), (37, 0.495, 0.025, 16, np.array([0.035, 0.04, 0.08]))]
rows = np.arange(H)[:, None] / H
for seed, base, amp, hm, col in layers:
    top_y = 1.0 - (ridge(seed, 0, 1, hm) * amp * 2.2) + 0   # размерность: доля высоты
    edge = (0.52 - ridge(seed, 0.0, amp * 2.2, hm))[None, :]
    mask = (rows > edge) & (rows < 0.52)
    img = np.where(mask[..., None], col[None, None, :] * (1 + 0.15 * (0.52 - rows)[..., None] * 8), img)
# ниже горизонта — цвет тумана арены
below = (rows >= 0.52)
img = np.where(below[..., None], np.array([0.07, 0.08, 0.12])[None, None, :], img)
Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save('Assets/_Project/Art/Textures/sky_night.png')
print('ok')
