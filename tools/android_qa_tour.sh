#!/usr/bin/env bash
# Фототур на эмуляторе: ставит тестовый APK (с Debug/QATour.cs), запускает игру и снимает
# экран каждый раз, когда игра пишет в лог «QATOUR_SHOT имя». Конец — «QATOUR_DONE».
# Не падает на первой ошибке: что успело сняться — то и сохраняется, плюс полный logcat.
set -uo pipefail
OUT=build/android-ui
PKG=com.bladepath.prototype
mkdir -p "$OUT/ingame" || { echo "cannot create $OUT"; exit 4; }
touch "$OUT/.write_test" || { echo "cannot write to $OUT"; exit 4; }
APK=$(find build/Android -type f -iname '*VisualEmulator*.apk' -print -quit)
if [[ -z "$APK" ]]; then echo "Missing emulator APK"; exit 2; fi

adb shell wm size 1920x824
adb shell wm density 280
adb shell settings put system accelerometer_rotation 0 || true
adb shell settings put system user_rotation 1 || true
adb install -r "$APK" || { echo "install failed"; exit 3; }
adb logcat -c
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null

failed=0
start=$SECONDS
# лог читается потоком: снимок делается в ту же секунду, как игра написала метку
# (раньше весь лог перечитывался раз в секунду, он рос, и снимки запаздывали на целый экран)
( sleep 1200; pkill -f "adb logcat -v raw -s Unity:I" ) &
watchdog=$!
while IFS= read -r line; do
  case "$line" in
    *QATOUR_SHOT*)
      name=$(sed -n 's/.*QATOUR_SHOT \([0-9A-Za-z_]*\).*/\1/p' <<<"$line")
      [ -z "$name" ] && continue
      adb exec-out screencap -p > "$OUT/$name.png" || true
      sz=$(wc -c < "$OUT/$name.png" 2>/dev/null || echo 0)
      echo "SHOT $name $sz bytes at $((SECONDS - start))s"
      if [ "$sz" -lt 20000 ]; then echo "SHOT FAILED $name ($sz bytes)"; failed=$((failed + 1)); fi
      ;;
    *QATOUR_DONE*) echo "TOUR DONE at $((SECONDS - start))s"; break ;;
  esac
done < <(adb logcat -v raw -s Unity:I)
kill $watchdog 2>/dev/null || true
pkill -f "adb logcat -v raw -s Unity:I" 2>/dev/null || true

adb logcat -d > "$OUT/logcat.txt" || true
grep -E 'QATOUR|Exception|Error' "$OUT/logcat.txt" | grep -v 'Unity   : \s*$' | head -300 > "$OUT/qatour_log.txt" || true
for d in "/sdcard/Android/data/$PKG/files" "/storage/emulated/0/Android/data/$PKG/files"; do
  for f in $(adb shell "ls $d/qa_*.png 2>/dev/null" | tr -d '\r'); do adb pull "$f" "$OUT/ingame/" >/dev/null 2>&1 || true; done
done
n=$(ls "$OUT"/*.png 2>/dev/null | wc -l)
echo "screenshots: $n, in-game: $(ls "$OUT"/ingame/*.png 2>/dev/null | wc -l), failed: $failed"
# ни одного снимка или битые снимки — прогон красный, а не «успешный с пустой папкой»
if [ "$n" -eq 0 ] || [ "$failed" -gt 0 ]; then exit 5; fi
exit 0
