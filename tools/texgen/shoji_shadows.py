"""Тени людей за светящимися сёдзи (рисуются множением на бумагу, BladePath/AO; белое — нет тени):
 • shoji_monk.png — монах в сэйдза в профиль (бритая голова чуть склонена, спина, рукава, сложенные руки), 256×512;
 • shoji_walker.png — лист из 8 кадров шага (2048×512): монах в длинном одеянии идёт вправо, руки сложены в рукавах.
Края размыты, как у тени на рисовой бумаге (фигура в полуметре за ней). Запуск: python3 shoji_shadows.py OUTDIR"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
S = 2  # рисуем в 2× и уменьшаем
W, H = 256, 512


def canvas():
    im = Image.new('L', (W * S, H * S), 0)
    return im, ImageDraw.Draw(im)


def finish(im, blur=7):
    im = im.filter(ImageFilter.GaussianBlur(blur * S)).resize((W, H), Image.LANCZOS)
    a = np.asarray(im, dtype=np.float32) / 255.0
    shade = 1.0 - 0.78 * a          # ядро тени — не чёрное: свет обходит фигуру
    return shade


def P(pts):
    return [(x * S, y * S) for x, y in pts]


def E(d, cx, cy, rx, ry):
    d.ellipse([(cx - rx) * S, (cy - ry) * S, (cx + rx) * S, (cy + ry) * S], fill=255)


# --- монах в сэйдза (лицом влево). Низ кадра (y = 512) — пол за сёдзи.
im, d = canvas()
base = 500
# колени/подол: широкая пологая основа
d.polygon(P([(40, base), (52, base - 46), (96, base - 70), (180, base - 74), (214, base - 40), (220, base)]), fill=255)
# спина и грудь: чуть сутулая, наклон вперёд
d.polygon(P([(88, base - 66), (92, base - 170), (112, base - 236), (150, base - 252), (182, base - 238), (198, base - 180), (206, base - 70)]), fill=255)
# рукав и руки на коленях
d.polygon(P([(96, base - 150), (70, base - 110), (66, base - 84), (110, base - 78), (126, base - 120)]), fill=255)
# шея и склонённая голова
d.polygon(P([(118, base - 240), (124, base - 268), (150, base - 272), (160, base - 246)]), fill=255)
E(d, 128, base - 294, 30, 33)
d.polygon(P([(98, base - 300), (90, base - 288), (100, base - 282)]), fill=255)   # нос
Image.fromarray((finish(im) * 255).astype(np.uint8)).save(os.path.join(out, 'shoji_monk.png'))

# --- идущий монах: 8 кадров цикла шага (вправо), 2048×512. Фигура по скелету: таз → бёдра/голени по фазе шага,
# корпус с лёгким наклоном и подъёмом на опорной ноге, руки сложены впереди в широких рукавах (монах идёт чинно, не машет),
# одеяние до щиколоток — подол следует за ногами (передняя нога выносит полу вперёд), видны ступни в дзори.
# Контуры — гладкие сплайны (Катмулл–Ром), иначе на бумаге читались «рубленые» трапеции.
def spline(pts, n=10, closed=True):
    pts = list(pts)
    if closed: pts = [pts[-1]] + pts + [pts[0], pts[1]]
    else: pts = [pts[0]] + pts + [pts[-1]]
    out_ = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = [np.array(q, dtype=float) for q in pts[i - 1:i + 3]]
        for k in range(n):
            t = k / n
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
            out_.append((q[0], q[1]))
    return out_


def limb(d, a, b, ra, rb):
    """Конечность-капсула от a (радиус ra) к b (радиус rb)."""
    ax, ay = a; bx, by = b
    dx, dy = bx - ax, by - ay; L = math.hypot(dx, dy) + 1e-6
    nx, ny = -dy / L, dx / L
    d.polygon(P([(ax + nx * ra, ay + ny * ra), (bx + nx * rb, by + ny * rb), (bx - nx * rb, by - ny * rb), (ax - nx * ra, ay - ny * ra)]), fill=255)
    E(d, ax, ay, ra, ra); E(d, bx, by, rb, rb)


def leg(hip, ph, bob):
    """Бедро/голень/стопа по фазе: ph = 0 — нога под тазом и уходит назад (опора), π — в выносе."""
    th = math.radians(16) * math.sin(ph + math.pi / 2)   # в рясе шаг короткий                  # бедро: вперёд-назад от вертикали
    swing = max(0.0, math.sin(ph))                                      # фаза переноса — колено сгибается
    kn = math.radians(6 + 34 * swing ** 1.3)
    thigh, shin = 118.0, 116.0
    knee = (hip[0] + math.sin(th) * thigh, hip[1] + math.cos(th) * thigh)
    sa = th - kn
    ank = (knee[0] + math.sin(sa) * shin, knee[1] + math.cos(sa) * shin)
    return knee, ank, swing


frames = []
NF = 8
for f in range(NF):
    im, d = canvas()
    ph = f / NF * 2 * math.pi
    gy = 492                                                   # пол
    bob = -5 * math.cos(2 * ph)                                 # выше всего — когда опорная нога вертикальна
    hipY = gy - 236 + bob
    hip = (128.0, hipY)
    # ноги (дальняя — слегка смещена назад и тоньше)
    feet = []
    for side, phase, sh in ((-1, ph + math.pi, -3), (1, ph, 0)):
        knee, ank, swing = leg((hip[0] + sh, hip[1]), phase, bob)
        lift = swing * 10
        ank = (ank[0], min(gy - 10, ank[1] - lift))
        limb(d, (hip[0] + sh, hip[1]), knee, 17, 13)
        limb(d, knee, ank, 13, 9)
        # стопа в дзори: носок вперёд, пятка чуть приподнята в конце опоры
        toe = (ank[0] + 30, min(gy, ank[1] + 12))
        heel = (ank[0] - 8, min(gy, ank[1] + 10))
        d.polygon(P([heel, (ank[0] - 6, ank[1] - 6), (ank[0] + 14, ank[1] - 2), toe, (toe[0] - 4, toe[1] + 2), (heel[0], heel[1] + 2)]), fill=255)
        feet.append(ank[0])
    back_x, front_x = min(feet), max(feet)
    mid = (back_x + front_x) / 2
    fx_ = mid + (front_x - mid) * 0.75; bx_ = mid + (back_x - mid) * 0.75   # подол сдерживает шаг — полы уже разлёта стоп
    # одеяние: плечи → пояс → подол; подол чуть выше щиколоток, полы следуют за ногами
    lean = 6                                                    # корпус чуть вперёд
    shY = hipY - 138; nkY = shY - 14
    hem = gy - 52 + bob * 0.3
    robe = [(100 + lean, shY + 6), (118 + lean, shY - 4), (140 + lean, shY - 2), (156 + lean, shY + 10),   # плечи
            (160 + lean * 0.6, shY + 70), (158, hipY - 10),                                                    # грудь, живот
            (fx_ + 20, hem - 50), (fx_ + 24, hem - 4), (fx_ + 8, hem + 4),                                     # передняя пола
            (mid + 4, hem - 4),                                                                                # провис между ногами
            (bx_ - 4, hem + 5), (bx_ - 18, hem - 2), (bx_ - 12, hem - 54),                                    # задняя пола
            (92, hipY - 20), (90 + lean * 0.4, shY + 60)]                                                       # спина
    d.polygon(P(spline(robe, 8)), fill=255)
    # руки сложены впереди у пояса, широкие рукава свисают
    sleeve = [(132 + lean, shY + 14), (158 + lean, shY + 40), (176, hipY - 54), (184, hipY - 18), (170, hipY + 18),
              (146, hipY + 26), (128, hipY + 4), (124, hipY - 40)]
    d.polygon(P(spline(sleeve, 8)), fill=255)
    E(d, 182, hipY - 34, 10, 9)                                 # кисти над рукавом
    # шея и бритая голова с ухом, носом и подбородком — смотрит вправо
    hx, hy = 136 + lean * 1.2, nkY - 34
    d.polygon(P([(124 + lean, shY + 2), (128 + lean, nkY - 6), (146 + lean, nkY - 8), (150 + lean, shY + 4)]), fill=255)
    head = spline([(hx - 26, hy + 2), (hx - 22, hy - 22), (hx - 2, hy - 33), (hx + 20, hy - 26), (hx + 28, hy - 6),
                   (hx + 33, hy + 4), (hx + 29, hy + 9), (hx + 30, hy + 16), (hx + 22, hy + 28), (hx + 4, hy + 32), (hx - 18, hy + 22)], 8)
    d.polygon(P(head), fill=255)
    frames.append(finish(im, 5))
sheet = np.concatenate(frames, axis=1)
Image.fromarray((sheet * 255).astype(np.uint8)).save(os.path.join(out, 'shoji_walker.png'))
print('ok')
