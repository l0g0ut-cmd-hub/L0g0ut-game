#!/usr/bin/env python3
"""Install EXACT user-approved BladePath selection PNGs from a local ZIP.

Usage:
  python3 tools/import_approved_selection_art.py --archive BladePath_Approved_Selection_Import.zip --dry-run
  python3 tools/import_approved_selection_art.py --archive BladePath_Approved_Selection_Import.zip

Never substitutes a JPEG/NPC/event illustration, rescales images, or overwrites
a different existing file. Validates ALL six PNGs before writing ANY of them.
"""
import argparse
import hashlib
import os
import struct
import tempfile
import zipfile
from pathlib import Path

from audit_missing_game_art import APPROVED_SHA256

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / "Assets/_Project/Resources/UI"


def verify(archive):
    pending = []
    with zipfile.ZipFile(archive) as z:
        for rel, expected in APPROVED_SHA256.items():
            name = "Assets/_Project/Resources/UI/" + rel
            try:
                content = z.read(name)
            except KeyError as exc:
                raise ValueError("Approved PNG absent from archive: " + name) from exc
            digest = hashlib.sha256(content).hexdigest()
            if digest != expected:
                raise ValueError("SHA-256 mismatch for " + name + ": " + digest)
            if content[:8] != b"\\x89PNG\\r\\n\\x1a\\n" or content[12:16] != b"IHDR":
                raise ValueError("Invalid PNG header: " + name)
            width, height = struct.unpack(">II", content[16:24])
            if width < 256 or height < 256:
                raise ValueError("Insufficient original resolution: " + name)
            dest = UI / rel
            if dest.exists():
                old_digest = hashlib.sha256(dest.read_bytes()).hexdigest()
                if old_digest != expected:
                    raise ValueError("Refusing to overwrite non-original file: " + str(dest))
                print("EXISTS", rel, width, height, digest)
            else:
                print("VERIFIED", rel, width, height, digest)
                pending.append((dest, content))
    return pending


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True, type=Path,
                        help="The existing approved-selection ZIP supplied by the user")
    parser.add_argument("--dry-run", action="store_true",
                        help="Validate all six original PNGs without modifying the project")
    args = parser.parse_args()
    pending = verify(args.archive)
    if args.dry_run:
        print("DRY RUN: all six exact originals verified; %d not installed" % len(pending))
        return
    for dest, content in pending:
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = None
        try:
            with tempfile.NamedTemporaryFile(dir=dest.parent, prefix=".approved_",
                                             suffix=".png", delete=False) as out:
                tmp = Path(out.name)
                out.write(content)
            os.replace(tmp, dest)
        finally:
            if tmp is not None and tmp.exists():
                tmp.unlink()
        print("IMPORTED", dest.relative_to(ROOT))
    print("Ready for Git commit: %d new approved PNGs" % len(pending))


if __name__ == "__main__":
    main()
