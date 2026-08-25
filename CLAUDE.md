# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A compatibility patch for **PathSmoothing** (third-party BepInEx mod for 7 Days To Die, by
redbeardt, vendored read-only under `reference/`) so it works correctly with the **Undead Legacy
Experimental** mod (sourced from Subquake's GitLab repos — see sibling `ul-updater`). Both mods
independently Harmony-patch the same vanilla method, and UL's own patch silently defeats one of
PathSmoothing's fixes — see "The bug to fix" below for the full mechanism, established by
decompiling both DLLs (that decompile work, and UL's own custom-AI analysis in general, lives in
the sibling `ul-decomp` project — this project only owns the PathSmoothing side and the fix).

**Status: root cause fully diagnosed via decompilation; no patch code written yet.**

## The bug to fix

PathSmoothing's `FarBlockAttackFix` (`decompiled/PathSmoothing/PathSmoothing/FarBlockAttackFix.cs:23-34`)
is a Harmony **transpiler** on vanilla `EntityMoveHelper.UpdateMoveHelper` — it rewrites the
method's IL, replacing a `NodeCountRemaining() <= 1` check with a real distance calculation
(`Utils.GetPathLengthDistanceSq(PathEntity)`, `public static`, in
`decompiled/PathSmoothing/PathSmoothing/Utils.cs:16-29`). It exists because path smoothing changes
node spacing, so counting remaining path nodes stops being a reliable "am I near the end of my
path" proxy — without the fix, entities attack blocks from too far away.

UndeadLegacy patches the *same vanilla method* with its own `H_ZombieDiggingPatch`
(in `UndeadLegacy.dll`, decompiled under `../ul-decomp/decompiled/UndeadLegacy/H_ZombieDiggingPatch.cs:8-428`
— see that project, not checked into this one) — a `Prefix` that reimplements zombie movement
(including digging) from scratch. Every exit path was checked: **14 `return false`, zero
`return true`** — it unconditionally skips the vanilla method body. Harmony transpilers only apply
to that vanilla body, so `FarBlockAttackFix`'s IL edit never executes once UndeadLegacy is loaded —
for *any* entity. UL's reimplementation carries its own untouched copy of the exact same bug, at
line 399: `path.NodeCountRemaining() <= 1`.

PathSmoothing's *other* patch on this method (`MainPatches.Patches__EntityMoveHelper__UpdateMoveHelper`,
`decompiled/PathSmoothing/PathSmoothing/MainPatches.cs:58-83`, a `void` Prefix doing the stop-range/
motion-offset smoothing) is **not** affected — a `void` Prefix always runs regardless of other
prefixes' skip-original decisions, and it works by writing `moveToPos` via `SetMoveTo`/`Stop()`,
fields UL's reimplementation subsequently reads. Only the block-attack distance fix is defeated,
not smoothing itself.

**The fix**: same transpiler technique `FarBlockAttackFix` already uses, re-targeted at UL's
`H_ZombieDiggingPatch`'s nested Prefix method instead of the vanilla original — replace its
`NodeCountRemaining() <= 1` with a call to `PathSmoothing.Utils.GetPathLengthDistanceSq(path) <=
1f`. This is additive (a third Harmony patch on top of both mods' existing ones), not a
modification of either mod's own DLL.

### Secondary, narrower bug (optional to also fix)

`MainPatches.Patches__EntityAlive__FindPath` (`decompiled/PathSmoothing/PathSmoothing/MainPatches.cs:85-108`)
only suppresses smoothing near an obstruction when the entity's active AI task
`is EAIApproachAndAttackTarget` — the **vanilla** type, checked by hard `is`, not by name/reflection.
Only one entity in UL's `entityclasses.xml` swaps to UL's own unrelated
`EAIULM_ApproachAndAttackTarget` class: `animalBear`. Everything else uses the unmodified vanilla
task, so this gap is currently Bear-only — lower priority than the block-attack fix above.

Fix, if pursued: an added case recognizing `EAIULM_ApproachAndAttackTarget` too, feeding the same
`PathSmoothing.Common.DontSmoothEntities` (`public static`, `decompiled/PathSmoothing/PathSmoothing/Common.cs:18`).

## Where things live

- **`reference/PathSmoothing/`** — the mod being patched, read-only, vendored copy: `PathSmoothing.dll`
  (v1.8.11.0), `ModInfo.xml`, `Config/worldglobal.xml` (its two tunables,
  `PathSmoothing_StopRange`/`PathSmoothing_MotionOffset`, reloadable in-game via `ps reload`).
- **`decompiled/PathSmoothing/`** — `ilspycmd -p --nested-directories` output of the DLL above,
  11 `.cs` files, resolved against `7DaysToDie_Data\Managed\` + `BepInEx\core\` as reference paths.
  Gitignored (see below) — regenerate rather than expect it checked into git; not this project's
  own code, just decompiled third-party output kept locally for reference while writing the patch.
- **UL's own decompiled source** (`H_ZombieDiggingPatch.cs`, `EAIULM_ApproachAndAttackTarget.cs`,
  `entityclasses.xml`, etc.) lives in the sibling `ul-decomp` project, not here — see its
  `CLAUDE.md` "Findings" section for the full analysis this project's fix is based on.

## Related sibling projects (same `GameRoot`, separate git repos, don't assume any code sharing)

- **`ul-decomp`** — owns the UndeadLegacy-side decompile and the root-cause analysis this project's
  fix is built on. Re-read its `CLAUDE.md` "Findings" section before changing the fix design.
- **`ul-updater`** — deploys/verifies the mod install this project's `reference/` copy and the live
  `GameRoot` install both track. See `../ul-updater/CLAUDE.md`.

None of these sibling folders' `.git`/`.claude` contents should be modified from here.
