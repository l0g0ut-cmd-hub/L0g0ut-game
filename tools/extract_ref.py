"""
Вырезает элементы из референс-листа (tools/ref_sheet.png, 1536x1024) в текстуры игры:
UI (полоски, кнопки, джойстик, пауза, портреты), кровь, листья клёна, огонь, знамя.
Фон листа ~ (27,27,27): прозрачность — заливкой фона от краёв вырезки (замкнутые тёмные области внутри рамок остаются).
python3 tools/extract_ref.py
"""
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

SRC = np.asarray(Image.open('tools/ref_sheet.png').convert('RGB')).astype(np.float32)
BG = np.array([27, 27, 27], np.float32)
UI = 'Assets/_Project/Resources/UI/'
TX = 'Assets/_Project/Art/Textures/'

def crop(box, pad=4):
    x0, y0, x1, y1 = box
    return SRC[y0 - pad:y1 + pad, x0 - pad:x1 + pad].copy()

def flood_alpha(rgb, thr=16, feather=1.2):
    d = np.abs(rgb - BG).max(axis=2)
    low = d < thr
    lab, n = ndimage.label(low)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    bgmask = np.isin(lab, list(border))
    a = (~bgmask).astype(np.float32)
    a = ndimage.gaussian_filter(a, feather)
    return np.clip(a * 1.15 - 0.08, 0, 1)

def circle_alpha(rgb, shrink=1.0):
    h, w = rgb.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    cy, cx = (h - 1) / 2, (w - 1) / 2
    r = min(h, w) / 2 * shrink
    dist = np.hypot(yy - cy, xx - cx)
    return np.clip(r - dist, 0, 1)

def save(rgb, a, path, size=None):
    img = np.dstack([np.clip(rgb, 0, 255), np.clip(a, 0, 1) * 255]).astype(np.uint8)
    im = Image.fromarray(img, 'RGBA')
    if size: im = im.resize(size, Image.LANCZOS)
    im.save(path)

# ---------- круглые кнопки: (bbox) -> файл
btns = {
    'btn_dash': (991, 728, 1106, 841),     # стрелка
    'btn_jump': (819, 728, 933, 839),      # следы
    'btn_block': (1176, 727, 1291, 842),   # щит
    'btn_heavy': (1344, 677, 1506, 839),   # когти — главный удар
}
for name, box in btns.items():
    c = crop(box, 2)
    a = np.minimum(flood_alpha(c), circle_alpha(c, 0.99))
    save(c, a, UI + name + '.png', (192, 192))

# пустая основа кнопки: щит без значка (светлый значок «выедаем» минимум-фильтром и размываем)
c = crop((1344, 677, 1506, 839), 2)
h, w = c.shape[:2]
yy, xx = np.mgrid[0:h, 0:w]
rr = np.hypot(yy - (h - 1) / 2, xx - (w - 1) / 2) / (min(h, w) / 2)
# камень внутри: тёмно-серый с зерном и лёгким затемнением к краю (как у кнопок листа)
rng = np.random.default_rng(1)
grain = ndimage.gaussian_filter(rng.normal(0, 9, (h, w)), 1.0) + ndimage.gaussian_filter(rng.normal(0, 30, (h, w)), 6)
shade = (1 - 0.35 * rr ** 2)
inner = (np.array([34, 33, 35], np.float32)[None, None, :] + grain[..., None]) * shade[..., None]
k = np.clip((rr - 0.78) / 0.04, 0, 1)[..., None]       # 0 внутри, 1 на рамке
blank = inner * (1 - k) + c * k
a = np.minimum(flood_alpha(c), circle_alpha(c, 0.99))
save(blank, a, UI + 'btn_base.png', (192, 192))

# ---------- джойстик: основа без шайбы + шайба отдельно
c = crop((638, 726, 774, 863), 2)
h, w = c.shape[:2]
cy, cx = (h - 1) / 2, (w - 1) / 2
yy, xx = np.mgrid[0:h, 0:w]
dist = np.hypot(yy - cy, xx - cx)
knob_r = 26
knob = c[int(cy - knob_r - 2):int(cy + knob_r + 3), int(cx - knob_r - 2):int(cx + knob_r + 3)]
save(knob, circle_alpha(knob, 0.98), UI + 'stick_knob.png', (96, 96))
fill = ndimage.gaussian_filter(ndimage.minimum_filter(c, size=(15, 15, 1)), (4, 4, 0))
km = np.clip((dist - knob_r - 4) / 4, 0, 1)[..., None]
base = c * km + fill * (1 - km)
save(base, np.minimum(flood_alpha(c), circle_alpha(c, 0.99)), UI + 'stick_base.png', (256, 256))

# ---------- пауза (ромб)
c = crop((516, 739, 606, 828), 3)
h, w = c.shape[:2]
yy, xx = np.mgrid[0:h, 0:w]
dm = np.abs(yy - (h - 1) / 2) + np.abs(xx - (w - 1) / 2)
save(c, np.clip(min(h, w) / 2 - 3 - dm, 0, 1), UI + 'btn_pause.png', (128, 128))

# ---------- полоски: рамка с прозрачным «окном» под заливку
def bar(box, name, win):
    c = crop(box, 3)
    a = flood_alpha(c)
    x0, y0, x1, y1 = win
    w = np.zeros(a.shape, np.float32)
    w[y0:y1 + 1, x0:x1 + 1] = 1
    a = np.minimum(a, 1 - w)
    save(c, a, UI + name + '.png')
    print(name, 'size', c.shape[1], c.shape[0], 'window', win)
bar((25, 748, 495, 795), 'bar_hp', (42, 19, 439, 33))
bar((27, 810, 443, 851), 'bar_ki', (38, 17, 398, 29))

# ---------- портреты
for name, box in {'portrait_ronin': (38, 869, 156, 985), 'portrait_undead': (173, 868, 292, 985)}.items():
    c = crop(box, 2)
    save(c, np.minimum(flood_alpha(c), circle_alpha(c, 0.99)), UI + name + '.png', (160, 160))

# ---------- кровь: альфа по «красноте» над фоном
c = crop((320, 850, 500, 1000), 6)
r = c[..., 0] - BG[0]; g = c[..., 1] - BG[1]
a = np.clip((r - g * 0.6) / 40, 0, 1)
# белая форма с лёгкой вариацией яркости: цвет крови задаёт код (тон декалей и брызг на «объективе»)
v = 0.7 + 0.3 * np.clip(c[..., 0] / 160, 0, 1)
col = np.dstack([v, v, v]) * 255
save(col, a, TX + 'blood_splat.png', (256, 256))

# ---------- лист клёна (самый крупный)
c = crop((585, 901, 628, 950), 6)
r = c[..., 0] - BG[0]; g = c[..., 1] - BG[1]
a = np.clip((r - g) / 60, 0, 1)
save(c * 1.25, a, TX + 'maple_leaf.png', (64, 64))

# ---------- огонь: аддитивный спрайт (фон вычитаем, чёрный = прозрачно)
c = crop((943, 845, 1074, 999), 6)
add = np.clip((c - BG) * 1.25, 0, 255)
save(add, np.ones(add.shape[:2]), TX + 'fire_sprite.png', (128, 128))

# ---------- знамя: рваная красная ткань с гербом
c = crop((919, 31, 1019, 319), 3)
a = flood_alpha(c, thr=20)
save(c, a, TX + 'banner.png', (128, 384))
# рантайм-эффекты читают кровь и лист из Resources/FX
import shutil
for f in ('blood_splat.png', 'maple_leaf.png'): shutil.copy(TX + f, 'Assets/_Project/Resources/FX/' + f)
print('done')
