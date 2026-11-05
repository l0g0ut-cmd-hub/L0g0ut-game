#!/usr/bin/env python3
"""Validate authored HP tip sprites and KI masks without Pillow or Unity.

The two HP end shapes are baked; the straight center is calculated by code.
The original gold frame art is never modified.
"""
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
KIT = ROOT / "Assets/_Project/Resources/UI/Kit"


def read_alpha(name, expected, mode):
    data = (KIT / name).read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", name
    width, height = struct.unpack_from(">II", data, 16)
    assert (width, height) == expected, (name, width, height)
    assert data[24] == 8 and data[25] == mode, (name, "PNG bit depth/color type")
    pos = 8
    stream = bytearray()
    transparency = None
    while pos < len(data):
        n = struct.unpack_from(">I", data, pos)[0]
        kind = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + n]
        crc = struct.unpack_from(">I", data, pos + 8 + n)[0]
        assert zlib.crc32(kind + chunk) == crc, (name, "PNG CRC", kind)
        if kind == b"tRNS":
            transparency = chunk
        if kind == b"IDAT":
            stream.extend(chunk)
        pos += n + 12
        if kind == b"IEND":
            break
    raw = zlib.decompress(stream)
    channels = {3: 1, 4: 2, 6: 4}[mode]
    stride = width * channels + 1
    assert len(raw) == height * stride, (name, "decompressed length")
    assert all(raw[y * stride] == 0 for y in range(height)), "unexpected row filter"
    if mode == 3:
        assert transparency is not None and len(transparency) == 256
        return lambda x, y: transparency[raw[y * stride + x + 1]]
    return lambda x, y: raw[y * stride + 1 + channels * x + (3 if mode == 6 else 1)]


def main():
    cap = read_alpha("hud_hp_caps.png", (578, 160), 6)
    for xy in ((112, 79), (141, 79), (501, 79), (533, 75), (540, 74), (554, 68)):
        assert cap(*xy) > 0, ("HP tapered end missing", xy, cap(*xy))
    for xy in ((300, 76), (0, 79), (575, 79), (110, 20), (533, 120)):
        assert cap(*xy) == 0, ("HP cap leaks outside authored tips", xy)
    ki = read_alpha("hud_ki_caps.png", (467, 76), 6)
    for xy in ((50, 35), (82, 35), (99, 35), (385, 35), (401, 35), (425, 35), (447, 27)):
        assert ki(*xy) > 0, ("KI endcap missing", xy)
    for xy in ((220, 35), (0, 35), (460, 35), (220, 6), (220, 66)):
        assert ki(*xy) == 0, ("KI endcaps leak into center/outside frame", xy)
    hud = (ROOT / "Assets/_Project/Scripts/Game/DuelHud.cs").read_text()
    assert 'UI/Kit/hud_hp_caps' in hud and 'UI/Kit/hud_ki_caps' in hud
    assert "DrawHpSegmentedFill" in hud and "DrawKiSegmentedFill" in hud
    assert "DrawArtMaskFill(" not in hud and "KiMaskChannel" not in hud
    assert "GUI.DrawTexture(mid, Texture2D.whiteTexture)" in hud
    assert hud.count("if (tips != null)") >= 2, "HP and KI must render with missing caps"
    assert "const float authoredWidth = 578f, authoredHeight = 160f" in hud
    assert "tips == null ||" not in hud, "fatal cap guard must not disable HP or KI"
    assert "const float authoredWidth = 467f, authoredHeight = 76f" in hud
    assert "const float midX0 = 83f, midX1 = 384f" in hud
    assert "const float leftSource = 94f, rightSource = 567f" in hud
    assert "const float midX0 = 140f, midX1 = 494f" in hud
    assert "MakeFrameFillMask(" not in hud
    assert not (KIT / "hud_hp_states.png").exists(), "101-frame atlas still in project"
    assert not (KIT / "hud_ki_mask.png").exists(), "obsolete KI full mask still in project"
    print("HUD MASK OK: authored HP and KI tips, calculated middles, safe fallback")


if __name__ == "__main__":
    main()
