#!/usr/bin/env python3
"""Import only missing approved BladePath artwork from a ZIP.

Usage: python3 tools/import_missing_art.py /path/to/bladepath_missing_only.zip
Refuses overwriting assets and refuses ambiguous names or corrupt PNG data.
"""
from pathlib import Path
from zipfile import ZipFile
import argparse
import struct

ROOT = Path(__file__).resolve().parents[1]
PREFIX = "Assets/_Project/Resources/UI/"
WHITELIST = {PREFIX + "map_rider_16.png"}
WHITELIST |= {PREFIX + f"selection/hero_{i}.png" for i in range(3)}
WHITELIST |= {PREFIX + f"selection/difficulty_{i}.png" for i in range(3)}

def size(raw):
    if len(raw) < 24 or raw[:8] != bytes([137,80,78,71,13,10,26,10]):
        raise ValueError("Corrupt PNG signature")
    return struct.unpack(">II", raw[16:24])

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    loaded = 0
    skipped = 0
    with ZipFile(args.archive) as archive:
        files = {info.filename.replace("\\\\", "/").lstrip("./"): info
                 for info in archive.infolist() if not info.is_dir()}
        unexpected = [name for name in files if name.startswith(PREFIX) and name not in WHITELIST]
        if unexpected:
            raise SystemExit("Unexpected file names: " + ", ".join(unexpected))
        for name in sorted(WHITELIST):
            info = files.get(name)
            dest = ROOT / name
            if dest.is_file():
                print("EXISTS: " + name)
                skipped += 1
                continue
            if info is None:
                print("ABSENT FROM ZIP: " + name)
                continue
            raw = archive.read(info)
            w,h = size(raw)
            if name.endswith("map_rider_16.png") and (w,h) != (1024,1024):
                raise SystemExit(f"Invalid rider grid: {w}x{h}")
            if name != PREFIX + "map_rider_16.png" and (w < 256 or h < 256):
                raise SystemExit(f"Portrait too small: {name} ({w}x{h})")
            if not args.dry_run:
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(raw)
            print(("WOULD ADD: " if args.dry_run else "ADDED: ") + name)
            loaded += 1
    print(f"New assets: {loaded}; existing unchanged: {skipped}")

if __name__ == "__main__":
    main()
