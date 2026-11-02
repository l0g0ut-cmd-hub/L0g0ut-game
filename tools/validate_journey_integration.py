#!/usr/bin/env python3
"""Pre-Unity integration checks: roads, 3-part new game, hero rigs and heal FX."""
import os
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(rel):
    p = ROOT / rel
    assert p.is_file(), "Missing required source: " + rel
    return p.read_text(encoding="utf-8")


def check_png(rel, width, height):
    p = ROOT / rel
    assert p.is_file(), "Missing art atlas: " + rel
    with p.open("rb") as f:
        header = f.read(33)
    assert header[:8] == b"\x89PNG\r\n\x1a\n", "Invalid PNG header: " + rel
    assert header[12:16] == b"IHDR", "No IHDR: " + rel
    w, h = struct.unpack(">II", header[16:24])
    assert (w, h) == (width, height), (
        "Incorrect atlas geometry (%s): %dx%d, expected %dx%d" %
        (rel, w, h, width, height))
    assert p.stat().st_size > 4096, "Truncated atlas: " + rel
    print("PNG", rel, w, h, p.stat().st_size)


def check_routes():
    src = read("Assets/_Project/Scripts/Game/TerrainRoads.cs")
    assert "public static class TerrainRoads" in src
    assert "public static Path Route(int from, int to)" in src
    assert "lengthPixels" in src and "public Vector2 Direction(float progress)" in src
    blocks = [
        src.split("// FOREST OF SHADOWS:")[1].split("// ONI MOUNTAINS:")[0],
        src.split("// ONI MOUNTAINS:")[1].split("// SHOGUNATE:")[0],
        src.split("// SHOGUNATE:")[1].split("// Diagnostics")[0],
    ]
    for n, block in enumerate(blocks):
        hubs = {}
        for variable, x, y, shore in re.findall(
                r"(\w+)\s*=\s*g\.Add\(\s*([.\d]+)f?,\s*([.\d]+)f?,\s*(\d)\)", block):
            hubs[variable] = (float(x), float(y), int(shore))
        edges = re.findall(r"g\.Link\((\w+),(\w+)\)", block)
        # The code reuses variables across regions but every region defines all
        # bridge hub names (b0..b3), and every crossing must use the bridge chain.
        assert len(edges) >= 15, "Region %s has too few road segments" % n
        assert "b0" in hubs and "b3" in hubs
        for a, b in edges:
            assert a in hubs and b in hubs, "Road connects to missing hub in region %s" % n
            if hubs[a][2] != hubs[b][2]:
                assert (hubs[a][2] == 2 or hubs[b][2] == 2), (
                    "Illegal land-to-land river jump on region %s: %s/%s" % (n, a, b))
        adjacency = {key: set() for key in hubs}
        for a, b in edges:
            adjacency[a].add(b)
            adjacency[b].add(a)
        for a, b in zip(("b0", "b1", "b2"), ("b1", "b2", "b3")):
            assert b in adjacency[a], "Missing bridge span"
        assert any(hubs[x][2] == 0 for x in adjacency["b0"])
        assert any(hubs[x][2] == 1 for x in adjacency["b3"])
        start = next(name for name in hubs if hubs[name][2] == 0)
        reached, pending = {start}, [start]
        while pending:
            for neighbor in adjacency[pending.pop()]:
                if neighbor not in reached and hubs[neighbor][2] != 2:
                    reached.add(neighbor)
                    pending.append(neighbor)
        assert not any(hubs[name][2] == 1 for name in reached), (
            "Road bypasses mandatory bridge")
        print("Road graph", n, len(hubs), "hubs", len(edges), "edges; bridge mandatory")

    screen = read("Assets/_Project/Scripts/Game/MapScreen.cs")
    assert screen.count("TerrainRoads.Route(") >= 8, (
        "One of the route render/movement components was not unified")
    for required in ("DrawRoads()", "DrawJourneyFootprints()", "DrawRouteGlints()",
                     "DrawHero()", "HeroUV()", "moveDuration"):
        assert required in screen
    assert "MoveTime" not in screen, "Legacy fixed travel duration remains"
    # No second tap: every marker selects; only the sidebar button travels.
    assert 'if (can && sel == i) Go(i);' not in screen
    assert 'Btn(new Rect(x, y, w, 40f), "ДВИГАТЬСЯ"' in screen
    assert 'bool canMove = !rolling && Campaign.CanGo(nextStep);' in screen
    assert 'if (parents[index] < 0) { sel = -1; return; }' in screen
    assert 'if (UiKit.Tex(path) == null) return;' not in screen
    menu = read("Assets/_Project/Scripts/Menu/MainMenuController.cs")
    assert 'VisualIdentity.Portrait(artRect, art);' in menu
    assert 'JourneyChoice.Portrait(JourneyChoice.Hero)' in screen
    assert 'VisualIdentity.Portrait(portrait, heroArt);' in screen
    assert 'UI/selection/characters_difficulties' not in screen
    assert 'if (texCache.TryGetValue(path, out t) && t != null) return t;' in read(
        "Assets/_Project/Scripts/Game/UiKit.cs")
    assert 'art = newJourneyStep == 1 ? "UI/event_" + i : "UI/npc_" + i;' in menu
    assert "if (hub.shore != shore) continue;" in src
    assert "lengthPixels * easedTravel" in screen
    assert "Direction(easedTravel)" in screen


def check_new_game():
    choice = read("Assets/_Project/Scripts/Game/JourneyChoice.cs")
    menu = read("Assets/_Project/Scripts/Menu/MainMenuController.cs")
    skins = read("Assets/_Project/Scripts/Visual/PlayerSkins.cs")
    builder = read("Assets/_Project/Scripts/Editor/HeroModelBuilder.cs")
    assert "JourneyChoice.BeginNew(chosenDifficulty, chosenHero)" in menu
    assert 'PlayerPrefs.SetInt("journey_hero"' in choice
    assert "EnemyAI.Difficulty =" in choice
    assert "TryAttachVariants(" in builder
    assert "PlayerSkins selector =" in builder
    assert "void Start()" in skins, "Skin activation before serialized models ready"
    assert "models[i].SetActive(models[i] == selected)" in skins
    # The new journey screen must never wipe Continue saves just by opening.
    assert menu.index("JourneyChoice.BeginNew(") > menu.index("newJourneyStep == 1")
    art = ROOT / "Assets/_Project/Resources/UI/selection/characters_difficulties.jpg"
    assert art.is_file() and art.stat().st_size > 2048
    with art.open("rb") as f:
        assert f.read(2) == b"\xff\xd8", "Bad selection JPEG"
    print("New Game choice: 3 difficulties and 3 player FBX options")


def check_heal():
    heal = read("Assets/_Project/Scripts/Game/Juice.cs")
    ik = read("Assets/_Project/Scripts/Visual/HeroRigDriver.cs")
    assert 'FxSprite.Spawn("fx_heal_hd"' in heal
    assert "1.55f, 6, 4, 24, 24f, false, 1.03f" in heal
    assert "SipEase" in ik and "Mathf.SmoothDamp" in ik
    assert "gourd.localScale = Vector3.one;" in ik
    check_png("Assets/_Project/Resources/FX/fx_heal_hd.png", 672, 448)
    print("Healing animation: 24 frames in 6x4 atlas, seamless IK")


if __name__ == "__main__":
    check_routes()
    check_new_game()
    check_heal()
    print("Journey integration checks: OK")
