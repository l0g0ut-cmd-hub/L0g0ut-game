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

    # All three painted maps have a campaign node ON their bridge.
    # The nearest hub must be on the bridge, not on the opposite river bank:
    # otherwise the rider crosses the river, overshoots and doubles back.
    # Read the C# bounds rather than trusting a duplicate hardcoded test.
    bridge_bounds = re.findall(
        r"if \(region == (\d) && p\.x >= ([\d.]+)f && p\.x <= ([\d.]+)f &&"
        r"\s*p\.y >= ([\d.]+)f && p\.y <= ([\d.]+)f\) return 2;",
        src)
    assert len(bridge_bounds) == 3, "Expected one explicit bridge anchor per region"
    authored_bridge_nodes = [(0, .45, .66), (1, .57, .48), (2, .46, .60)]
    for region, x, y in authored_bridge_nodes:
        b = next((v for v in bridge_bounds if int(v[0]) == region), None)
        assert b is not None, "Missing bridge anchor in region %d" % region
        _, xmin, xmax, ymin, ymax = b
        # Include the full authored jitter +/- 0.012 around each checkpoint.
        assert float(xmin) <= x - .012 and float(xmax) >= x + .012, (
            "Bridge anchor does not cover randomized node X in region %d" % region)
        assert float(ymin) <= y - .012 and float(ymax) >= y + .012, (
            "Bridge anchor does not cover randomized node Y in region %d" % region)
        hubs = {}
        for name, hx, hy, shore in re.findall(
                r"(\w+)\s*=\s*g\.Add\(\s*([.\d]+)f?,\s*([.\d]+)f?,\s*(\d)\)",
                blocks[region]):
            if int(shore) == 2:
                hubs[name] = (float(hx), float(hy))
        assert len(hubs) == 4
        nearest = min((((hx-x)*810)**2 + ((hy-y)*540)**2)**.5
                      for hx, hy in hubs.values())
        assert nearest < 45, "Bridge checkpoint is too far from bridge road"
    print("Bridge checkpoint anchoring: 3/3 regions; procedural jitter covered")

    screen = read("Assets/_Project/Scripts/Game/MapScreen.cs")
    assert screen.count("TerrainRoads.Route(") >= 8, (
        "One of the route render/movement components was not unified")
    for required in ("DrawRoads()", "DrawJourneyFootprints()", "DrawRouteGlints()",
                     "DrawHero()", "HeroUV()", "moveDuration"):
        assert required in screen
    assert "MoveTime" not in screen, "Legacy fixed travel duration remains"
    # No second tap: every marker selects; only the sidebar button travels.
    assert 'if (can && sel == i) Go(i);' not in screen
    assert 'Btn(new Rect(x, 540f - 166f, w, 44f), "В ПУТЬ"' in screen
    assert 'interactive && travelReady, true' in screen
    # В ПУТЬ to a distant node = one tapped hop along the highlighted route
    # (user decision 2026-11-02); the goal stays selected, no autopilot.
    assert ': sel >= 0 && Campaign.CanGo(NextPreviewStep) ? NextPreviewStep : -1;' in screen
    assert 'if (sel != i && NextPreviewStep != i) return;' in screen
    assert 'if (goal >= 0 && goal != Campaign.At && goal < Campaign.Nodes.Count) SelectNode(goal);' in screen
    assert 'Campaign.MoveTo(i); // persist only after the rider arrives' in screen
    assert 'if (!interactive || i != hitNode || panDragging) continue;' in screen
    assert 'Go(next);' not in screen, "Unattended travel to a different node reintroduced"
    assert 'if (UiKit.Tex(heroArt) != null)' in screen
    assert 'heroArt = "UI/npc_"' not in screen
    assert "float artHeight = artSpace >= 94f" in screen
    heal_source = read("Assets/_Project/Scripts/Game/Juice.cs")
    assert "public void ClearBattleDecals()" in heal_source
    assert "if (age > d.life)" in heal_source
    duel_source = read("Assets/_Project/Scripts/Game/DuelManager.cs")
    assert "Juice.Instance.ClearBattleDecals();" in duel_source
    assert "Footprints.Instance.ClearMarks();" in duel_source
    assert 'if (parents[index] < 0) { sel = -1; return; }' in screen
    assert 'if (UiKit.Tex(path) == null) return;' not in screen
    menu = read("Assets/_Project/Scripts/Menu/MainMenuController.cs")
    assert 'newJourneyStep == 1 ? "UI/event_"' not in menu
    assert '"UI/npc_" + i' not in menu
    assert '"ИЛЛЮСТРАЦИЯ НЕ ЗАГРУЖЕНА"' in menu
    assert 'VisualIdentity.Portrait(artRect, art, null, true);' in menu
    visuals = read("Assets/_Project/Scripts/Game/VisualIdentity.cs")
    assert 'GUI.DrawTexture(imageRect, t, ScaleMode.ScaleToFit);' in visuals
    assert 'new Rect(0f, 1f - slice, 1f, slice)' not in visuals
    assert 'JourneyChoice.Portrait(JourneyChoice.Hero)' in screen
    assert 'VisualIdentity.Portrait(portrait, heroArt);' in screen
    assert 'UI/selection/characters_difficulties' not in screen
    assert 'if (texCache.TryGetValue(path, out t) && t != null) return t;' in read(
        "Assets/_Project/Scripts/Game/UiKit.cs")
    assert 'art = newJourneyStep == 1 ? "UI/event_" + i : "UI/npc_" + i;' not in menu
    assert "if (hub.shore != shore) continue;" in src
    # Cache must be invalidated when regenerated node coordinates change in
    # place. Checking only List reference/region silently reuses old roads.
    bind = src.split("public static void Bind(int region, List<MapNode> nodes)", 1)[1]
    bind = bind.split("public static Path Route(int from, int to)", 1)[0]
    for token in ("cachedNodePositions", "!ReferenceEquals(nodes, activeNodes)",
                  "cachedNodePositions.Length != nodes.Count",
                  "previous.x != nodes[i].x || previous.y != nodes[i].y",
                  "cachedNodePositions = new Vector2[nodes.Count]"):
        assert token in bind, "Missing route cache mutation guard: " + token
    assert re.search(r"if \(changed\)\s*\{\s*cache\.Clear\(\);", bind), (
        "Route cache not cleared when node coordinates change")
    # The primary travel button must be visible inside the 540px virtual map
    # and must not overlap the inventory/menu actions on narrow Android screens.
    actions = []
    for label in ("В ПУТЬ", "СНАРЯЖЕНИЕ", "В МЕНЮ"):
        match = re.search(
            r'Btn\(new Rect\(x, 540f - (\d+)f, w, (\d+)f\), "' + label,
            screen)
        assert match, "Map action missing or no longer pinned: " + label
        top, height = 540 - int(match.group(1)), int(match.group(2))
        assert 0 <= top and top + height <= 540, "Map action off-screen: " + label
        actions.append((top, top + height))
    assert all(a[1] < b[0] for a, b in zip(actions, actions[1:])), (
        "Map action hitboxes overlap")
    assert "lengthPixels * easedTravel" in screen
    assert "Direction(easedTravel)" in screen
    # Optional replacement art must not silently become a broken 16-frame rider.
    rider = ROOT / "Assets/_Project/Resources/UI/map_rider_16.png"
    if rider.is_file():
        check_png("Assets/_Project/Resources/UI/map_rider_16.png", 1024, 1024)
    assert 'riderAtlas.width >= 512' in screen
    assert 'riderAtlas.width % 4 == 0' in screen


def check_new_game():
    choice = read("Assets/_Project/Scripts/Game/JourneyChoice.cs")
    importer = read("tools/import_approved_selection_art.py")
    assert "from audit_missing_game_art import APPROVED_SHA256" in importer
    assert 'parser.add_argument("--dry-run"' in importer
    assert "Refusing to overwrite non-original file" in importer
    assert 'bytes.fromhex("89504e470d0a1a0a")' in importer
    apk = read(".github/workflows/build-android.yml")
    audit = read("tools/audit_missing_game_art.py")
    assert 'BLADEPATH_REQUIRE_APPROVED_ART: "1"' in apk
    assert 'if missing and os.environ.get("BLADEPATH_REQUIRE_APPROVED_ART") == "1":' in audit
    assert "APK release blocked: missing or SHA-mismatched original PNGs" in audit
    menu = read("Assets/_Project/Scripts/Menu/MainMenuController.cs")
    skins = read("Assets/_Project/Scripts/Visual/PlayerSkins.cs")
    builder = read("Assets/_Project/Scripts/Editor/HeroModelBuilder.cs")
    assert "JourneyChoice.BeginNew(chosenDifficulty, chosenHero)" in menu
    assert 'PlayerPrefs.SetInt("journey_hero"' in choice
    assert "EnemyAI.Difficulty =" in choice
    assert '"UI/selection/approved_"' in choice
    assert "Resources.Load<Texture2D>(approved)" in choice
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
    # Production art is mandatory: a 6 KB design thumbnail is not a portrait.
    for group in ("hero", "difficulty"):
        for i in range(3):
            file = ROOT / ("Assets/_Project/Resources/UI/selection/%s_%d.jpg" % (group, i))
            assert file.is_file(), "Missing illustrated selection card: " + str(file)
            assert file.stat().st_size > 30000, "Low-quality or empty card: " + str(file)
            with file.open("rb") as source:
                assert source.read(3) == bytes((255, 216, 255)), (
                    "Not a valid JPEG: " + str(file))
    check_png("Assets/_Project/Resources/UI/map_rider_16.png", 1024, 1024)
    print("Legacy selection JPGs present; six approved original PNGs checked separately by audit")
    print("16-frame rider atlas present (visual frame quality still requires APK review)")
    print("New Game choice: 3 difficulties and 3 player FBX options")


def check_selection_screens():
    """Full-screen painted selection screens (user-approved 2026-11-02).

    Frames, names and buttons are painted into the image; the menu only adds
    invisible tap zones and a gold glow on the chosen card. Do not replace
    this with redrawn UI cards again.
    """
    sel = "Assets/_Project/Resources/UI/selection/"
    for name in ("screen_difficulty.jpg", "screen_traveller.jpg"):
        f = ROOT / (sel + name)
        assert f.is_file() and f.stat().st_size > 200000, "Missing selection screen: " + name
        with f.open("rb") as src:
            assert src.read(3) == bytes((255, 216, 255)), "Not a JPEG: " + name
    check_png(sel + "card_glow.png", 320, 480)
    for i in range(3):
        check_png("Assets/_Project/Resources/UI/hero_face_%d.png" % i, 256, 256)
        assert (ROOT / ("Assets/_Project/Resources/UI/hero_face_sq_%d.jpg" % i)).is_file()
    menu = read("Assets/_Project/Scripts/Menu/MainMenuController.cs")
    assert '"UI/selection/screen_difficulty"' in menu and '"UI/selection/screen_traveller"' in menu
    assert "const float SelW = 1916f, SelH = 821f;" in menu
    assert '"UI/selection/card_glow"' in menu
    assert "DrawJourneySelectionLegacy();" in menu
    hud = read("Assets/_Project/Scripts/Game/DuelHud.cs")
    assert "pp = JourneyChoice.Face(JourneyChoice.Hero);" in hud
    for i in (1, 2):
        check_png("Assets/_Project/Resources/UI/map_rider_16_hero%d.png" % i, 1024, 1024)
    screen = read("Assets/_Project/Scripts/Game/MapScreen.cs")
    assert '"UI/map_rider_16_hero" + JourneyChoice.Hero' in screen
    check_png("Assets/_Project/Resources/UI/map_icon_herbalist.png", 256, 256)
    assert 'const string HerbalistIcon = "UI/map_icon_herbalist";' in screen
    camp = read("Assets/_Project/Scripts/Game/Campaign.cs")
    # Backtracking (user request 2026-11-02): back only to visited places;
    # shrine and rest stay one-time, merchant keeps its stock.
    assert "return D.nodes[to].done && D.nodes[to].next.Contains(from);" in camp
    assert "Adjacent(D.at, i)" in camp
    assert "n.Type == NodeType.Shrine || n.Type == NodeType.Rest))" in screen
    assert "kept.Key == at) stock = kept.Value;" in screen
    # Meshy models (2026-11-02): Akiko player, Oni and Shogun enemies.
    builder = read("Assets/_Project/Scripts/Editor/HeroModelBuilder.cs")
    for fbx, art in (("Akiko_Player", "Akiko"), ("Oni_Enemy", "Oni"), ("Shogun_Enemy", "Shogun")):
        f = ROOT / ("Assets/_Project/Models/%s.fbx" % fbx)
        assert f.is_file() and f.stat().st_size > 500000, "Missing rigged FBX: " + fbx
        a = ROOT / ("Assets/_Project/Art/%s/%s_albedo.jpg" % (art, art.lower()))
        assert a.is_file() and a.stat().st_size > 200000, "Missing albedo: " + str(a)
        assert '"Assets/_Project/Models/%s.fbx"' % fbx in builder
    assert "Exists(AkikoFbxPath) ? AkikoFbxPath : Samurai2FbxPath" in builder
    assert "models[i].SetActive(models[i] == chosen)" in read("Assets/_Project/Scripts/Visual/EnemySkins.cs")
    print("Selection screens: 2 painted screens, glow, 3 hero faces; 3 map riders; herbalist icon; backtracking; Meshy models")


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
    check_selection_screens()
    check_heal()
    print("Journey integration checks: OK")
