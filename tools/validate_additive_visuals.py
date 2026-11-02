#!/usr/bin/env python3
"""Additive visual acceptance: original art stays intact, effects stay bounded.

This is an early cheap CI check, not a replacement for the Unity compiler
or comparisons of real screenshots on a device.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def source(path):
    f = ROOT / path
    if not f.is_file():
        raise AssertionError("Missing script: " + path)
    return f.read_text(encoding="utf-8")


def verify_art_baseline():
    manifest = json.loads(source("tools/visual_baseline.json"))
    entries = manifest["git_blob_sha1"]
    assert len(entries) >= 30, "Too few protected original art resources"
    for rel, sha in entries.items():
        path = ROOT / rel
        if not path.is_file():
            raise AssertionError("Existing painted art was removed: " + rel)
        contents = path.read_bytes()
        git_blob = b"blob " + str(len(contents)).encode("ascii") + b"\0" + contents
        found = hashlib.sha1(git_blob).hexdigest()
        if found != sha:
            raise AssertionError(
                "Existing painted art changed without reviewed baseline update: "
                + rel)
    print("Preserved %d original maps, UI pieces and VFX textures" % len(entries))


def verify_runtime_additions():
    m = source("Assets/_Project/Scripts/Game/MapScreen.cs")
    hud = source("Assets/_Project/Scripts/Game/DuelHud.cs")
    juice = source("Assets/_Project/Scripts/Game/Juice.cs")
    dust = source("Assets/_Project/Scripts/Visual/DustBurstPool.cs")
    prints = source("Assets/_Project/Scripts/Visual/Footprints.cs")
    snow = source("Assets/_Project/Scripts/Visual/SnowDrifts.cs")
    koi = source("Assets/_Project/Scripts/Visual/KoiPond.cs")
    pond = source("Assets/_Project/Scripts/Visual/WaterRipple.cs")

    checks = {
        "authored world panoramas retained":
            "Campaign.MapTexture(Campaign.Region)" in m,
        "authored node medallions retained":
            'UiKit.Tex("UI/map_icons")' in m,
        "existing footstep path retained":
            "DrawJourneyFootprints();" in m,
        "ambient details paint beneath roads and medallions":
            m.index("DrawAmbientLandmarkMotes();") <
            m.index("DrawRoads();") <
            m.index("DrawRouteGlints();") <
            m.index("DrawNodes("),
        "map ambient motes have low-graphics fallback":
            "void DrawAmbientLandmarkMotes()" in m and
            "DuelManager.Gfx != 1" in m,
        "existing ornate posture frame retained":
            'UiKit.Tex("UI/posture_frame")' in hud,
        "posture edge accents only":
            "if (!broken && k > 0.84f && DuelManager.Gfx == 1)" in hud,
        "old blood/snow particle palettes retained":
            "new Color(0.8f, 0.85f, 0.94f, 0.42f)" in dust and
            "new Color(0.38f, 0.34f, 0.3f, 0.3f)" in dust,
        "dust is bounded":
            "if (dustPuffs.Count >= 10) return;" in juice,
        "dust effect does not allocate per trigger":
            "DustBurstPool.Create(transform" in juice and
            "new GameObject(\"Dust\")" not in juice,
        "dust returns to pool":
            "ParticleSystemStopAction.Callback" in dust and
            "void OnParticleSystemStopped()" in dust,
        "original footstep shape preserved":
            "static float SoleDist(" in prints and
            "ownedSnowSole" in prints and "ownedBloodSole" in prints,
        "snow drift materials cleaned":
            "void OnDestroy()" in snow and
            "driftMaterial" in snow and "iceMaterial" in snow,
        "koi ripple and mist textures cleaned":
            "void OnDestroy()" in koi and "ringTexture" in koi and
            "mistMaterial" in koi and "ringMaterial" in koi,
        "koi fog scales with mobile budget":
            "MobileVfxBudget.ParticleScale" in koi,
        "artist's pond texture offset preserved":
            "off = mat.mainTextureOffset;" in pond,
    }
    for feature, ok in checks.items():
        if not ok:
            raise AssertionError("Additive VFX regression: " + feature)
        print("OK:", feature)


if __name__ == "__main__":
    verify_art_baseline()
    verify_runtime_additions()
    print("Additive visual checks: OK")
