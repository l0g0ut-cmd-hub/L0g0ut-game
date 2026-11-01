#!/usr/bin/env python3
"""Lightweight CI validation for runtime artwork (no Pillow/Unity required)."""
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
EVENTS = ("wounded_ronin", "shrine", "bandits", "fox",
          "herbalist", "merchant", "camp", "ruins")
ICONS = ("node_duel", "node_elite", "node_unknown",
         "node_camp_node", "node_treasure_node")
EFFECTS = ("slash_red", "sparks_gold", "sigil_red", "flame_red", "spirit_wolf")


def validate_png(path, *, sheet=False):
    data = path.read_bytes()
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path}: invalid PNG signature")
    if data[12:16] != b"IHDR":
        raise ValueError(f"{path}: missing IHDR")
    w, h = struct.unpack(">II", data[16:24])
    bit_depth, mode = data[24], data[25]
    if w < 64 or h < 64:
        raise ValueError(f"{path}: too small ({w}x{h})")
    if mode not in (2, 6):
        raise ValueError(f"{path}: unsupported PNG color mode {mode}")
    if bit_depth != 8:
        raise ValueError(f"{path}: unsupported bit depth {bit_depth}")
    if sheet and w != h * 6:
        raise ValueError(f"{path}: six-frame 6x1 sheet expected, got {w}x{h}")
    return w, h


def main():
    errors = []
    expected = [ROOT / "Assets/Resources/UI/pack02" / (name + ".png") for name in EVENTS + ICONS]
    expected.extend(ROOT / "Assets/Resources/FX" / (name + ".png") for name in EFFECTS)
    for path in expected:
        try:
            size = validate_png(path, sheet=path.parent.name == "FX")
            print(f"ART OK {path.relative_to(ROOT)} {size[0]}x{size[1]}")
        except (ValueError, FileNotFoundError) as ex:
            errors.append(str(ex))
    if errors:
        print("\n".join("ART ERROR " + x for x in errors), file=sys.stderr)
        return 1
    print(f"Visual pack OK: {len(expected)} binary resources verified")
    return 0


if __name__ == "__main__":
    sys.exit(main())
