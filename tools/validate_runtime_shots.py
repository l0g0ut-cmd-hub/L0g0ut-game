#!/usr/bin/env python3
"""Check real Unity screenshots, rejecting repeated studio intro frames."""
from pathlib import Path
from PIL import Image, ImageChops, ImageStat
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else 'build/visual-runtime')
names = ['main_menu'] + [f'difficulty_{d}_fog_{f}' for d in range(3) for f in range(2)] + [f'traveller_{h}' for h in range(3)]
images = {}
for name in names:
    p = root / (name + '.png')
    if not p.is_file() or p.stat().st_size < 16000:
        raise SystemExit(f'FAIL: absent/small screenshot: {p}')
    im = Image.open(p).convert('RGB')
    if im.width < 700 or im.height < 300:
        raise SystemExit(f'FAIL: suspicious render size: {name} {im.size}')
    stat = ImageStat.Stat(im.resize((120,60)))
    if max(stat.stddev) < 7:
        raise SystemExit(f'FAIL: flat/blank screenshot: {name}')
    images[name] = im

def delta(a,b):
    first=images[a].resize((320,144))
    second=images[b].resize((320,144))
    return ImageStat.Stat(ImageChops.difference(first,second)).mean[0]

for name in names[1:]:
    change = delta('main_menu',name)
    if change < 8:
        raise SystemExit(f'FAIL: {name} looks like studio splash/main menu; difference {change:.2f}')
for first,second in [
        ('difficulty_0_fog_0','difficulty_1_fog_0'),
        ('difficulty_1_fog_0','difficulty_2_fog_0'),
        ('difficulty_2_fog_0','difficulty_2_fog_1')]:
    change = delta(first,second)
    print(first, second, 'mean difference:', round(change, 3))
    if change < 0.055:
        raise SystemExit(f'FAIL: UI did not respond to selection: {first} -> {second}')
print('PASS: ten valid and distinct Unity IMGUI screenshots')
