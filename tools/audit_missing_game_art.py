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
    "travellers": [f"selection/hero_{i}.png" for i in range(3)],
    "difficulties": [f"selection/difficulty_{i}.png" for i in range(3)],
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
    if len(header) < 24 or header[:8] != b"\\x89PNG\\r\\n\\x1a\\n":
        return None
    return struct.unpack(">II", header[16:24])

def check(group, paths):
    items = []
    for rel in paths:
        file = UI / rel
        present = file.is_file() and file.stat().st_size > 4096
        resolution = png_size(file) if present and file.suffix.lower() == ".png" else None
        enough = True
        if present and rel.startswith("selection/") and resolution:
            enough = resolution[0] >= 256 and resolution[1] >= 256
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

if __name__ == "__main__":
    main()
