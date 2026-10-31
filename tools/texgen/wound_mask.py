"""Маска ран и брызг крови на персонажах (шейдер BladePath/Character, _WoundMask): бесшовная 256×256, значение — «порог».
Шейдер заливает кровью места, где маска ниже текущего _Wound: с потерей здоровья сначала проступают отдельные пятна
и брызги, потом они растут и сливаются, от пятен вниз тянутся потёки (кровь стекает по ткани и доспеху).

    python3 tools/texgen/wound_mask.py <out_dir>
"""
import sys, os
import numpy as np
from PIL import Image

OUT = sys.argv[1]
N = 256
rng = np.random.default_rng(31)


def periodic_noise(scale, seed):
    """Бесшовный шум: белый шум, отфильтрованный в частотной области (периодичен по построению)."""
    r = np.random.default_rng(seed).standard_normal((N, N))
    f = np.fft.fft2(r)
    ky = np.fft.fftfreq(N)[:, None]; kx = np.fft.fftfreq(N)[None, :]
    k = np.sqrt(kx * kx + ky * ky)
    f *= np.exp(-(k * scale) ** 2)
    n = np.real(np.fft.ifft2(f))
    return (n - n.min()) / (n.max() - n.min())


base = periodic_noise(22, 1) * 0.6 + periodic_noise(8, 2) * 0.3 + periodic_noise(3, 3) * 0.1
# пятна-брызги: круглые «капли» с ореолом мелких точек (периодически — по тору)
ys, xs = np.mgrid[0:N, 0:N]
spl = np.zeros((N, N))
for i in range(26):
    cx, cy = rng.random() * N, rng.random() * N
    rad = rng.uniform(4, 13)
    dx = (xs - cx + N / 2) % N - N / 2; dy = (ys - cy + N / 2) % N - N / 2
    d = np.sqrt(dx * dx + dy * dy)
    spl = np.maximum(spl, np.clip(1 - d / rad, 0, 1) ** 0.7 * rng.uniform(0.5, 1.0))
    for j in range(rng.integers(3, 8)):
        a = rng.random() * 6.283; r = rad * rng.uniform(1.3, 2.6); rr = rng.uniform(1, 2.6)
        px, py = cx + np.cos(a) * r, cy + np.sin(a) * r
        ddx = (xs - px + N / 2) % N - N / 2; ddy = (ys - py + N / 2) % N - N / 2
        spl = np.maximum(spl, np.clip(1 - np.sqrt(ddx * ddx + ddy * ddy) / rr, 0, 1) * 0.6)
# потёки вниз от пятен (v вниз = −y в UV; в текстуре строка 0 — верх, тянем «вниз» по строкам)
drip = spl.copy()
for s in range(1, 40):
    drip = np.maximum(drip, np.roll(spl, s, axis=0) * (1 - s / 40) * (np.sin(xs * 0.9 + s * 0.05) > 0.6) * 0.8)
m = 1.0 - np.maximum(drip * 0.85, 0) - (1 - base) * 0.35
# выравнивание по рангу: доля залитого = порогу (_Wound 0,2 → 20% поверхности)
order = np.argsort(m, axis=None)
eq = np.empty(N * N); eq[order] = np.linspace(0, 1, N * N)
m = eq.reshape(N, N)
os.makedirs(OUT, exist_ok=True)
Image.fromarray((m * 255).astype(np.uint8), "L").save(os.path.join(OUT, "wound_mask.png"))
print("wound_mask.png", "coverage@0.2:", round(float((m < 0.2).mean()), 3), "@0.35:", round(float((m < 0.35).mean()), 3))
