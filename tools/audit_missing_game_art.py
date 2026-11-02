#!/usr/bin/env python3
"""Audit art required by BladePath. Never pretend a fallback is finished art.

Run: python3 tools/audit_missing_game_art.py
The check is non-destructive and does not block the running Android build.
"""
from pathlib import Path
import json
import struct

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / "Assets/_Project/Resources/UI"
REQUIRED = {
    "travellers": [f"selection/hero_{i}.jpg" for i in range(3)],
    "difficulties": [f"selection/difficulty_{i}.jpg" for i in range(3)],
    "rider": ["map_rider_16.png"],
}
EXISTING = {
    "maps": [f"map_{i}.jpg" for i in range(3)],
    "events": [f"event_{i}.jpg" for i in range(4)],
    "npc": [f"npc_{i}.jpg" for i in range(4)],
    "healing": ["../FX/fx_heal_hd.png"],
}

def png_size(path):
    with path.open("rb") as f:
        header = f.read(24)
    if len(header) < 24 or header[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", header[16:24])

def jpeg_size(path):
    """Find image dimensions in a baseline or progressive JPEG, without Pillow."""
    data = path.read_bytes()
    if data[:2] != bytes.fromhex("ffd8"):
        return None
    i = 2
    while i + 3 < len(data):
        if data[i] != 0xff:
            return None
        while i < len(data) and data[i] == 0xff:
            i += 1
        if i >= len(data):
            return None
        marker = data[i]
        i += 1
        if marker in (0xd8, 0xd9) or 0xd0 <= marker <= 0xd7:
            continue
        if marker == 0xda:
            break  # Start of compressed image: all frame metadata is earlier.
        if i + 2 > len(data):
            return None
        length = struct.unpack(">H", data[i:i+2])[0]
        if length < 2 or i + length > len(data):
            return None
        if marker in (0xc0, 0xc1, 0xc2, 0xc3):
            if length < 7:
                return None
            height, width = struct.unpack(">HH", data[i+3:i+7])
            return (width, height)
        i += length
    return None

def check(group, paths):
    items = []
    for rel in paths:
        file = UI / rel
        present = file.is_file() and file.stat().st_size > 4096
        resolution = (png_size(file) if file.suffix.lower() == ".png"
                      else jpeg_size(file) if file.suffix.lower() in (".jpg", ".jpeg")
                      else None) if present else None
        enough = resolution is not None if file.suffix.lower() in (".jpg", ".jpeg", ".png") else present
        if present and rel.startswith("selection/"):
            enough = resolution is not None and resolution[0] >= 256 and resolution[1] >= 256
        if present and rel == "map_rider_16.png":
            enough = resolution == (1024, 1024)
        items.append({"resource": rel, "present": present,
                      "quality_gate": enough if present else False,
                      "pixels": resolution})
    return items

def main():
    audit = {group: check(group, paths) for group, paths in {**REQUIRED, **EXISTING}.items()}
    for group, items in audit.items():
        for item in items:
            status = "OK" if item["present"] and item["quality_gate"] else "MISSING/INVALID"
            print(f"{status:16} {group:14} {item['resource']}")
    report = ROOT / "build/art_readiness.json"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(audit, indent=2, ensure_ascii=False), encoding="utf-8")
    missing = [x["resource"] for group in REQUIRED for x in audit[group]
               if not x["present"] or not x["quality_gate"]]
    print(f"Missing production assets: {len(missing)}")
    for file in missing:
        print("  - " + file)
    # Compilation can pass even when the approved visual identity is absent.
    # Surface that distinction prominently without blocking Unity diagnostics.
    import os
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as out:
            out.write("### Original selection art readiness\n\n")
            if missing:
                out.write("**NOT READY** — missing approved art:\n\n")
                for item in missing:
                    out.write(f"- `{item}`\n")
            else:
                out.write("All seven original art resources are present.\n")
            out.write("\n")
    if missing and os.environ.get("GITHUB_ACTIONS"):
        print("::warning title=Approved art missing::" + ", ".join(missing))

if __name__ == "__main__":
    main()
