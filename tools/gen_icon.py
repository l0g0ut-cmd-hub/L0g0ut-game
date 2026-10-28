"""Иконка приложения 1024x1024: багровое небо, луна, катана наискось, брызги крови. python3 tools/gen_icon.py"""
import math, random
from PIL import Image, ImageDraw, ImageFilter
N = 1024
random.seed(3)
img = Image.new('RGB', (N, N))
px = img.load()
for y in range(N):
    for x in range(N):
        d = math.hypot(x - N * 0.62, y - N * 0.36) / N
        r = int(max(0, 120 - d * 190)); g = int(max(0, 12 - d * 20)); b = int(max(0, 18 - d * 20))
        px[x, y] = (r + 8, g + 4, b + 8)
glow = Image.new('L', (N, N), 0)
ImageDraw.Draw(glow).ellipse((N*0.38, N*0.12, N*0.86, N*0.60), fill=255)
glow = glow.filter(ImageFilter.GaussianBlur(60))
img = Image.composite(Image.new('RGB', (N, N), (255, 120, 90)), img, glow.point(lambda v: v // 3))
d = ImageDraw.Draw(img)
d.ellipse((N*0.45, N*0.19, N*0.79, N*0.53), fill=(246, 226, 200))
# брызги
for i in range(70):
    a = random.uniform(-0.6, 0.9); rr = random.uniform(0.05, 0.42) * N
    cx, cy = N*0.52 + math.cos(a)*rr, N*0.55 + math.sin(a)*rr*0.6
    s = random.uniform(4, 22) * (1 - rr / N)
    d.ellipse((cx-s, cy-s, cx+s, cy+s), fill=(150, 0, 8))
# катана: клинок, цуба, рукоять
def poly(pts, col): d.polygon([(x*N, y*N) for x, y in pts], fill=col)
ang = math.radians(-38)
ux, uy = math.cos(ang), math.sin(ang); nx, ny = -uy, ux
def P(t, w): return (0.47 + ux*t*1.15 + nx*w*1.7, 0.6 + uy*t*1.15 + ny*w*1.7)
poly([P(-0.06, -0.018), P(0.40, -0.012), P(0.47, 0.0), P(0.40, 0.014), P(-0.06, 0.016)], (12, 10, 14))
poly([P(-0.06, -0.012), P(0.40, -0.008), P(0.46, 0.0), P(0.40, -0.001), P(-0.06, -0.002)], (215, 220, 230))
cx, cy = P(-0.07, 0)
d.ellipse(((cx-0.06)*N, (cy-0.06)*N, (cx+0.06)*N, (cy+0.06)*N), fill=(160, 20, 20), outline=(20, 8, 8), width=10)
poly([P(-0.08, -0.02), P(-0.30, -0.018), P(-0.30, 0.018), P(-0.08, 0.02)], (20, 14, 16))
for k in range(7):
    t = -0.10 - k * 0.028
    poly([P(t, -0.02), P(t - 0.014, 0.02), P(t - 0.02, 0.02), P(t - 0.006, -0.02)], (150, 20, 24))
img = img.filter(ImageFilter.SMOOTH)
img.save('Assets/_Project/Art/icon.png')
print('ok')
