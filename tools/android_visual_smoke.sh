#!/usr/bin/env bash
# Screenshots of actual installed QA APK, at 21:9, with stable tap positions.
set -euo pipefail
OUT=build/android-ui
mkdir -p "$OUT"
# The release build may coexist with the emulator QA package. Only install x86_64 QA.
APK=$(find build/Android -type f -iname '*VisualEmulator*.apk' -print -quit)
if [[ -z "$APK" ]]; then echo 'Missing APK for emulator'; exit 2; fi
adb shell wm size 1920x824
adb shell wm density 280
adb shell settings put system accelerometer_rotation 0 || true
adb shell settings put system user_rotation 1 || true
adb install -r "$APK"
adb shell monkey -p com.bladepath.prototype -c android.intent.category.LAUNCHER 1
sleep 19
shot() {
  local name="$1"
  adb exec-out screencap -p > "$OUT/$name.png"
  test "$(wc -c < "$OUT/$name.png")" -ge 18000 || { echo "Screenshot empty: $name"; exit 3; }
  echo "SHOT $name $(wc -c < "$OUT/$name.png") bytes"
}
shot 00_main_menu
adb shell input tap 430 386; sleep 4
shot 01_difficulty_initial
adb shell input tap 525 365; sleep 2; shot 02_easy
adb shell input tap 920 365; sleep 2; shot 03_warrior
adb shell input tap 1320 365; sleep 2; shot 04_master
adb shell input tap 1680 711; sleep 2; shot 05_fog_toggled
adb shell input tap 1680 711; sleep 2; shot 06_fog_restored
adb shell input tap 620 720; sleep 4; shot 07_traveller
adb shell input tap 900 365; sleep 1; shot 08_hero_selected
adb shell input tap 750 735; sleep 20; shot 09_game_started
adb logcat -d -s Unity:E AndroidRuntime:E > "$OUT/errors.log" || true
python3 - <<'PY'
from pathlib import Path
from PIL import Image, ImageStat
files=sorted(Path('build/android-ui').glob('*.png'))
if len(files)!=10: raise SystemExit(f'Expected ten APK screenshots; got {len(files)}')
for path in files:
  img=Image.open(path).convert('RGB')
  stat=ImageStat.Stat(img.resize((80,40)))
  if max(stat.stddev)<5: raise SystemExit(f'Flat/unrendered APK frame: {path}')
  print(path, img.size, 'stddev', [round(x,1) for x in stat.stddev])

# Validate that the APK actually switches fog state and that the approved
# carved outer frame does NOT jump between OFF and ON.
from PIL import ImageChops
on=Image.open('build/android-ui/05_fog_toggled.png').convert('RGB')
off=Image.open('build/android-ui/06_fog_restored.png').convert('RGB')
if on.size!=off.size:
  raise SystemExit('Fog screenshots changed device resolution')
w,h=on.size
scale=max(w/1824.,h/862.)
ox=(w-1824.*scale)*.5
oy=(h-862.*scale)*.5
bounds=(round(ox+1485*scale), round(oy+683*scale),
        round(ox+1754*scale), round(oy+801*scale))
x0,y0,x1,y1=bounds
if not (0<=x0<x1<=w and 0<=y0<y1<=h):
  raise SystemExit(f'Fog UI rectangle outside Android screenshot: {bounds}, screen={(w,h)}')
a=on.crop(bounds); b=off.crop(bounds)
diff=ImageChops.difference(a,b)
diff.save('build/android-ui/fog_on_off_difference.png')
changed=sum(ImageStat.Stat(diff.resize((100,50))).mean)/3.
print('FOG TOGGLE REAL ANDROID CHECK: bounds=',bounds,'state mean RGB change=',round(changed,2))
if changed<5.:
  raise SystemExit('Fog switch ON and OFF are visually identical in the actual APK')
# The 9px perimeter of the switch is decorative and must remain stationary.
band=max(3,round(7*scale))
regions=[
  (0,0,a.width,band),(0,a.height-band,a.width,a.height),
  (0,band,band,a.height-band),(a.width-band,band,a.width,a.height-band),
]
border_means=[]
for region in regions:
  perimeter=diff.crop(region)
  border_means.append(sum(ImageStat.Stat(perimeter).mean)/3.)
print('FOG FRAME perimeter mean RGB differences:',[round(v,2) for v in border_means])
if max(border_means)>14.:
  raise SystemExit('Approved fog toggle outer frame shifts or degrades between ON and OFF')
PY