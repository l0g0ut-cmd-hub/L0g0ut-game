#!/usr/bin/env python3
"""Cheap lifecycle regression gate before a slow Unity Android build.

The checks validate source contracts, not C# compilation or runtime rendering.
They protect against old round results and blood effects leaking into a new duel.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]

def source(rel):
    return (ROOT / rel).read_text(encoding="utf-8")

duel = source("Assets/_Project/Scripts/Game/DuelManager.cs")
juice = source("Assets/_Project/Scripts/Game/Juice.cs")
hud = source("Assets/_Project/Scripts/Game/DuelHud.cs")
map_screen = source("Assets/_Project/Scripts/Game/MapScreen.cs")

start = duel.index("public void ResetDuel()")
end = duel.index("void SetState(", start)
reset = duel[start:end]
assert "Juice.Instance.ClearBattleDecals();" in reset
assert "Footprints.Instance.ClearMarks();" in reset
for required in ("TimedOut = false;", "LastDraw = false;", "Perfect = false;"):
    assert re.search(r"^\s*" + re.escape(required) + r"\s*$", reset, re.MULTILINE), (
        "Round reset must execute, not comment out: " + required)

clear_start = juice.index("public void ClearBattleDecals()")
clear_end = juice.index("void Mist(", clear_start)
clear = juice[clear_start:clear_end]
assert "battleFxGeneration++;" in clear
assert "transientBloodFx.Clear();" in clear
assert "ParticleSystemStopBehavior.StopEmittingAndClear" in clear
assert "d.t.gameObject.SetActive(false);" in clear
assert "tracked[i].bloodSteps = 0;" in clear
assert 'TrackBloodFx(g);' in juice[juice.index('void Mist('):juice.index('void SlashFlash(')]
assert "while (generation == battleFxGeneration" in juice
assert "FountainDecals(neck, seconds, battleFxGeneration)" in juice

# User's previous build showed both a bad portrait fallback and an
# overflowing equipment button in a duel modal.
assert 'w * 0.38f, 48f), "СНАРЯЖЕНИЕ"' not in map_screen
assert 'Btn(new Rect(x, r.yMax - 64f, w, 44f), "СНАРЯЖЕНИЕ"' in map_screen
assert 'heroArt = "UI/npc_"' not in map_screen
# Combo counter redesigned (user request 2026-11-02): brush digits, but kept
# under the life bars and capped in height so it never hides the fight.
assert 'const float ComboMaxH = 66f;' in hud
assert 'DrawCombo(duel.player, new Vector2(70f, 121f), TextAnchor.MiddleLeft);' in hud
assert '"ударов подряд"' in hud
assert 'JourneyChoice.Names[JourneyChoice.Hero]' in hud
assert 'pp = JourneyChoice.Portrait(JourneyChoice.Hero)' in hud
assert 'new Rect(0.035f, 0.742f, 0.365f, 0.250f)' in hud
assert 'pp = UiKit.Tex("UI/portrait_hero")' not in hud
assert 'if (duel.State == DuelState.Intro) splats.Clear();' in hud
assert 'duel.Damaged -= OnDamaged;' in hud
print("Duel round reset, blood lifecycle, and HUD layout: OK")
