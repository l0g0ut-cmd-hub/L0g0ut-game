#!/bin/sh
# Скачивает библиотеки эффектов (CC0) в tools/vfx_library/src (папка в .gitignore — в APK и репозиторий не попадает).
# Нужное для игры переносится в Assets/_Project/Resources/... вручную, по одному листу.
set -e
cd "$(dirname "$0")"
mkdir -p src
[ -d src/brackeys ] || git clone --depth 1 https://github.com/Brackeys/vfx-in-godot src/brackeys
[ -d src/kenney ] || git clone --depth 1 https://github.com/Calinou/kenney-particle-pack src/kenney
echo "Brackeys: src/brackeys/vfx/brackeys_particle_bundle/{flipbooks,predrawn,particles/alpha}"
echo "Kenney:   src/kenney/addons/kenney_particle_pack"
