#!/usr/bin/env python3
"""Fast source-level guards for Android visual/haptic regressions.

This is NOT a C# compiler or an in-game graphics test. Unity compilation
remains the authoritative integration check.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def source(path):
    p = ROOT / path
    assert p.is_file(), f"Missing required visual script: {path}"
    return p.read_text(encoding="utf-8")

haptics = source("Assets/_Project/Scripts/Game/Haptics.cs")
input_code = source("Assets/_Project/Scripts/Input/PrototypeInput.cs")
menu = source("Assets/_Project/Scripts/Menu/MainMenuController.cs")
assert 'Enabled = false;' in haptics, "New installs must default to vibration off"
assert 'PlayerPrefs.GetInt("haptics", 0)' in input_code, "Gameplay must restore the stored vibration preference"
assert 'PlayerPrefs.GetInt("haptics", 0)' in menu, "Menu must show vibration as off for new players"
assert 'PlayerPrefs.Save();' in menu and 'PlayerPrefs.Save();' in input_code
assert 'if (Enabled) Handheld.Vibrate()' in haptics, "Long vibration must honor disabled setting"

flash_pool = source("Assets/_Project/Scripts/Visual/CinematicFlashPool.cs")
juice = source("Assets/_Project/Scripts/Game/Juice.cs")
assert "class CinematicFlashPool : MonoBehaviour" in flash_pool
assert "flashPool.Emit(pos, size, c, life, m, rotDeg, grow)" in juice
assert "ParticleSystemStopAction.None" in flash_pool
assert "ParticleSystemStopAction.Destroy" not in juice[juice.index("void Burst1("):juice.index("// искры: раскалённые", juice.index("void Burst1("))]

mist = source("Assets/_Project/Scripts/Game/JourneyMistArt.cs")
map_source = source("Assets/_Project/Scripts/Game/MapScreen.cs")
assert "JourneyMistArt.Texture" in map_source and "void DrawPaintedMist()" in map_source
assert "Texture2D Create()" in mist
assert "DrawLandmarkLights();" in map_source
assert "DrawJourneyFootprints();" in map_source, "Travel direction must stay visible"

trail = source("Assets/_Project/Scripts/Visual/WeaponTrail.cs")
post = source("Assets/_Project/Scripts/Visual/PostFX.cs")
assert "float taper" in trail and "MobileVfxBudget.Level" in trail
assert "float daylightBloom" in post and "float focusPower" in post
assert "MobileVfxBudget.AllowDepthOfField" in post
# Result-screen buttons must appear in front of the 55% darkening drawn
# by DuelHud, with a bright enabled label rather than a disabled-looking tint.
hud_code = source("Assets/_Project/Scripts/Game/DuelHud.cs")
assert "GUI.depth = 10;" in hud_code and "GUI.depth = previousHudDepth;" in hud_code
assert "GUI.depth = Paused || Over ? -100 : 0;" in input_code
assert "GUI.depth = previousInputDepth;" in input_code
assert "bool resultButton = b == (int)B.OverAgain" in input_code
assert "new Color(1f, 0.96f, 0.85f, 1f)" in input_code
# Red combat announcements share one HUD renderer. Their danger category
# still selects the red ornamental banner, but foreground text MUST be white
# (UiKit.Text supplies the existing dark shadow). Non-danger colors stay intact.
assert "bool danger = duel.AnnounceColor.r > 0.8f && duel.AnnounceColor.g < 0.55f;" in hud_code
assert 'UiKit.Tex(danger ? "UI/Kit/hud_banner_red" : "UI/Kit/hud_banner")' in hud_code
assert "Color ac = danger" in hud_code
assert "? new Color(1f, 1f, 1f, aa)" in hud_code, "Red banner must use white lettering"
assert ": new Color(duel.AnnounceColor.r, duel.AnnounceColor.g, duel.AnnounceColor.b, aa)" in hud_code
assert "duel.AnnounceText, afs * sc, ac);" in hud_code
assert "label.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a)" in source("Assets/_Project/Scripts/Game/UiKit.cs")
print("Visual/haptic runtime source guards: OK")
