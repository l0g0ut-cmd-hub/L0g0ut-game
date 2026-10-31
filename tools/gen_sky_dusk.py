"""Закатное небо-панорама (вариант gen_sky_day.py). Было: дневное небо-панорама (equirect 4096x2048, раскладка Skybox/Panoramic как в gen_sky2.py: u = 0.5 − atan2(z, x)/2π,
v = 0.5 + высота/π). Ясный летний день: глубокая синева в зените, светлая дымка у горизонта, солнце высоко за левым плечом
камеры боя (вне кадра — без ослепления bloom), ореол вокруг него; кучевые облака с освещённой солнцем макушкой и серым
низом, у горизонта — перистые полосы; 4 гряды гор в воздушной перспективе (дальние голубее и светлее). Ниже горизонта — цвет
дневного тумана арены. Направление солнца совпадает с DayNight.sunDir.
Запуск: python3 tools/gen_sky_day.py [OUT]"""
import sys
import numpy as np
from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else 'Assets/_Project/Art/Textures/sky_dusk.png'
W, H = 4096, 2048
rng = np.random.default_rng(19)

u = (np.arange(W) + 0.5) / W
v = 1.0 - (np.arange(H) + 0.5) / H
lon = (0.5 - u) * 2 * np.pi
lat = (v - 0.5) * np.pi
LON, LAT = np.meshgrid(lon, lat)
DX = np.cos(LAT) * np.cos(LON); DZ = np.cos(LAT) * np.sin(LON); DY = np.sin(LAT)

sun = np.array([0.42, 0.07, 0.9]); sun /= np.linalg.norm(sun)   # низко над горизонтом за храмом, правее (совпадает с DayNight.duskSunDir)
cosS = np.clip(DX * sun[0] + DY * sun[1] + DZ * sun[2], -1, 1)
angS = np.arccos(cosS)


def fbm2(x, y, oct=6, seed=0, lac=2.0, gain=0.5):
    r = np.random.default_rng(seed)
    acc = np.zeros_like(x); amp = 1.0; f = 1.0; tot = 0
    for o in range(oct):
        for k in range(3):
            a = r.random() * 2 * np.pi; ph = r.random() * 2 * np.pi
            acc += amp * np.sin((np.cos(a) * x + np.sin(a) * y) * f + ph)
        tot += amp * 3; amp *= gain; f *= lac
    return acc / tot * 0.5 + 0.5


up = np.clip(LAT / (np.pi / 2), 0, 1)
zen = np.array([0.13, 0.12, 0.30]); hor = np.array([0.95, 0.55, 0.32])   # фиолетовый зенит → оранжевый горизонт
t = up ** 0.42
# к солнцу горизонт горячее, от солнца — розово-лиловый
sideS = 0.5 + 0.5 * np.clip(cosS * 1.4, -1, 1)
horc = hor[None, None] * sideS[..., None] + np.array([0.62, 0.36, 0.44])[None, None] * (1 - sideS[..., None])
img = horc * (1 - t[..., None]) + zen * t[..., None]
img += np.exp(-(LAT / 0.05) ** 2)[..., None] * np.array([0.12, 0.06, 0.02])   # тёплая дымка у самой земли
# солнце: ореолы (диск сам — вне кадра, но в отражениях и при облёте камеры виден)
img += (np.exp(-angS / 0.025) * 1.3 + np.exp(-angS / 0.15) * 0.45 + np.exp(-angS / 0.7) * 0.25)[..., None] * np.array([1.0, 0.62, 0.3])
img = np.where((angS < 0.016)[..., None], np.array([1.0, 0.86, 0.6]), img)

# облака на плоскости в перспективе
above = DY > 0.015
cx = np.where(above, DX / np.maximum(DY, 0.015), 0) * 0.45
cz = np.where(above, DZ / np.maximum(DY, 0.015), 0) * 0.45
wx = fbm2(cx * 0.6, cz * 0.6, 4, 31) - 0.5; wz = fbm2(cx * 0.6 + 7, cz * 0.6, 4, 32) - 0.5
dens = fbm2(cx * 1.1 + wx * 1.6, cz * 1.1 + wz * 1.6, 7, 33, 2.05, 0.55)        # кучевые: округлые
cov = np.clip((dens - 0.53) * 4.5, 0, 1) ** 1.2
cov *= np.clip((LAT - 0.025) / 0.1, 0, 1) * np.clip(1 - (LAT - 0.7) / 0.5, 0.2, 1)
cirr = fbm2(cx * 2.4 + wx * 2.0, cz * 0.6 + wz * 2.0, 6, 34, 2.1, 0.5)        # перистые полосы
cir = np.clip((cirr - 0.56) * 3.0, 0, 1) * 0.55 * np.clip((LAT - 0.02) / 0.06, 0, 1) * np.clip(1 - LAT / 0.5, 0, 1)
# освещение облаков: макушка к солнцу светлая, низ — серо-голубой; к солнцу — серебряная кромка
edge = np.clip(cov * (1 - cov) * 4, 0, 1)
dens2 = fbm2(cx * 1.1 + wx * 1.6 + 0.08, cz * 1.1 + wz * 1.6 + 0.08, 7, 33, 2.05, 0.55)   # сдвиг к солнцу: где плотнее — тень
shade = np.clip((dens2 - dens) * 9 + 0.55, 0, 1)
lit = np.array([1.0, 0.62, 0.38]); dark = np.array([0.32, 0.22, 0.34])   # снизу облака подсвечены закатом, сверху — лиловая тень
ccol = dark * (1 - shade[..., None]) + lit * shade[..., None]
ccol += np.array([0.5, 0.25, 0.08]) * (edge * np.exp(-angS / 0.9))[..., None]
img = img * (1 - 0.95 * cov[..., None]) + ccol * (0.95 * cov[..., None])
img = img * (1 - cir[..., None]) + (np.array([1.0, 0.6, 0.42]) * (0.6 + 0.4 * sideS[..., None])) * cir[..., None]


def ridge(seed, amp, base, rough):
    r = np.random.default_rng(seed)
    s = np.zeros(W)
    xs = np.linspace(0, 2 * np.pi, W, endpoint=False)
    for k in range(1, 90):
        s += r.normal() / k ** rough * np.sin(k * xs + r.random() * 2 * np.pi)
    s = (s - s.min()) / (s.max() - s.min())
    ridged = 1 - np.abs(2 * s - 1)
    return base + amp * (0.55 * s + 0.45 * ridged ** 2)


layers = [  # те же гряды, что ночью (seed), — днём в воздушной перспективе
    (5, 0.150, 0.020, 1.15, np.array([0.42, 0.30, 0.42]), 0.7),
    (8, 0.105, 0.010, 1.05, np.array([0.30, 0.20, 0.32]), 0.5),
    (13, 0.070, 0.002, 0.95, np.array([0.20, 0.13, 0.22]), 0.3),
    (17, 0.040, -0.004, 0.9, np.array([0.12, 0.08, 0.13]), 0.15),
]
for seed, amp, base, rough, col, fog in layers:
    h = ridge(seed, amp, base, rough)
    top = h[None, :]
    mask = LAT < top
    depth = np.clip((top - LAT) / (amp + 0.02), 0, 1)
    c = col[None, None, :] * np.ones_like(depth)[..., None] * (1 - 0.25 * depth[..., None])
    # склон к солнцу светлее (по долготе), дымка в долинах
    side = 0.5 + 0.5 * np.cos(LON - np.arctan2(sun[2], sun[0]))
    c *= (0.85 + 0.25 * side)[..., None]
    c += np.array([0.85, 0.5, 0.42]) * (fog * np.clip(1 - (LAT - (base - 0.01)) / 0.04, 0, 1))[..., None] * 0.5
    img = np.where(mask[..., None], c, img)

fogc = np.array([0.62, 0.42, 0.38])
k = np.clip(-LAT / 0.03, 0, 1)
img = img * (1 - k[..., None]) + fogc * k[..., None]
img = np.clip(img, 0, 1)
img += (rng.random(img.shape) - 0.5) / 255.0
Image.fromarray(np.clip(img * 255 + 0.5, 0, 255).astype(np.uint8)).save(OUT)
print('ok', OUT)
