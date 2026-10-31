"""Тени людей за светящимися сёдзи (рисуются множением на бумагу, BladePath/AO; белое — нет тени):
 • shoji_monk.png — монах в сэйдза в профиль (бритая голова чуть склонена, спина, рукава, сложенные руки), 256×512;
 • shoji_walker.png — лист из 4 кадров шага (1024×512): монах в длинном одеянии идёт вправо, полы и ноги в шаге, руки качаются.
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

# --- идущий монах: 4 кадра цикла шага (вправо)
frames = []
for f in range(4):
    im, d = canvas()
    ph = (f + 0.5) / 4.0 * 2 * math.pi   # со сдвигом: все 4 кадра разные
    bob = -abs(math.sin(ph)) * 6       # на шаге чуть выше
    gy = 500                           # пол
    hip = (128, gy - 210 + bob)
    # ноги: две, в противофазе; видны ниже подола (подол ~ gy − 70)
    for side in (1, -1):
        s = math.sin(ph) * side
        foot = (128 + s * 46, gy)
        knee = (128 + s * 26 + 8, gy - 105 + bob - abs(s) * 6)
        d.line(P([hip, knee, foot]), fill=255, width=22 * S, joint='curve')
        d.polygon(P([(foot[0] - 10, gy), (foot[0] - 8, gy - 14), (foot[0] + 22, gy - 10), (foot[0] + 26, gy)]), fill=255)
    # одеяние: от плеч расширяется к подолу, подол колышется в шаге
    sway = math.sin(ph) * 12
    d.polygon(P([(96, gy - 326 + bob), (162, gy - 326 + bob), (176 + sway * 0.4, gy - 200 + bob), (190 + sway, gy - 74 + bob),
                 (70 + sway, gy - 72 + bob), (80 + sway * 0.4, gy - 200 + bob)]), fill=255)
    # руки в широких рукавах: качаются в противофазе к ногам, рукав расширяется к обшлагу и висит
    for side in (1, -1):
        a = -math.sin(ph) * side * 0.42
        sh = (128 + side * 4, gy - 316 + bob)
        ca, sa = math.cos(a), math.sin(a)
        el = (sh[0] + sa * 70, sh[1] + ca * 70)
        hand = (el[0] + math.sin(a + 0.35) * 62, el[1] + math.cos(a + 0.35) * 62)
        d.line(P([sh, el, hand]), fill=255, width=26 * S, joint='curve')
        # рукав: трапеция от локтя вниз
        d.polygon(P([(el[0] - 20, el[1] - 10), (el[0] + 20, el[1] - 10), (el[0] + 26 + sa * 30, el[1] + 58), (el[0] - 22 + sa * 30, el[1] + 62)]), fill=255)
        E(d, hand[0], hand[1], 11, 12)
    # шея, голова (бритая), нос — смотрит вправо
    d.polygon(P([(120, gy - 330 + bob), (122, gy - 350 + bob), (142, gy - 352 + bob), (144, gy - 330 + bob)]), fill=255)
    E(d, 134, gy - 376 + bob, 27, 30)
    d.polygon(P([(158, gy - 384 + bob), (168, gy - 372 + bob), (157, gy - 366 + bob)]), fill=255)
    frames.append(finish(im, 6))
sheet = np.concatenate(frames, axis=1)
Image.fromarray((sheet * 255).astype(np.uint8)).save(os.path.join(out, 'shoji_walker.png'))
print('ok')
