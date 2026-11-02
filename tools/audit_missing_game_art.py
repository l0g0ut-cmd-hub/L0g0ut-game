#!/usr/bin/env python3
"""Audit art required by BladePath. Never pretend a fallback is finished art.

Run: python3 tools/audit_missing_game_art.py
The check is non-destructive and does not block the running Android build.
"""
from pathlib import Path
import json
import struct
import hashlib

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / "Assets/_Project/Resources/UI"
# SHA-256 hashes from the six user-approved PNGs in
# bladepath_selection_originals.zip (2026-11-02). JPGs in the repository
# are different compositions and MUST NOT count as the approved originals.
# Distinct approved_* basenames avoid Unity Resources JPG/PNG collisions.
APPROVED_SHA256 = {
    "selection/approved_hero_0.png": "76d4661eb36dcfde341c166e6060d18f672d17a44dd0e4f45ce936deeeb59811",
    "selection/approved_hero_1.png": "d7cb45486efc734ff87dfae9120bc326ff3c21eb354a978dd02823414d53b075",
    "selection/approved_hero_2.png": "0e5b04ec47eb8366dbc2d3b748ba8cf9c208dc3a980ed9d0635fbe40ea3c9c48",
    "selection/approved_difficulty_0.png": "171b5dc83e4bf7734a7cbb9df7f25c6efbbba0d1ff9b637c3ac702be0c77e161",
    "selection/approved_difficulty_1.png": "c18e8b4a3514484ee1d2ace20899dd0493c4bac9e3c95db99176b99e878e0c84",
    "selection/approved_difficulty_2.png": "099c9a25264350beefad6aa14e3b6dbc946fc33864cf9b0694badce1708ca207",
}
REQUIRED = {
    "approved_travellers": [f"selection/approved_hero_{i}.png" for i in range(3)],
    "approved_difficulties": [f"selection/approved_difficulty_{i}.png" for i in range(3)],
    "rider": ["map_rider_16.png"],
}
EXISTING = {
    "legacy_travellers": [f"selection/hero_{i}.jpg" for i in range(3)],
    "legacy_difficulties": [f"selection/difficulty_{i}.jpg" for i in range(3)],
    "maps": [f"map_{i}.jpg" for i in range(3)],
    "events": [f"event_{i}.jpg" for i in range(8)],
    "npc": [f"npc_{i}.jpg" for i in range(5)],
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
        expected_hash = APPROVED_SHA256.get(rel)
        actual_hash = hashlib.sha256(file.read_bytes()).hexdigest() if present and expected_hash else None
        if expected_hash is not None:
            enough = enough and actual_hash == expected_hash
        items.append({"resource": rel, "present": present,
                      "quality_gate": enough if present else False,
                      "pixels": resolution,
                      "approved_sha256": expected_hash,
                      "actual_sha256": actual_hash})
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
                out.write("All six SHA-256 verified original selection PNGs and rider atlas are present.\n")
            out.write("\n")
    if missing and os.environ.get("GITHUB_ACTIONS"):
        print("::warning title=Approved art missing::" + ", ".join(missing))
    # The Android release workflow explicitly opts into strict validation.
    # Avoid burning the Unity build slot on known-wrong substitute JPG art.
    if missing and os.environ.get("BLADEPATH_REQUIRE_APPROVED_ART") == "1":
        raise SystemExit("APK release blocked: missing or SHA-mismatched original PNGs: " +
                         ", ".join(missing))

if __name__ == "__main__":
    main()
