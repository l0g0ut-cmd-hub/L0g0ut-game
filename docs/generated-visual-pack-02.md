# Visual Pack 02 — animated sprite atlases and map events

This update introduces shared VFX meshes, optional painted 6-frame katana slashes, and map illustrations.

## Binary assets
The generated ZIP is supplied in the ChatGPT conversation as `BladePath_Visual_Pack_02.zip`. The connected GitHub tool only writes UTF-8 files, not binary PNG/JPG. Therefore **the new artwork is not in the repository or APK yet**. Extract `Assets/Resources/` from the ZIP into the Unity project's `Assets/Resources/`, then commit the images.

- `FX/slash_red.png`: 6x1, 6 frames, 768x128, black background, additive blending. Automatically used for hit flashes on high graphics.
- Other 6x1 FX atlases: `sparks_gold`, `blood_spray`, `smoke`, `petals`, `sigil_red`, `spirit_wolf`, `flame_red`.
- `UI/pack02/*.jpg`: eight event/location cards; event screen picks `wounded_ronin`, `shrine`, `camp`, `ruins` if present, otherwise falls back to pack01 and existing game art.
- `UI/pack02/node_*.png`: five additional map node concepts; not wired into the existing icon atlas yet.

## Engine
`FxSprite` now shares immutable per-frame meshes across instances with the same atlas dimensions, clamps invalid frame counts and caches the main camera. Existing VFX textures and call sites remain supported.

## QA
Build in Unity, inspect event cards for readability, compare low/high quality, ensure 6-frame atlas UVs are correct, and test many concurrent FX for memory/performance. Composite-sheet frames are art concepts rather than mocap or rigged animations; final sprites may need cleanup.
