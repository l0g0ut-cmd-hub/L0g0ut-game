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
# The end-of-match overlay and its action buttons must use the SAME
# OnGUI painter, with buttons emitted after every darkening effect.
assert "public void DrawResultActionsFromHud()" in input_code
assert "PrototypeInput.Instance.DrawResultActionsFromHud();" in hud_code
assert hud_code.index("PrototypeInput.Instance.DrawResultActionsFromHud();") > hud_code.index("UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.55f * t));")
assert 'if (over) { DrawRectButton((int)B.OverAgain);' not in input_code
assert "DrawRectButton((int)B.OverAgain);" in input_code
assert "new Color(1f, 0.99f, 0.91f, 1f)" in input_code
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
# Intro round numerals must remain brush calligraphy but be light over the
# red ensō. The white atlas exists already: no source artwork is modified.
assert 'UiKit.BrushNumber(rc, roundNumber, 84f, rAl, TextAnchor.MiddleCenter, true)' in hud_code
assert 'if (!UiKit.BrushNumber(' in hud_code, "Missing white number font fallback"
ui_code = source("Assets/_Project/Scripts/Game/UiKit.cs")
assert 'Tex(bright ? "UI/brush_digits_white" : "UI/brush_digits")' in ui_code
assert "new Color(1f, 0.96f, 0.87f, alpha)" in ui_code
assert "alpha * 0.72f" in ui_code, "Brush numeral shadow must stay visible"
assert (ROOT / "Assets/_Project/Resources/UI/brush_digits_white.png").is_file()
# Round-intro journey/opponent caption must be fully backed, with symmetric
# padding for the longest title, not a narrow decorative brush strip.
assert "Rect captionPanel = new Rect(cx - 390f, 280f, 780f, captionH);" in hud_code
assert "float captionH = mk > 1 ? 118f : 94f;" in hud_code
assert "Rect captionTitle = new Rect(cx - 365f, 288f, 730f, 34f);" in hud_code
assert 'UiKit.Rect(captionPanel, new Color(0.025f, 0.018f, 0.02f, 0.72f * ta))' in hud_code
assert 'DrawBrush("UI/brush_stroke", new Vector2(cx, 318f)' not in hud_code
# BP-049: Difficulty background is neutral; there must be exactly one
# dynamically drawn frame, in the selected card's actual rectangle.
assert 'Texture2D glow = UiKit.Tex(difficulty' in menu
assert '"UI/selection/difficulty_active_frame" : "UI/selection/card_glow"' in menu
assert 'if (i == chosen && glow != null)' in menu
assert 'if (difficulty) chosenDifficulty = i;' in menu
assert 'DifficultyHaloNeutralizer' not in menu
assert 'GUI.DrawTexture(haloArea, neutralizer' not in menu
assert (ROOT / "Assets/_Project/Resources/UI/selection/screen_difficulty_redesign.jpg").is_file()
assert (ROOT / "Assets/_Project/Resources/UI/selection/difficulty_active_frame.png").is_file()

# BP-042: selection actions cannot draw opaque black rectangles outside
# carved gold corners. Existing approved background paintings stay untouched.
assert 'Texture2D board = UiKit.Tex("UI/Kit/ui_board");' not in menu
assert 'Rect cover = new Rect(r.x - 4f, r.y - 2f' not in menu
assert 'bool pressedBack = SelectionHitbox(back, s, ox, oy);' in menu
assert 'bool pressedOk = SelectionHitbox(ok, s, ox, oy);' in menu
assert 'return GUI.Button(r, GUIContent.none, GUIStyle.none);' in menu
# Main-menu side ornaments only occupy existing pillarbox space. The video,
# baked-in controls and hit zones are not shifted or scaled to add borders.
assert 'Rect vr = VideoRect();' in menu
assert 'GUI.DrawTexture(vr, tex, ScaleMode.StretchToFill, false);' in menu
assert 'DrawSideBorders(vr);' in menu
assert 'float left = Mathf.Max(0f, video.x);' in menu
assert 'float right = Mathf.Max(0f, Screen.width - video.xMax);' in menu
assert 'UiKit.Tex("UI/menu_side_panels")' in menu
assert 'UiKit.Tex("UI/menu_side_borders")' not in menu, "Do not restore blurry JPEG borders"
assert (ROOT / "Assets/_Project/Resources/UI/menu_side_panels.png").is_file()
assert 'float k = vr.width / CW;' in menu
assert 'VisualIdentity.KitButton(r, label, true, red)' not in menu
# Result buttons stay ahead of vignette and use intact art, rather than
# slicing the lacquer into distorted segments or adding mismatched overlays.
assert "GUI.depth = Paused || Over ? -100 : 0;" in input_code
assert "GUI.contentColor = Color.white;" in input_code
assert "GUI.contentColor = oldContentTint;" in input_code
assert "GUI.backgroundColor = oldBackgroundTint;" in input_code
assert "new Color(1f, 0.99f, 0.91f, 1f)" in input_code
assert "if (resultButton) GUI.DrawTexture(vr, wood, ScaleMode.StretchToFill, true);" in input_code
assert "if (resultButton)" in input_code
assert "new Color(1f, 0.84f, 0.58f, 0.14f)" not in input_code
# Both "ИДЕАЛЬНО" and a long round-won title fit a source-art red brush
# stretched ONLY through its center; short narrow brush backing is forbidden.
assert "bool redResultBanner = !duel.TimedOut && !duel.LastDraw && !brushWin;" in hud_code
assert "float bannerW = Mathf.Min(940f, W - 80f);" in hud_code
assert 'UiKit.Slice(banner, stroke, banner.height / stroke.height,' in hud_code
assert "sub.Length > 0 ? 145f : 127f" in hud_code
assert "880f, sub.Length > 0 ? 100f : 120f" in hud_code
assert 'DrawBrush("UI/brush_stroke_red", new Vector2(cx, 172f)' not in hud_code
assert "float spiritW = Mathf.Min(W - 80f, 850f);" in hud_code
assert "UiKit.Slice(spiritBanner, spiritStroke" in hud_code
print("Visual/haptic runtime source guards: OK")
