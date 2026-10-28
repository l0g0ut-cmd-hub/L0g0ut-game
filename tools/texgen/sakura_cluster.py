"""Карточка кроны сакуры (RGBA, альфа-срез): гроздь пятилепестковых цветков с выемкой на кончике лепестка,
бледно-розовые с тёмной серединкой и тычинками, под ними тонкие веточки. Рисуется в 4× и сглаживается вниз.
Запуск: python3 tools/texgen/sakura_cluster.py OUT.png [size]"""
import sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]; N = int(sys.argv[2]) if len(sys.argv) > 2 else 512
S = N * 4
rng = np.random.default_rng(17)
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
C = S / 2

# веточки: от низа-центра к краям, тонкие, тёмно-коричневые
def twig(x0, y0, ang, length, w, depth):
    x1 = x0 + math.cos(ang) * length; y1 = y0 + math.sin(ang) * length
    d.line([(x0, y0), (x1, y1)], fill=(52, 34, 30, 255), width=max(2, int(w)))
    if depth > 0:
        for s in (-1, 1):
            twig(x1, y1, ang + s * rng.uniform(0.35, 0.8), length * rng.uniform(0.5, 0.7), w * 0.65, depth - 1)
for k in range(5):
    twig(C + rng.uniform(-0.05, 0.05) * S, C + 0.3 * S, -math.pi / 2 + rng.uniform(-1.0, 1.0), S * 0.22, S * 0.012, 3)

def petal_poly(cx, cy, ang, length, width):
    """Лепесток: овал от центра наружу с выемкой на кончике."""
    pts = []
    for i in range(25):
        t = i / 24.0                      # 0..1 вдоль лепестка (одна сторона)
        r = length * t
        half = width * (math.sin(math.pi * min(1.0, t * 0.98)) ** 0.8) * (0.35 + 0.65 * min(1.0, t / 0.7))   # узко у основания, шире к краю
        pts.append((r, half))
    # выемка на кончике
    tip = [(length * 0.93, width * 0.18), (length * 0.86, 0.0), (length * 0.93, -width * 0.18)]
    side2 = [(r, -h) for (r, h) in reversed(pts)]
    poly = pts + tip + side2
    ca, sa = math.cos(ang), math.sin(ang)
    return [(cx + x * ca - y * sa, cy + x * sa + y * ca) for (x, y) in poly]

flowers = []
for i in range(95):
    rr = 0.44 * math.sqrt(rng.random()) ** 0.9
    a = rng.random() * 2 * math.pi
    flowers.append((C + math.cos(a) * rr * S, C + math.sin(a) * rr * S * 0.92, rng.uniform(0.045, 0.075) * S))
flowers.sort(key=lambda f: f[1])     # верхние рисуются первыми — нижние перекрывают (взгляд снизу на ветку)
for (fx, fy, fr) in flowers:
    base_a = rng.random() * 2 * math.pi
    tone = rng.uniform(0.0, 1.0)
    shade = rng.uniform(0.82, 1.0)
    # повёрнутый «в профиль» цветок — сплюснут
    squash = rng.uniform(0.55, 1.0)
    for p in range(5):
        ang = base_a + p * 2 * math.pi / 5 + rng.uniform(-0.12, 0.12)
        poly = petal_poly(0, 0, ang, fr, fr * 0.40)
        poly = [(fx + x, fy + y * squash) for (x, y) in poly]
        col = np.array([255, 224, 234]) * (1 - tone) + np.array([246, 186, 208]) * tone
        pv = rng.uniform(0.9, 1.05)
        col = tuple(int(min(255, c * shade * pv)) for c in col) + (255,)
        edge = tuple(int(c * 0.86) for c in col[:3]) + (255,)
        d.polygon(poly, fill=col, outline=edge, width=max(1, int(fr * 0.035)))
        # основание лепестка розовее
        inner = petal_poly(0, 0, ang, fr * 0.45, fr * 0.22)
        inner = [(fx + x, fy + y * squash) for (x, y) in inner]
        d.polygon(inner, fill=tuple(int(c) for c in (np.array(col[:3]) * np.array([0.96, 0.8, 0.86]))) + (255,))
    # серединка: тёмно-розовая звезда + жёлтые тычинки
    d.ellipse([fx - fr * 0.14, fy - fr * 0.14 * squash, fx + fr * 0.14, fy + fr * 0.14 * squash], fill=(186, 82, 110, 255))
    for k in range(9):
        a = rng.random() * 2 * math.pi; l = fr * rng.uniform(0.25, 0.42)
        x2, y2 = fx + math.cos(a) * l, fy + math.sin(a) * l * squash
        d.line([(fx, fy), (x2, y2)], fill=(170, 70, 95, 255), width=max(1, int(fr * 0.03)))
        d.ellipse([x2 - fr * 0.04, y2 - fr * 0.04, x2 + fr * 0.04, y2 + fr * 0.04], fill=(240, 205, 120, 255))

small = img.resize((N, N), Image.LANCZOS)
# лёгкое затемнение к центру грозди (самозатенение) — только RGB
arr = np.asarray(small).astype(float)
yy, xx = np.mgrid[0:N, 0:N]
r = np.sqrt((xx - N / 2) ** 2 + (yy - N / 2) ** 2) / (N / 2)
arr[..., :3] *= (0.78 + 0.22 * np.clip(r / 0.8, 0, 1))[..., None]
# цвет под прозрачными пикселями = соседний цвет (иначе на краях альфа-среза тёмная кайма в мипах)
rgb = Image.fromarray(np.clip(arr[..., :3], 0, 255).astype(np.uint8))
dil = rgb.filter(ImageFilter.MaxFilter(9))
a = arr[..., 3:4] / 255.0
mix = arr[..., :3] * (a > 0.02) + np.asarray(dil).astype(float) * (a <= 0.02)
res = np.concatenate([mix, arr[..., 3:4]], -1)
Image.fromarray(np.clip(res, 0, 255).astype(np.uint8), 'RGBA').save(out)
print('ok', out)
