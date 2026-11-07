# Навыки для ИИ-агентов (Claude Code и др.)

Взяты из https://github.com/gamedev-skills/awesome-gamedev-agent-skills (коммит 19d93f9, 2026-10-28, Apache-2.0 —
см. LICENSE- и NOTICE-файлы рядом). Оставлено только то, что нужно «Пути клинка» (Unity 6, Android, IMGUI, файтинг + карта похода):

- unity-csharp-scripting, unity-build-pipeline, unity-animation, unity-scriptableobjects — Unity 6
- game-feel (удар, хит-стоп, тряска), camera-systems — бой
- performance-optimization — FPS на телефоне, лишние выделения памяти (GC)
- game-ui-ux — интерфейс, безопасная зона экрана
- save-systems — сохранения (PlayerPrefs JSON: версия, резервная копия)
- create-game-assets — атласы от ChatGPT: единый стиль, нормализация, проверка в игре
- audio-design — звук
- rpg, roguelike — прокачка, добыча, карта похода

Claude Code подхватывает папку .claude/skills сам.

Добавлено 2026-11-07:
- shader-techniques, particle-systems — из https://github.com/pluginagentmarketplace/custom-plugin-game-developer
  (коммит aa7edfe; своя лицензия авторов: распространение разрешено вместе с лицензией — LICENSE-custom-plugin-game-developer)
- tools-unity-primetween, tools-unity-object-pooling, eng-unity-mobile-optimization — из
  https://github.com/tjboudreaux/cc-plugin-unity-gamedev (коммит 3c7670a, MIT — LICENSE-cc-plugin-unity-gamedev)

Библиотеки и ассеты эффектов (см. tools/vfx_library/README.md):
- PrimeTween — пакет npm в Packages/manifest.json (лицензия автора разрешает только так, не копией исходников)
- Brackeys VFX Bundle (CC0) — github.com/Brackeys/vfx-in-godot, коммит 23b58f2
- Kenney Particle Pack (CC0) — github.com/Calinou/kenney-particle-pack, коммит ab70866
