#!/usr/bin/env python3
"""Предпросмотр HUD пошагового боя без Unity: рисует нижнюю панель, очередь ходов и подпись раунда
по тем же формулам, что Tactics.cs (HudLayout, QueueRect, DrawHud, DrawQueue), теми же картинками и шрифтом.

  python3 tools/hud_preview.py [ширина_экрана высота_экрана] [выход.png]

По умолчанию 2280×1080 (телефон владельца) → build/hud_preview_2280.png. Виртуальная высота 540, как в игре.
Цифры в коде и здесь должны совпадать: правишь раскладку в Tactics.cs — правь и тут, иначе предпросмотр врёт.
"""
import sys, os, math
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "Assets/_Project/Resources")
FONT = os.path.join(RES, "Fonts/OldStandard-Bold.ttf")
GOLD = (230, 194, 120); INK = (237, 227, 204); DIM = (173, 163, 143)

sw, sh = (int(sys.argv[1]), int(sys.argv[2])) if len(sys.argv) > 2 else (2280, 1080)
out = sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, f"build/hud_preview_{sw}.png")
K = sh / 540.0                     # точек экрана на виртуальную точку (UiKit.Scale)
W = 540.0 * sw / sh                # виртуальная ширина

img = Image.new("RGBA", (sw, sh), (64, 92, 52, 255))
dr = ImageDraw.Draw(img)
cache = {}


def tex(path):
    if path not in cache:
        p = os.path.join(RES, path + ".png")
        if not os.path.exists(p): p = os.path.join(RES, path + ".jpg")
        cache[path] = Image.open(p).convert("RGBA") if os.path.exists(p) else None
    return cache[path]


def px(r):  # виртуальный прямоугольник → экранные целые
    x, y, w, h = r
    return int(round(x * K)), int(round(y * K)), max(1, int(round(w * K))), max(1, int(round(h * K)))


def draw_tex(r, t, uv=None, alpha=1.0, tint=None):
    if t is None: return
    x, y, w, h = px(r)
    src = t
    if uv:  # uv в долях, v снизу вверх (как Unity)
        u0, v0, uw, vh = uv
        W0, H0 = t.size
        box = (int(u0 * W0), int((1 - v0 - vh) * H0), int((u0 + uw) * W0), int((1 - v0) * H0))
        src = t.crop(box)
    src = src.resize((w, h), Image.LANCZOS)
    if tint or alpha < 1:
        r_, g_, b_, a_ = src.split()
        if tint:
            r_ = r_.point(lambda v: int(v * tint[0])); g_ = g_.point(lambda v: int(v * tint[1])); b_ = b_.point(lambda v: int(v * tint[2]))
        if alpha < 1: a_ = a_.point(lambda v: int(v * alpha))
        src = Image.merge("RGBA", (r_, g_, b_, a_))
    img.alpha_composite(src, (x, y))


def rect(r, col):
    x, y, w, h = px(r)
    over = Image.new("RGBA", (w, h), col)
    img.alpha_composite(over, (x, y))


def ring(c, rad, col, width=2):
    x, y = c[0] * K, c[1] * K; R = rad * K
    dr.ellipse((x - R, y - R, x + R, y + R), outline=col, width=width)


def label(box, text, size, color, align="center", fixed=False):
    """Как Tactics.Label: шрифт ужимается, пока строка не влезет; строка по центру своей высоты (линия = ascent+descent)."""
    x, y, w, h = box
    bw, bh = w * K - 2, h * K - 2
    fs = max(1, int(round(size * K)))
    while fs > 1:
        f = ImageFont.truetype(FONT, fs)
        tw = f.getlength(text); a, d = f.getmetrics()
        if fixed or (tw <= bw and a + d <= bh): break
        fs -= 1
    f = ImageFont.truetype(FONT, fs); a, d = f.getmetrics(); tw = f.getlength(text)
    top = y * K + 1 + (bh - (a + d)) / 2
    lx = x * K + 1 + ((bw - tw) / 2 if align == "center" else 0)
    dr.text((lx + 1, top + 1), text, font=f, fill=(0, 0, 0, 200))
    dr.text((lx, top), text, font=f, fill=color)
    return fs / K


def fit_size(texts, w, h, mx):
    fs = int(round(mx * K))
    for t in texts:
        s = fs
        while s > 1:
            f = ImageFont.truetype(FONT, s); a, d = f.getmetrics()
            if f.getlength(t) <= w * K - 3 and a + d <= h * K - 2: break
            s -= 1
        fs = min(fs, s)
    return fs / K


def slice3(r, t, k, cap, mid):
    """UiKit.Slice по ширине: края cap, середина mid — не тянутся, промежутки тянутся; по высоте целиком."""
    if t is None: return
    x, y, w, h = r
    c = cap * t.width * k; m = mid * t.width * k
    s = max(0, (w - 2 * c - m) / 2)
    segs = [(x, c, 0, cap), (x + c, s, cap, 0.5 - mid / 2 - cap), (x + c + s, m, 0.5 - mid / 2, mid),
            (x + c + s + m, s, 0.5 + mid / 2, 0.5 - mid / 2 - cap), (x + w - c, c, 1 - cap, cap)]
    for sx, swd, u0, uw in segs:
        if swd > 0.2 and uw > 0: draw_tex((sx, y, swd + 0.6, h), t, (u0, 0, uw, 1))


# ---------------------------------------------------------------- HudLayout (Tactics.cs)
HEIGHT, BOTTOM_RIM = 76.0, 9.0
safe = (0.0, 0.0, W, 540.0)
footer = (safe[0], safe[1] + safe[3] - HEIGHT, safe[2], HEIGHT)
pad, gap, inner = 6.0, 8.0, HEIGHT - BOTTOM_RIM - 6.0
button = min(62.0, max(40.0, (safe[2] - 380.0) / 6.0))
hero_w = min(250.0, safe[2] * .25)
hero = (footer[0] + pad, footer[1] + 3, hero_w, inner)
actions = (footer[0] + footer[2] - pad - 6 * button, hero[1], 6 * button, inner)
orders = (hero[0] + hero[2] + gap, hero[1], actions[0] - gap - (hero[0] + hero[2] + gap), inner)

# полоса панели
bar = tex("UI/Battle/Kit/hud_bar")
bk = HEIGHT / (235 - 66 - 20)
slice3((0, footer[1] - (66 + 20) * bk, W, 235 * bk), bar, bk, 0.015, 0.26)

# блок героя
hx, hy, hw_, hh_ = hero
face = min(44.0, hh_ - 14)
hp = (hx + 3, hy + (hh_ - face - 12) * .5, face, face)
rect(hp, (60, 30, 20, 255)); ring((hp[0] + face / 2, hp[1] + face / 2), face / 2 + .5, GOLD)
label((hx, hp[1] + face + 1, face + 6, 12), "ДУХ 5/5", 9.5, (168, 214, 255))
sx = hp[0] + face + 8; swd = hx + hw_ - sx
label((sx, hy, swd, 15), "УР. 1  АТК 1  ЗАЩ 1  СИЛ 1  ЗНА 1", 11.5, GOLD, "left")
cell = min(46.0, swd / 4); icon = min(40.0, hh_ - 19, cell - 6)
rnd = tex("UI/Battle/Kit/hud_round")
for i in range(4):
    r = (sx + i * cell + (cell - icon) * .5, hy + hh_ - icon - 1, icon, icon)
    draw_tex((r[0] - 3, r[1] - 3, r[2] + 6, r[3] + 6), rnd)
    rect((r[0] + 6, r[1] + 6, r[2] - 12, r[3] - 12), (120, 40, 30, 160))

# журнал
ox, oy, ow, oh = orders
rect((ox, oy + 1, 1, oh - 2), (150, 102, 48, 165))
oy0 = oy + (oh - 58) * .5
label((ox + 7, oy0, ow - 13, 15), "Онмёдзи-архимаги: дух пал — пропускают ход.", 10.5, DIM, "left")
label((ox + 7, oy0 + 15, ow - 13, 15), "Враг: Ронины → Мастера синоби: 36 урона, пало 2.", 10.5, INK, "left")
label((ox + 7, oy0 + 31, ow - 13, 27), "Ход врага: Ронины ×9", 13, GOLD, "left")

# кнопки
tips = ["ЖДАТЬ", "ЗАЩИТА", "АВТОБОЙ", "БЕГСТВО", "СДАТЬСЯ", "УМЕНИЯ"]
tip_size = None
plate = tex("UI/Battle/Kit/hud_slot_label")
for i in range(6):
    r = (actions[0] + i * button + 1, actions[1], button - 2, actions[3])
    isz = max(16.0, min(46.0, min(r[2] - 6, r[3] - 19)))
    ic = (r[0] + r[2] / 2 - isz / 2, r[1], isz, isz)
    draw_tex(ic, tex(f"UI/Battle/btn_{i}"))
    band = (r[0], r[1] + r[3] - 18, r[2], 18)
    draw_tex(band, plate)
    tr = (band[0] + band[2] * .07, band[1] + band[3] * .14, band[2] * .86, band[3] * .72)
    if tip_size is None: tip_size = fit_size(tips, tr[2], tr[3], 10)
    label(tr, tips[i], tip_size, INK)

# очередь и раунд
q = tex("UI/Battle/queue_bar")
qw = min(W * 0.56, 520.0); qh = qw * q.height / q.width
qr = (safe[0] + safe[2] / 2 - qw / 2, safe[1] + 4, qw, qh)
draw_tex(qr, q)
SX = [99, 217, 332, 446, 558, 669, 780, 891, 1004, 1117]; SY = [144, 144, 144, 144, 144, 144, 143, 142, 142, 144]
port = tex("UI/Battle/unit_portraits")
d = qh * (2 * 46 / 269); side = qh * 125 / 269
for i in range(10):
    c = (qr[0] + qw * SX[i] / 1214, qr[1] + qh * SY[i] / 269)
    cellu = i % 12
    draw_tex((c[0] - d / 2, c[1] - d / 2, d, d), port, ((cellu % 4) * .25, 1 - (cellu // 4 + 1) / 3, .25, 1 / 3), alpha=1 if i < 6 else .55)
    draw_tex((c[0] - side / 2, c[1] - side / 2, side, side), tex("UI/Battle/Kit/ring_foe" if i % 2 else "UI/Battle/Kit/ring_ours"))
rr = (qr[0] + qw + 6, qr[1] + qh / 2 - 19, 128, 38)
draw_tex(rr, tex("UI/Battle/Kit/btn_secondary"))
label((rr[0] + rr[2] * .1, rr[1] + rr[3] * .18, rr[2] * .8, rr[3] * .64), "РАУНД 1", 14, GOLD)

os.makedirs(os.path.dirname(out), exist_ok=True)
img.convert("RGB").save(out)
print(out, f"виртуальная ширина {W:.0f}, кнопка {button:.0f}, герой {hero_w:.0f}, журнал {orders[2]:.0f}")
