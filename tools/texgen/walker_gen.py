"""Цикл шага тени монаха за сёдзи: верх (голова, плечи, руки в рукавах) берётся с исходного кадра ChatGPT,
низ (подол рясы и ступни) строится по настоящей кинематике походки: опора 60 % цикла, перенос 40 %,
стопа опоры неподвижна относительно пола, перекат с пятки на носок, плавное покачивание корпуса вверх-вниз.
15 кадров цикла + 1 кадр «стоит» (стопы вместе) — сетка 8×2, ячейка 256×512.
usage: python3 walker_gen.py SRC_ORIGINAL_STRIP OUT_PNG"""
import sys, math, numpy as np
from PIL import Image, ImageDraw, ImageFilter

SRC, OUT = sys.argv[1], sys.argv[2]
SS = 3                      # суперсэмплинг
CW, CH = 256, 512
F = 15                      # кадров в цикле
S = 258.0                   # длина цикла (2 шага) в пикселях: 0,78 м при высоте наклейки 1,55 м
GROUND = 500.0
CUT = 330                   # ниже — синтез
src = 255 - np.asarray(Image.open(SRC).convert('L')).astype(float)
tmpl = src[:, 0:CW].copy()  # кадр 0: верх фигуры
cx = 131.0                  # ось бёдер

def smooth(t): return t * t * (3 - 2 * t)

def foot(p):
    """Стопа в фазе p∈[0,1): x лодыжки относительно оси, подъём, наклон (град, + = пятка вверх)."""
    p %= 1.0
    if p < 0.6:
        x = 0.3 * S - p / 0.6 * 0.6 * S
        lift = 0.0
        if p < 0.08: pitch = -14 * (1 - p / 0.08)            # касание пяткой, носок ещё поднят
        elif p > 0.42: pitch = 28 * ((p - 0.42) / 0.18) ** 1.5   # пятка отрывается, перекат на носок
        else: pitch = 0.0
    else:
        s = (p - 0.6) / 0.4
        x = -0.3 * S + 0.6 * S * smooth(s)
        lift = 15 * math.sin(math.pi * min(1, s * 1.1))
        pitch = 28 * (1 - s) - 14 * smooth(s)
    return x, lift, pitch

def shoe(d, ax, ay, pitch, k):
    """Сандалия/таби сбоку: от пятки (−16) до носка (+34), высота 15; вращение вокруг носка при пятке вверх, вокруг пятки при носке вверх."""
    pts = [(-17, 0), (-19, -10), (-13, -19), (6, -20), (22, -15), (34, -7), (38, -1), (37, 1)]
    a = math.radians(pitch)
    piv = (37, 0) if pitch > 0 else (-17, 0)
    out = []
    for (x, y) in pts:
        x0, y0 = x - piv[0], y - piv[1]
        xr = x0 * math.cos(a) + y0 * math.sin(a)
        yr = -x0 * math.sin(a) + y0 * math.cos(a)
        out.append(((ax + piv[0] + xr) * k, (ay + piv[1] + yr) * k))
    d.polygon(out, fill=255)
    # лодыжка до подола
    top = out[2][1] / k + 2
    d.rectangle([(ax - 13) * k, (top - 40) * k, (ax + 9) * k, top * k], fill=255)

def frame(p, stand=False):
    if stand:
        A = (6.0, 0.0, 0.0); B = (-8.0, 0.0, 0.0); bob = -3.0
    else:
        A = foot(p); B = foot(p + 0.5)
        bob = 3.5 * math.cos(4 * math.pi * (p - 0.04))     # ниже всего в двойной опоре
    k = SS
    img = Image.new('L', (CW * k, CH * k), 0)
    d = ImageDraw.Draw(img)
    # подол: края от бёдер к подолу; ширина у подола следует за стопами (ряса «шагает»)
    yh = 474 + bob
    fr = max(A[0], B[0]); bk = min(A[0], B[0])
    hem_r = cx + max(70, fr + 26); hem_l = cx + min(-74, bk - 28)
    yt = CUT - 34 + bob
    row = np.nonzero(tmpl[CUT - 34] > 100)[0]
    lt, rt = row.min() + 1.5, row.max() - 1.5
    # передний край чуть выпирает коленом выносимой ноги
    sw = A if A[1] > B[1] else B
    knee = cx + 0.55 * sw[0] + 34
    poly = [(lt, yt), (rt, yt)]
    for i in range(1, 9):
        t = i / 8; y = yt + (yh - yt) * t
        xr = rt + (hem_r - rt) * t ** 1.3 + max(0, knee - (rt + (hem_r - rt) * t)) * math.sin(math.pi * t) * 0.35
        poly.append((xr, y))
    for i in range(0, 11):
        t = i / 10; x = hem_r + (hem_l - hem_r) * t
        poly.append((x, yh + 2.5 * math.sin(t * math.pi * 3 + p * 6)))
    for i in range(8, 0, -1):
        t = i / 8; y = yt + (yh - yt) * t
        poly.append((lt + (hem_l - lt) * t ** 1.3, y))
    d.polygon([(x * k, y * k) for x, y in poly], fill=255)
    for f in (B, A):
        shoe(d, cx + f[0], GROUND - f[1], f[2], k)
    low = np.asarray(img.resize((CW, CH), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.6))).astype(float) / 255
    # верх из шаблона со сдвигом на покачивание, мягкий шов
    up = np.zeros((CH, CW))
    sh = int(round(bob))
    srcrows = tmpl
    if sh > 0: up[sh:] = srcrows[:CH - sh]
    elif sh < 0: up[:CH + sh] = srcrows[-sh:]
    else: up[:] = srcrows
    up = np.clip(up / 199.0, 0, 1)
    ys = np.arange(CH)[:, None]
    w = np.clip((CUT - 4 + bob - ys) / 14.0, 0, 1)
    m = np.maximum(up * w, low * (1 - w * 0.0)) if False else np.maximum(up * w, low)
    return m

cells = [frame(i / F) for i in range(F)] + [frame(0, stand=True)]
sheet = np.zeros((CH * 2, CW * 8))
for i, c in enumerate(cells):
    r, col = divmod(i, 8)
    sheet[r * CH:(r + 1) * CH, col * CW:(col + 1) * CW] = c
im = Image.fromarray((255 - sheet * 199).clip(0, 255).astype(np.uint8))
im.save(OUT)
