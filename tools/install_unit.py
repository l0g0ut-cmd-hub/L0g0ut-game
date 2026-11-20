#!/usr/bin/env python3
"""Готовую анимацию Meshy из ветки meshy-gen → Resources/Units/unit_<имя>.fbx + _albedo.jpg.

  python3 tools/install_unit.py anim_oni oni [альбедо.jpg|png]

Берёт <папка>/<папка>_processed_animation_fps_fbx_url.fbx (30 к/с), иначе animation_fbx_url.fbx.
Альбедо: аргумент, иначе tools/models/meshy/<имя>_rig_albedo.jpg, иначе *base_color* из forest_<имя>/unit_<имя>.
"""
import os, subprocess, sys, io
from PIL import Image

REF = "refs/remotes/origin/meshy-gen"
DST = "Assets/_Project/Resources/Units/"


def blob(path):
    return subprocess.run(["git", "show", f"{REF}:{path}"], capture_output=True, check=True).stdout


def files():
    return subprocess.run(["git", "ls-tree", "-r", "--name-only", REF], capture_output=True, text=True, check=True).stdout.split()


def main():
    folder, name = sys.argv[1], sys.argv[2]
    al = sys.argv[3] if len(sys.argv) > 3 else ""
    fl = files()
    fbx = next((f for f in fl if f.startswith(folder + "/") and "processed_animation_fps_fbx" in f), None) \
        or next((f for f in fl if f.startswith(folder + "/") and f.endswith("animation_fbx_url.fbx")), None)
    if not fbx: sys.exit("нет FBX анимации в " + folder)
    open(DST + f"unit_{name}.fbx", "wb").write(blob(fbx))
    if al and os.path.exists(al): img = Image.open(al)
    elif os.path.exists(f"tools/models/meshy/{name}_rig_albedo.jpg"): img = Image.open(f"tools/models/meshy/{name}_rig_albedo.jpg")
    else:
        bc = next((f for f in fl if f.split("/")[0] in (f"forest_{name}", f"unit_{name}", folder) and "base_color" in f), None)
        if not bc: sys.exit("нет альбедо для " + name)
        img = Image.open(io.BytesIO(blob(bc)))
    img = img.convert("RGB")
    if img.width > 1024: img = img.resize((1024, 1024), Image.LANCZOS)
    img.save(DST + f"unit_{name}_albedo.jpg", quality=90)
    print(DST + f"unit_{name}.fbx", os.path.getsize(DST + f"unit_{name}.fbx") // 1024, "КБ, альбедо", img.size)


if __name__ == "__main__":
    main()
