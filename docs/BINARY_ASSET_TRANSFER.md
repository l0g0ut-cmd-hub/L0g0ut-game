# Binary assets from this chat to GitHub — verified transfer protocol

This addresses the 2026-11-02 failure where six approved PNG files were repeatedly reported as blocked although the connected GitHub API can create binary objects.

## Required sequence

1. Before assuming an asset cannot be uploaded, inspect available GitHub operations: `create_blob` (accepts `encoding: "base64"`), `create_tree`, `create_commit`, `update_ref`. Verify the original file exists locally and calculate its SHA-256 and **Git blob SHA-1** (the SHA-1 of `b"blob "+str(len(bytes)).encode()+b"\\0"+bytes`). Keep actual original bytes; do not redraw or recompress.
2. If the GitHub connector cannot read local files directly from its own sandbox, encode the original bytes as **single-line Base64 text** into a staging file. Pass the staging file through `files__manage_library` upload (to a temporary path) and `files__read` so `functions.exec` can read the whole Base64 string. Check `has_more=false` and minimum/expected byte length.
3. In **one functions.exec call** consume the returned text directly into `tools.mcp__GitHub__create_blob({repository_full_name,content:base64,encoding:"base64"})`. Never echo the huge Base64 in a user message, GitHub source code or CI log. Git blob creation by itself is NOT integration.
4. Verify the returned Git blob SHA-1 equals the locally calculated Git object SHA-1. Build a tree from the latest base tree (preserving all other files), create a commit with the latest branch head as parent and update the working branch using `expected_sha` (lease). Refuse to force-push if the branch changed.
5. **Read the committed tree back from GitHub**, confirm six expected paths, bytes and SHAs. Run `tools/audit_missing_game_art.py` with `BLADEPATH_REQUIRE_APPROVED_ART=1`, then preflight, then (only for completed grouped release) one Unity APK build. Inspect the screens on the device. Never claim `APK_VERIFIED` from blob upload alone.
6. Always update `docs/DEVELOPMENT_LOG.md` and this inventory after source changes. Avoid repeating any task whose artifacts already exist.

**Evidence for completed recovery:** `de6b96e2` created six Git-tracked `Assets/_Project/Resources/UI/selection/approved_{hero,difficulty}_{0,1,2}.png`. Local source bytes from `bladepath_selection_originals.zip` matched each staging PNG exactly and each Git blob SHA-1 matched independently. The project already prioritizes these paths in `JourneyChoice.cs`; do not change filenames to collide with legacy JPG basenames.

**Failure policy:** Don't spend another scheduled run merely writing "binary upload blocked". If a connector cannot accept a file directly, test a supported alternate transfer route; if that fails, record the exact step, error and reproducible evidence and continue another non-duplicate task. Do not loop on the same technique without new evidence.
