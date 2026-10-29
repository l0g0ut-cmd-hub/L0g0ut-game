"""Красные кандзи кистью для HUD (как 忍殺 / 危 в Sekiro): шрифт Yuji Boku (SIL OFL, google/fonts),
рваный край, сухая кисть на концах штрихов, брызги туши, тёмная тень и красное свечение для читаемости на светлом сёдзи.

    python3 tools/texgen/brush_glyphs.py <YujiBoku-Regular.ttf> <out_dir>

Выход: deathblow.png (忍殺, широкая), zan.png (斬), kiken.png (危) — RGBA, прямая альфа.
"""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage

FONT, OUT = sys.argv[1], sys.argv[2]
rng = np.random.default_rng(7)


def noise(h, w, scale, octaves=4, stretch=(1.0, 1.0)):
    """Гладкий шум (сумма октав увеличенного белого шума), stretch — вытянутость по (y, x)."""
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        s = scale / (2 ** o)
        gh, gw = max(2, int(h / (s * stretch[0])) + 2), max(2, int(w / (s * stretch[1])) + 2)
        g = rng.random((gh, gw)).astype(np.float32)
        up = np.array(Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255.0
        acc += amp * up; tot += amp; amp *= 0.5
    return acc / tot


def glyph(text, size_px, pad):
    ss = 2   # сглаживание: рисуем вдвое крупнее
    ft = ImageFont.truetype(FONT, size_px * ss)
    l, t, r, b = ft.getbbox(text)
    W, H = (r - l) + 2 * pad * ss, (b - t) + 2 * pad * ss
    im = Image.new("L", (W, H), 0)
    ImageDraw.Draw(im).text((pad * ss - l, pad * ss - t), text, font=ft, fill=255)
    m = np.array(im, np.float32) / 255.0
    h, w = m.shape
    # рваный край: искажение маски шумом (кисть, впитавшаяся бумага)
    dx = (noise(h, w, 18 * ss) - 0.5) * 7 * ss
    dy = (noise(h, w, 18 * ss) - 0.5) * 7 * ss
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    m = ndimage.map_coordinates(m, [yy + dy, xx + dx], order=1, mode="constant")
    # сухая кисть: у краёв штриха и на «хвостах» тушь рвётся вытянутыми полосами
    inside = ndimage.distance_transform_edt(m > 0.5)
    streak = noise(h, w, 10 * ss, octaves=3, stretch=(0.25, 3.0))   # полосы по горизонтали (ход кисти)
    streak2 = noise(h, w, 10 * ss, octaves=3, stretch=(3.0, 0.25))  # и по вертикали
    st = np.maximum(streak, streak2)
    edge = np.clip(inside / (9.0 * ss), 0.0, 1.0)                    # 0 у края → 1 внутри штриха
    dry = np.clip((st - 0.42) * 4.0, 0.0, 1.0)
    a = m * np.clip(edge * 1.4 + dry * (1.0 - edge) * 1.2 - (1.0 - edge) * 0.25, 0.0, 1.0)
    a = np.where(inside > 3.5 * ss, m, a)   # сердцевина штриха — сплошная
    # брызги туши вокруг
    spl = Image.new("L", (w, h), 0); dr = ImageDraw.Draw(spl)
    ys, xs = np.nonzero(m > 0.5)
    for _ in range(int(60 * w / (600 * ss))):
        k = rng.integers(len(xs)); cx, cy = xs[k], ys[k]
        ang = rng.random() * 2 * np.pi; dist = (8 + rng.pareto(2.2) * 26) * ss
        px, py = cx + np.cos(ang) * dist, cy + np.sin(ang) * dist
        rad = min(3.2 * ss, max(1.0, (rng.pareto(2.6) * 2.2 + 1.0) * ss))   # мелкие брызги, без клякс
        if rng.random() < 0.18:   # вытянутая капля по направлению разлёта
            ex, ey = np.cos(ang) * rad * 2.6, np.sin(ang) * rad * 2.6
            dr.line([(px - ex, py - ey), (px + ex, py + ey)], fill=255, width=int(rad * 1.3) + 1)
        else:
            dr.ellipse([px - rad, py - rad, px + rad, py + rad], fill=255)
    s = np.array(spl, np.float32) / 255.0
    s *= np.clip(1.0 - m * 2, 0, 1)
    a = np.clip(np.maximum(a, s * 0.95), 0.0, 1.0)
    # вниз до целевого размера
    A = np.array(Image.fromarray((a * 255).astype(np.uint8)).resize((w // ss, h // ss), Image.LANCZOS), np.float32) / 255.0
    core = np.array(Image.fromarray((np.clip(inside / (6.0 * ss), 0, 1) * 255).astype(np.uint8)).resize((w // ss, h // ss), Image.LANCZOS), np.float32) / 255.0
    return A, core


def compose(A, core):
    h, w = A.shape
    tone = noise(h, w, 30, octaves=3)
    # тушь: насыщенный киноварный в сердцевине, темнее и суше к краю
    base = np.stack([0.78 + 0.1 * tone, 0.05 + 0.03 * tone, 0.04 + 0.02 * tone], -1)
    dark = np.array([0.40, 0.02, 0.02])
    k = np.clip(core, 0, 1)[..., None]
    col = dark * (1 - k) + base * k
    # тень (чтобы читалось на светлых сёдзи) и тлеющее красное свечение
    shadow = np.array(Image.fromarray((A * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(w / 90)), np.float32) / 255.0
    glow = np.array(Image.fromarray((A * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(w / 28)), np.float32) / 255.0
    sa = np.clip(shadow * 0.75, 0, 1); ga = np.clip(glow * 0.55, 0, 1)
    # обводка: плотный тёмный контур вокруг туши — знак не сливается с кровью и красным светом
    t = max(2.0, 0.02 * min(h, w))
    dist = ndimage.distance_transform_edt(~((A > 0.5) & (core > 0.02)))   # контур по штрихам, брызги без обводки
    oa = np.clip((t - dist) / 1.2 + 0.5, 0, 1) * 0.96
    # слои снизу вверх: свечение (красное) → тень (почти чёрная) → обводка → тушь
    out_c = np.zeros((h, w, 3)); out_a = np.zeros((h, w))
    for c_, a_ in ((np.array([0.85, 0.08, 0.04]), ga), (np.array([0.05, 0.0, 0.0]), sa), (np.array([0.04, 0.01, 0.01]), oa)):
        out_c = c_ * a_[..., None] + out_c * (1 - a_[..., None]); out_a = a_ + out_a * (1 - a_)
    out_c = col * A[..., None] + out_c * (1 - A[..., None]); out_a = A + out_a * (1 - A)
    # прямая альфа: цвет делим обратно
    straight = np.where(out_a[..., None] > 1e-4, out_c / np.maximum(out_a[..., None], 1e-4), 0)
    rgba = np.concatenate([np.clip(straight, 0, 1), out_a[..., None]], -1)
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")


def make(text, size_px, pad, name, square=False):
    A, core = glyph(text, size_px, pad)
    im = compose(A, core)
    # холст в степень двойки с теми же пропорциями (Unity растягивает NPOT-текстуры): квадрат 256 или 2:1 1024×512
    tw, th = (256, 256) if square else (1024, 512)
    s = max(im.size[0] / tw, im.size[1] / th)
    cw, ch = int(round(tw * s)), int(round(th * s))
    sq = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    sq.paste(im, ((cw - im.size[0]) // 2, (ch - im.size[1]) // 2)); im = sq.resize((tw, th), Image.LANCZOS)
    im.save(os.path.join(OUT, name))
    print(name, im.size)


os.makedirs(OUT, exist_ok=True)
make("忍殺", 300, 70, "deathblow.png")
make("斬", 300, 70, "zan.png", square=True)
make("危", 300, 70, "kiken.png", square=True)
make("死", 300, 70, "shi.png", square=True)    # поражение (как экран смерти Sekiro)
make("勝", 300, 70, "sho.png", square=True)    # победа


def enso(name, S=256):
    """Метка добивания (как красная точка Sekiro): энсо — жирный круг одним мазком кисти, сухой хвост, красное свечение."""
    ss = 2; W = S * ss
    yy, xx = np.mgrid[0:W, 0:W].astype(np.float32)
    cx = cy = W / 2; R = W * 0.27
    dx, dy = xx - cx, yy - cy
    r = np.sqrt(dx * dx + dy * dy)
    ang = (np.arctan2(dy, dx) + np.pi * 0.62) % (2 * np.pi)   # начало мазка — слева вверху
    u = ang / (2 * np.pi)                                     # 0 … 1 вдоль мазка
    width = (0.17 * (1 - u) ** 0.7 + 0.03) * W                # жирное начало → тонкий хвост
    width *= 0.45 + 0.55 * np.sqrt(np.clip(u / 0.06, 0, 1))   # кисть касается бумаги — скруглённое начало
    wob = (noise(W, W, 40 * ss) - 0.5) * 0.03 * W
    d = np.abs(r - R - wob) / np.maximum(width * 0.5, 1.0)
    edge_n = (noise(W, W, 5 * ss) - 0.5) * 0.35
    body = (d < 1.0 + edge_n).astype(np.float32) * (u < 0.94)
    # сухая кисть только к хвосту: полосы вдоль окружности (шум по радиусу)
    lanes = np.interp(np.clip((r - R) / (0.2 * W) * 60 + 60, 0, 119), np.arange(120), ndimage.gaussian_filter1d(rng.random(120), 1.2))
    lanes = (lanes - lanes.min()) / (lanes.max() - lanes.min() + 1e-6)
    tail = np.clip((u - 0.55) / 0.35, 0, 1)
    a = body * np.clip(1.0 - tail * (1.0 - np.clip((lanes - 0.25) * 2.5, 0, 1)) * 1.1, 0, 1)
    a = ndimage.gaussian_filter(a, 0.7 * ss)
    A = np.array(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).resize((S, S), Image.LANCZOS), np.float32) / 255.0
    inside = ndimage.distance_transform_edt(A > 0.5)
    core = np.clip(inside / 5.0, 0, 1)
    im = compose(A, core)
    im.save(os.path.join(OUT, name)); print(name, im.size)


enso("deathblow_mark.png")
