#!/usr/bin/env python3
"""Visual acceptance gate for the integrated adventure and combat UI.

This deliberately checks *actual art pixel data*, not only PNG signatures:
the journey die must have transparency, three visibly different pip counts
and non-flat shading. It also checks that the authored illustration paths
exist and that every major game UI uses the shared design language.

This catches bad/missing art before running a costly Unity Android build.
Unity compilation and a device screenshot review remain separate gates.
"""
from pathlib import Path
import struct
import sys
import zlib

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / "Assets/_Project/Resources/UI"


def text(rel):
    path = ROOT / rel
    if not path.is_file():
        raise AssertionError(f"Missing C# UI component: {rel}")
    return path.read_text(encoding="utf-8")


def rgba_png(path):
    if not path.is_file():
        raise AssertionError(f"Missing authored visual asset: {path}")
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise AssertionError(f"Invalid PNG: {path}")
    idx = 8
    width = height = bit_depth = color_type = None
    compressed = bytearray()
    while idx < len(data):
        if idx + 12 > len(data):
            raise AssertionError(f"Truncated PNG: {path}")
        n = struct.unpack(">I", data[idx:idx + 4])[0]
        kind = data[idx + 4:idx + 8]
        value = data[idx + 8:idx + 8 + n]
        idx += n + 12
        if kind == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", value[:10])
        if kind == b"IDAT":
            compressed += value
        if kind == b"IEND":
            break
    if bit_depth != 8 or color_type != 6:
        raise AssertionError("Journey die must be 8-bit RGBA")
    stride = width * 4
    raw = zlib.decompress(compressed)
    if len(raw) != height * (stride + 1):
        raise AssertionError("Journey die has invalid decompressed pixel length")
    if any(raw[y * (stride + 1)] != 0 for y in range(height)):
        raise AssertionError("Journey die PNG uses an unexpected row filter")
    return width, height, raw


def check_die():
    die = UI / "map_dice_3d.png"
    width, height, raw = rgba_png(die)
    if (width, height) != (384, 128):
        raise AssertionError(f"Unexpected 3x1 die atlas dimensions: {width}x{height}")

    def pixel(x, y):
        i = y * (width * 4 + 1) + 1 + x * 4
        return raw[i:i+4]

    if any(pixel(x, 0)[3] > 5 for x in (1, 127, 128, 255, 256, 383)):
        raise AssertionError("Journey die loses transparent silhouettes")

    red_counts = []
    for face in range(3):
        found = 0
        colors = set()
        for y in range(22, 102):
            for x in range(face * 128 + 17, face * 128 + 113):
                r, g, b, alpha = pixel(x, y)
                if alpha < 220:
                    continue
                colors.add((r // 16, g // 16, b // 16))
                if 40 < r < 190 and g < 85 and b < 85 and r > g * 1.6:
                    found += 1
        if len(colors) < 18:
            raise AssertionError(f"Die face {face+1} appears flat or unshaded")
        red_counts.append(found)
    if not (red_counts[0] > 20 and
            red_counts[1] > red_counts[0] * 1.45 and
            red_counts[2] > red_counts[1] * 1.18):
        raise AssertionError(f"Die pips are missing or identical across faces: {red_counts}")
    print(f"DIE 3D art OK: {width}x{height}, visible ruby pip pixels = {red_counts}")


def check_design_system():
    map_code = text("Assets/_Project/Scripts/Game/MapScreen.cs")
    inventory = text("Assets/_Project/Scripts/Game/InventoryScreen.cs")
    combat = text("Assets/_Project/Scripts/Game/DuelHud.cs")
    menu = text("Assets/_Project/Scripts/Menu/MainMenuController.cs")
    controls = text("Assets/_Project/Scripts/Input/PrototypeInput.cs")
    kit = text("Assets/_Project/Scripts/Game/UiKit.cs")
    theme = text("Assets/_Project/Scripts/Game/VisualIdentity.cs")

    checks = {
        "die image loaded from Assets Resources":
            'UiKit.Tex("UI/map_dice_3d")' in map_code,
        "dice selected via three distinct atlas cells":
            "new Rect(index / 3f, 0f, 1f / 3f, 1f)" in map_code,
        "map lacquer panel":
            "VisualIdentity.Surface(r" in map_code,
        "map event art uses authored illustrations":
            'Picture(new Rect(r.x, r.y, 330f, r.height), "UI/event_" + ev)' in map_code,
        "map NPC art not low-res placeholders":
            'Pack02Art(' not in map_code,
        "map herbalist preview":
            'case NodeType.Herbalist: return "UI/npc_4";' in map_code,
        "map story cards unified":
            'VisualIdentity.Portrait(r, UiKit.Tex(tex) != null ? tex : "UI/path_panorama")' in map_code,
        "inventory unified":
            "VisualIdentity.Surface(panel" in inventory and
            "VisualIdentity.Heading(" in inventory,
        "duel actions have room for full Russian labels":
            'Btn(new Rect(x, r.yMax - 116f, w, 44f), "В БОЙ"' in map_code and
            'Btn(new Rect(x, r.yMax - 64f, w, 44f), "СНАРЯЖЕНИЕ"' in map_code and
            'w * 0.38f, 48f), "СНАРЯЖЕНИЕ"' not in map_code,
        "combat combo unified":
            '"UI/brush_digits_white"' in combat and
            "const float ComboMaxH = 66f;" in combat and
            "DrawCombo(duel.enemy, new Vector2(W - 70f, 121f), TextAnchor.MiddleRight);" in combat,
        "menu unified":
            "VisualIdentity.Surface(box" in menu,
        "combat pause unified":
            "VisualIdentity.Surface(pr" in controls or
            "VisualIdentity.Surface(r" in controls,
        "base typography remains intact":
            "GUI.Label(pr, s, label)" in kit and "label.CalcSize" in kit,
        "theme provides shared assets":
            all(name in theme for name in
                ("Color Brass", "Color Lacquer", "Color Parchment",
                 "void Portrait(", "bool Button(", "void Surface("))
    }
    for key, ok in checks.items():
        if not ok:
            raise AssertionError(f"Visual regression: {key}")
        print("UI OK", key)

    authored_jpegs = [
        "event_0.jpg", "event_1.jpg", "event_2.jpg", "event_3.jpg",
        "event_4.jpg", "event_5.jpg", "event_6.jpg", "event_7.jpg",
        "npc_0.jpg", "npc_1.jpg", "npc_2.jpg", "npc_3.jpg", "npc_4.jpg",
    ]
    originals = ["plank.png", "plank_red.png", "map_icons.png",
                 "path_panorama.png", "map_dice_3d.png"]
    for name in authored_jpegs + originals:
        if not (UI / name).is_file():
            raise AssertionError(f"Required original UI art missing: {name}")
    herbalist = ROOT / "Assets/Resources/UI/pack02/herbalist.png"
    if not herbalist.is_file():
        raise AssertionError("Herbalist fallback art is missing")
    print(f"AUTHORED ART OK: {len(authored_jpegs)} portraits/events and "
          f"{len(originals)} ornaments, 1 herbalist resource")


if __name__ == "__main__":
    try:
        check_die()
        check_design_system()
    except (AssertionError, OSError, ValueError, zlib.error) as exc:
        print("VISUAL REGRESSION:", exc, file=sys.stderr)
        sys.exit(1)
    print("Visual consistency acceptance gate: OK")
