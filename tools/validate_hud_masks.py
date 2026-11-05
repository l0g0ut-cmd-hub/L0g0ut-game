#!/usr/bin/env python3
"""Verify authored HUD mask geometry (no Pillow/Unity dependency).

The masks are full-size RGBA-compatible grayscale+alpha PNG resources.
They must line up 1:1 with hud_hp_l.png and hud_ki.png; the pointed tips
MUST NOT be cut off at the old rectangular window endpoints.
"""
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
KIT = ROOT / "Assets/_Project/Resources/UI/Kit"


def read_mask(name, expected):
    data = (KIT / name).read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", name
    w, h = struct.unpack_from(">II", data, 16)
    assert (w, h) == expected, (name, w, h)
    assert data[24:26] == bytes((8, 4)), f"{name}: 8-bit gray+alpha expected"
    pos = 8
    stream = bytearray()
    while pos < len(data):
        n = struct.unpack_from(">I", data, pos)[0]
        kind = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + n]
        check = struct.unpack_from(">I", data, pos + 8 + n)[0]
        assert zlib.crc32(kind + chunk) == check, (name, "PNG CRC", kind)
        if kind == b"IDAT":
            stream.extend(chunk)
        pos += 12 + n
        if kind == b"IEND":
            break
    raw = zlib.decompress(stream)
    stride = w * 2 + 1
    assert len(raw) == h * stride, (name, len(raw))
    assert all(raw[y * stride] == 0 for y in range(h)), "unexpected PNG row filter"
    return lambda x, y: raw[y * stride + 1 + x * 2 + 1]


def check(name, size, middle, tip, outside):
    alpha = read_mask(name, size)
    assert alpha(*middle) == 255, (name, "interior is not opaque")
    assert alpha(*tip) > 140, (name, "ornate tip unfilled", tip, alpha(*tip))
    for xy in outside:
        assert alpha(*xy) == 0, (name, "leaks outside gold frame", xy)
    print(f"HUD MASK OK {name}: {size[0]}x{size[1]}, interior & tip coverage")


def main():
    check("hud_hp_mask.png", (578, 160), (300, 75), (548, 69),
          [(300, 35), (300, 125), (570, 70), (20, 75)])
    check("hud_ki_mask.png", (467, 76), (220, 35), (447, 27),
          [(220, 6), (220, 66), (460, 23), (5, 35)])
    hud = (ROOT / "Assets/_Project/Scripts/Game/DuelHud.cs").read_text()
    assert "DrawArtMaskFill" in hud and "FrameRect(r, frame, win, mirror)" in hud
    assert 'UI/Kit/hud_hp_mask' in hud and 'UI/Kit/hud_ki_mask' in hud
    assert "MakeFrameFillMask(" not in hud and "HpFillArea" not in hud
    print("HUD mask artwork/geometry contract OK")


if __name__ == "__main__":
    main()
