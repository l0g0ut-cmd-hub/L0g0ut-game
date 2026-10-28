"""Ночное небо-панорама v2 (equirect 4096x2048, раскладка Skybox/Panoramic: u = 0.5 − atan2(z, x)/2π, v = 0.5 + высота/π).
Градиент с лунным ореолом, звёзды разной яркости и цвета (гаусс-точки), Млечный Путь с пылевыми прожилками,
перистые облака в перспективе (подсвечены луной: серебряные края к луне), 4 гряды гор с воздушной перспективой
и туманом в долинах. Ниже горизонта — цвет тумана арены. Значения — цвета как в старом gen_sky.py (sRGB-байты).
Запуск: python3 tools/gen_sky2.py [OUT]"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

OUT = sys.argv[1] if len(sys.argv) > 1 else 'Assets/_Project/Art/Textures/sky_night.png'
W, H = 4096, 2048
rng = np.random.default_rng(11)

# направления пикселей
u = (np.arange(W) + 0.5) / W
v = 1.0 - (np.arange(H) + 0.5) / H              # 1 вверху картинки
lon = (0.5 - u) * 2 * np.pi                       # atan2(z, x)
lat = (v - 0.5) * np.pi                           # высота над горизонтом
LON, LAT = np.meshgrid(lon, lat)
DX = np.cos(LAT) * np.cos(LON); DZ = np.cos(LAT) * np.sin(LON); DY = np.sin(LAT)

# луна в сцене: (4.8, 6.4, 16) от центра арены
m = np.array([4.8, 6.4, 16.0]); m /= np.linalg.norm(m)
cosm = np.clip(DX * m[0] + DY * m[1] + DZ * m[2], -1, 1)
ang = np.arccos(cosm)

def fbm2(x, y, oct=6, seed=0, lac=2.0, gain=0.5):
    """Гладкий fBm из синусоид со случайными направлениями (без швов — функция координат)."""
    r = np.random.default_rng(seed)
    acc = np.zeros_like(x); amp = 1.0; f = 1.0; tot = 0
    for o in range(oct):
        for k in range(3):
            a = r.random() * 2 * np.pi; ph = r.random() * 2 * np.pi
            acc += amp * np.sin((np.cos(a) * x + np.sin(a) * y) * f + ph)
        tot += amp * 3; amp *= gain; f *= lac
    return acc / tot * 0.5 + 0.5

up = np.clip(LAT / (np.pi / 2), 0, 1)
# --- градиент: чёрно-синий зенит → дымчатый горизонт, тёплое зарево у самой земли
zen = np.array([0.010, 0.016, 0.050]); hor = np.array([0.105, 0.115, 0.190])
t = up ** 0.45
img = hor * (1 - t[..., None]) + zen * t[..., None]
img += np.exp(-(LAT / 0.06) ** 2)[..., None] * np.array([0.06, 0.04, 0.045])
# ореол луны: узкий и широкий
img += (np.exp(-ang / 0.06) * 0.25 + np.exp(-ang / 0.35) * 0.06 + np.exp(-ang / 1.1) * 0.02)[..., None] * np.array([0.75, 0.82, 1.0])

# --- Млечный Путь: полоса вдоль большого круга, с пылевыми прожилками
gal = np.array([0.35, 0.80, -0.48]); gal /= np.linalg.norm(gal)    # нормаль к плоскости галактики
dg = DX * gal[0] + DY * gal[1] + DZ * gal[2]
band = np.exp(-(dg / 0.16) ** 2)
px, py = LON * 3.0, LAT * 3.0
dust = fbm2(px * 2.5, py * 2.5, 6, 3)
glowmw = fbm2(px, py, 5, 4)
mw = band * (0.5 + 0.8 * glowmw) * np.clip(1.3 - 1.6 * np.clip(dust - 0.45, 0, 1) * 2, 0.15, 1)
img += (mw * 0.11)[..., None] * np.array([0.85, 0.85, 1.0]) * (up[..., None] ** 0.3)

# --- облака: плоскость на высоте, перспектива к горизонту
above = DY > 0.02
cx = np.where(above, DX / np.maximum(DY, 0.02), 0) * 0.55
cz = np.where(above, DZ / np.maximum(DY, 0.02), 0) * 0.55
wx = fbm2(cx * 0.7, cz * 0.7, 4, 21) - 0.5; wz = fbm2(cx * 0.7 + 5, cz * 0.7, 4, 22) - 0.5
dens = fbm2(cx * 1.6 + wx * 1.8, cz * 0.9 + wz * 1.8, 7, 23, 2.1, 0.52)   # вытянуты по x — перистые полосы
cov = np.clip((dens - 0.47) * 4.0, 0, 1) ** 1.3
cov *= np.clip((LAT - 0.03) / 0.12, 0, 1) * np.clip(1 - (LAT - 0.9) / 0.5, 0.35, 1)   # у горизонта тают в дымке
# свет: к луне — светлее и серебристее (рассеяние вперёд), ядра темнее краёв
edge = np.clip(cov * (1 - cov) * 4, 0, 1)
phase = np.exp(-ang / 0.5) * 1.2 + 0.25
ccol = np.array([0.10, 0.11, 0.17]) * (0.6 + 0.6 * (1 - cov))[..., None] + np.array([0.55, 0.62, 0.78]) * (phase * (0.25 + 0.75 * edge) * 0.35)[..., None]
img = img * (1 - 0.85 * cov[..., None]) + ccol * (0.85 * cov[..., None])

# --- звёзды: степенная яркость, цвет по температуре, гасятся у горизонта, у луны и за облаками
n = 14000
sl = rng.random(n) * 2 * np.pi
sy = np.arcsin(rng.random(n) ** 0.85)                     # больше к зениту
br = rng.pareto(2.2, n) * 0.18 + 0.05
br = np.clip(br, 0, 2.5)
temp = rng.random(n)
scol = np.stack([0.85 + 0.15 * temp, 0.88 + 0.06 * (1 - abs(temp - 0.5)), 1.0 - 0.25 * temp], -1)
star = np.zeros((H, W, 3))
su = (0.5 - sl / (2 * np.pi)) % 1.0 * W
sv = (1 - (sy / np.pi + 0.5)) * H
xi = np.clip(su.astype(int), 0, W - 1); yi = np.clip(sv.astype(int), 0, H - 1)
np.add.at(star, (yi, xi), scol * br[:, None])
star = ndimage.gaussian_filter(star, (0.9, 0.9, 0))
big = br > 0.6
star2 = np.zeros((H, W, 3)); np.add.at(star2, (yi[big], xi[big]), scol[big] * br[big, None] * 0.6)
star += ndimage.gaussian_filter(star2, (2.4, 2.4, 0))
ext = np.clip(LAT / 0.25, 0, 1) ** 1.5 * np.clip(ang / 0.5, 0, 1) ** 2 * (1 - cov)
img += star * 1.6 * ext[..., None]

# --- горы: 4 гряды, дальние светлее и синее, туман в долинах; освещённый луной склон чуть светлее
def ridge(seed, amp, base, rough):
    r = np.random.default_rng(seed)
    s = np.zeros(W)
    xs = np.linspace(0, 2 * np.pi, W, endpoint=False)
    for k in range(1, 90):
        a = r.normal() / k ** rough
        s += a * np.sin(k * xs + r.random() * 2 * np.pi)
    s = (s - s.min()) / (s.max() - s.min())
    ridged = 1 - np.abs(2 * s - 1)                         # острые гребни
    hgt = base + amp * (0.55 * s + 0.45 * ridged ** 2)
    return hgt
layers = [  # seed, амплитуда (рад), основание, шероховатость, цвет, туман
    (5, 0.150, 0.020, 1.15, np.array([0.092, 0.102, 0.165]), 0.75),
    (8, 0.105, 0.010, 1.05, np.array([0.054, 0.062, 0.105]), 0.55),
    (13, 0.070, 0.002, 0.95, np.array([0.034, 0.040, 0.070]), 0.35),
    (17, 0.040, -0.004, 0.9, np.array([0.020, 0.024, 0.044]), 0.15),
]
for seed, amp, base, rough, col, fog in layers:
    h = ridge(seed, amp, base, rough)
    top = h[None, :]
    mask = LAT < top
    depth = np.clip((top - LAT) / (amp + 0.02), 0, 1)                    # 0 у гребня → 1 у подножия
    c = col[None, None, :] * np.ones_like(depth)[..., None]
    c = c * (1 - 0.35 * depth[..., None])                                 # к подножию темнее…
    c += np.array([0.10, 0.11, 0.17]) * (fog * np.clip(1 - (LAT - (base - 0.01)) / 0.03, 0, 1))[..., None] * 0.6   # …но там туман
    rim = np.exp(-((top - LAT) / 0.0025) ** 2) * np.clip(1 - ang / 1.2, 0, 1) * 0.09
    c += rim[..., None] * np.array([0.7, 0.8, 1.0])
    img = np.where(mask[..., None], c, img)

# ниже горизонта — туман арены (плавный переход)
fogc = np.array([0.07, 0.08, 0.12])
k = np.clip(-LAT / 0.03, 0, 1)
img = img * (1 - k[..., None]) + fogc * k[..., None]
img = np.clip(img, 0, 1)
# лёгкий дизеринг против полос градиента на 8 битах
img += (rng.random(img.shape) - 0.5) / 255.0
Image.fromarray(np.clip(img * 255 + 0.5, 0, 255).astype(np.uint8)).save(OUT)
print('ok', OUT)
