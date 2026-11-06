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
    """Roads follow the painted paths (tools/map_roads.json → gen_map_roads.py).

    Every place sits exactly on a road hub; every pair of neighbouring layers is
    connected; the far bank is reachable ONLY across the bridge hubs.
    """
    import json
    src = read("Assets/_Project/Scripts/Game/TerrainRoads.cs")
    camp = read("Assets/_Project/Scripts/Game/Campaign.cs")
    assert "public static class TerrainRoads" in src
    assert "public static Path Route(int from, int to)" in src
    assert "lengthPixels" in src and "public Vector2 Direction(float progress)" in src
    assert "// ROADS-GRAPH BEGIN" in src and "// ROADS-SPOTS BEGIN" in camp
    assert "UnityEngine.Random.Range(-0.012f, 0.012f)" not in camp, "Places must not be jittered off the roads"
    assert "MigrateSpots(d);" in camp
    data = json.loads((ROOT / "tools/map_roads.json").read_text(encoding="utf-8"))
    for r in "012":
        m = data[r]
        pos = {}
        for layer in m["spots"]:
            for n, x, y in layer:
                pos[n] = (x, y)
        pos.update({k: tuple(v) for k, v in m["points"].items()})
        adj = {}
        cnt = 0
        for a, mids, b in m["roads"]:
            prev = a
            for p in mids:
                cnt += 1
                k = "_%d" % cnt
                adj.setdefault(prev, set()).add(k); adj.setdefault(k, set()).add(prev)
                prev = k
            adj.setdefault(prev, set()).add(b); adj.setdefault(b, set()).add(prev)

        def reach(src_, blocked=()):
            seen, todo = {src_}, [src_]
            while todo:
                for nb in adj.get(todo.pop(), ()):
                    if nb not in seen and nb not in blocked:
                        seen.add(nb); todo.append(nb)
            return seen
        spots = m["spots"]
        for l in range(len(spots) - 1):
            for a in spots[l]:
                got = reach(a[0])
                for b in spots[l + 1]:
                    assert b[0] in got, "No road %s -> %s on map %s" % (a[0], b[0], r)
        start, boss = spots[0][0][0], spots[-1][0][0]
        assert boss not in reach(start, set(m["bridge"])), "Map %s: far bank reachable without the bridge" % r
        for n, x, y in (s_ for layer in spots for s_ in layer):
            assert ("new Vector2(%s" % (("%.3f" % x).rstrip("0").rstrip(".") + "f")) in camp, "Spot %s not generated" % n
        print("Road map", r, len(adj), "hubs; all layer pairs connected; bridge mandatory")

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
    assert 'if (goal >= 0 && goal != Campaign.At && goal < Campaign.Nodes.Count)' in screen
    assert 'Campaign.MoveTo(i, serviceOnTheWay); // persist only after the rider arrives' in screen
    assert 'if (!interactive || i != hitNode || panDragging) continue;' in screen
    # One В ПУТЬ tap rides the whole route but stops at every new encounter.
    assert 'if (modal == Modal.None && !Campaign.InFight && Campaign.CanGo(hop)) Go(hop);' in screen
    assert 'foreach (int next in nodes[at].next)' in screen, "Route preview must be forward-only"
    assert 'if (UiKit.Tex(heroArt) != null)' in screen
    assert 'heroArt = "UI/npc_"' not in screen
    assert "float artHeight = artSpace >= 94f" in screen
    heal_source = read("Assets/_Project/Scripts/Game/Juice.cs")
    assert "public void ClearBattleDecals()" in heal_source
    assert "if (age > d.life)" in heal_source
    duel_source = read("Assets/_Project/Scripts/Game/DuelManager.cs")
    assert "Juice.Instance.ClearBattleDecals();" in duel_source
    assert "Footprints.Instance.ClearMarks();" in duel_source
    assert 'if (parents[index] < 0) return;' in screen
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
    assert "static int Nearest(Graph graph, Vector2 pos, int region)" in src
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
    # BP-047: approved 5-November difficulty remake plus two-state fog icon.
    for name, minimum in (("screen_difficulty_redesign.jpg", 250000),
                          ("fog_toggle_off.jpg", 12000),
                          ("fog_toggle_on.jpg", 12000)):
        f = ROOT / (sel + name)
        assert f.is_file() and f.stat().st_size > minimum, "Missing redesigned UI art: " + name
        assert f.read_bytes()[:3] == bytes((255, 216, 255)), "Not JPEG: " + name
    assert '"UI/selection/screen_difficulty_redesign"' in menu and '"UI/selection/screen_traveller"' in menu
    # BP-053: source JPEGs remain archived, runtime uses transparent PNG
    # overlays of approved option #1, aligned on an unchanging plaque.
    assert '"UI/selection/fog_toggle_inner_on"' in menu and '"UI/selection/fog_toggle_inner_off"' in menu
    # black matte is removed from the screen art itself; no patch overlay
    assert 'difficulty_plaque_matte_fix' not in menu
    for name in ("fog_toggle_inner_off.png", "fog_toggle_inner_on.png"):
        art = ROOT / (sel + name)
        assert art.is_file() and art.read_bytes()[:8] == bytes((137,80,78,71,13,10,26,10)), name
    assert "DrawFogToggleArt(" in menu
    assert "const float DifficultyW = 1824f, DifficultyH = 862f;" in menu
    assert 'bool pressedOk = SelectionHitbox(ok, s, ox, oy);' in menu
    assert "const float SelW = 1916f, SelH = 821f;" in menu
    assert 'if (i == chosen) DrawDifficultySelectionGlow(r, s);' in menu
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
    for fbx, art in (("Akiko_Player", "Akiko"), ("Oni_Enemy", "Oni"), ("Shogun_Enemy", "Shogun"),
                     ("Fallen_Enemy", "Fallen"), ("Shadow_Enemy", "Shadow"),
                     ("Dzin_Player", "Dzin"), ("Ren_Player", "Ren")):
        f = ROOT / ("Assets/_Project/Models/%s.fbx" % fbx)
        assert f.is_file() and f.stat().st_size > 500000, "Missing rigged FBX: " + fbx
        a = ROOT / ("Assets/_Project/Art/%s/%s_albedo.jpg" % (art, art.lower()))
        assert a.is_file() and a.stat().st_size > 200000, "Missing albedo: " + str(a)
        assert '"Assets/_Project/Models/%s.fbx"' % fbx in builder
    assert "Exists(AkikoFbxPath) ? AkikoFbxPath : Samurai2FbxPath" in builder
    assert "Exists(DzinFbxPath) ? DzinFbxPath : RoninFbxPath" in builder
    assert "Exists(RenFbxPath) ? RenFbxPath : Hero3FbxPath" in builder
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
