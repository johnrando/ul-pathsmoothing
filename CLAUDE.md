# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A compatibility patch for **PathSmoothing** (third-party mod for 7 Days To Die, by redbeardt,
vendored read-only under `reference/`) so it works correctly with the **Undead Legacy
Experimental** mod (sourced from Subquake's GitLab repos — see sibling `ul-updater`). Both mods
independently Harmony-patch the same vanilla method, and UL's own patch silently defeats one of
PathSmoothing's fixes — see "The bugs" below for the mechanism, established by decompiling both
DLLs (that decompile work, and UL's own custom-AI analysis in general, lives in the sibling
`ul-decomp` project — this project only owns the PathSmoothing side and the fix).

The patch is **additive**: a third mod, its own Harmony id, no modification or redistribution of
either mod's DLL. Everything about the other two mods is resolved by reflection at load time, so
this assembly loads and logs cleanly with either of them absent.

**Status: implemented, building, and deployed to `Mods/` — awaiting the first in-game run.**
`Mods/` now holds `0_TFP_Harmony`, `UndeadLegacy` (2.7.15), `PathSmoothing` (byte-identical to
`reference/`) and `PathSmoothingULCompat`. Every reflection target this patch looks up was verified
present in those *deployed* binaries, and the IL rewrite was verified statically against UL's actual
IL — but nothing has exercised a live load yet.

Versioning follows the user's scheme in `ModInfo.xml`, currently `0.0.0.1`; keep `AssemblyInfo.cs`
in step with it. On an Undead Legacy update, follow the re-verification checklist in `README.md`
before bumping `UndeadLegacyVersion.TestedAgainst`. **Author in `ModInfo.xml` is `John Rando` and no author or attribution of any kind
goes in the source files** — that is deliberate, don't "fix" it. Git commits are unaffected and
continue as normal against the Forgejo remote.

## The bugs

### 1. Prefix ordering makes PathSmoothing's smoothing inert (the visible one)

**This is the bug behind the reported symptom: zombies zig-zagging diagonally between voxel rows on
flat ground instead of running straight at the player.** It was missed on the first pass — the
earlier analysis asserted UL's prefix "subsequently reads" the fields PathSmoothing writes. It does
not; it reads them *first*.

PathSmoothing smooths by overwriting `EntityMoveHelper.moveToPos` in
`MainPatches.Patches__EntityMoveHelper__UpdateMoveHelper` (a void Prefix) — pointing the entity
straight at its target instead of at the next grid path node — and relies on the method body it
precedes to consume that value. UL's prefix *is* the body. Both are registered at default priority
(neither declares `[HarmonyPriority]`), and equal priority falls back to registration index
(`PatchInfoSerialization.PriorityComparer`: priority descending, then `index.CompareTo`). UL
registers ~1 second earlier — BepInEx chainloader (`Loading [Undead Legacy …]`) vs ModManager's
`InitMod` — so **UL's prefix runs first**.

Every prefix still runs: HarmonyX's `HarmonyManipulator.WritePrefixes` emits an unconditional
`call` per prefix and only consults the accumulated `__runOriginal` flag *after* all of them, to
decide whether to run the original body. So the void prefix executes — just too late:

1. `ASPPathNavigate.UpdateNavigation()` → `moveHelper.SetMoveTo(currentPath, …)` sets
   `moveToPos = currentPoint.AdjustedPositionForEntity(entity)` — the grid node. Every tick,
   immediately before `moveHelper.UpdateMoveHelper()` (`EntityAlive` ~5515-5517).
2. UL's prefix reads `moveToPos` — the grid node — moves the entity, returns false.
3. PathSmoothing's prefix writes `moveToPos` = straight at target.
4. Next tick, step 1 overwrites it before anything reads it.

The smoothing target is recomputed every tick and never once consumed.

**Fix** (`MoveHelperPrefixOrderFix.cs`): unpatch UL's prefix and re-register it at
`Priority.Last`, so it sorts after PathSmoothing's. Principled rather than a hack — a prefix that
replaces the method body belongs after the void prefixes written to run before that body.
PathSmoothing's own registration is left untouched, so `ps` enable/disable keeps working as
designed. Guarded: no-op if UL's prefix is already at `Priority.Last`, and the resulting order is
read back from `Harmony.GetPatchInfo` and surfaced by `psul`.

Side effect to know about: UL's prefix is then owned by our Harmony id. UL never unpatches
anything, so nothing breaks, but `Harmony.GetPatchInfo` will attribute it to us.

### 2. UL's digging patch defeats PathSmoothing's end-of-path fix

PathSmoothing's `FarBlockAttackFix` (`decompiled/PathSmoothing/PathSmoothing/FarBlockAttackFix.cs:23-34`)
is a Harmony **transpiler** on vanilla `EntityMoveHelper.UpdateMoveHelper` — it rewrites the
method's IL, replacing a `NodeCountRemaining() <= 1` check with a real distance calculation
(`Utils.GetPathLengthDistanceSq(PathEntity)`, `public static`, in
`decompiled/PathSmoothing/PathSmoothing/Utils.cs:16-29`). It exists because path smoothing changes
node spacing, so counting remaining path nodes stops being a reliable "am I near the end of my
path" proxy.

UndeadLegacy patches the *same vanilla method* with its own `H_ZombieDiggingPatch`
(in `UndeadLegacy.dll`, decompiled under `../ul-decomp/decompiled/UndeadLegacy/H_ZombieDiggingPatch.cs:8-428`
— see that project, not checked into this one) — a `Prefix` that reimplements zombie movement
(including digging) from scratch. Every exit path was checked: **14 `return false`, zero
`return true`** — it unconditionally skips the vanilla method body. Harmony transpilers only apply
to that vanilla body, so `FarBlockAttackFix`'s IL edit never executes once UndeadLegacy is loaded —
for *any* entity. UL's reimplementation carries its own untouched copy of the exact same bug, at
line 399: `path.NodeCountRemaining() <= 1`.

Both the vanilla site and UL's copy are the `IsUnreachableSideJump` gap-jump gate (vanilla
`EntityMoveHelper.UpdateMoveHelper` has exactly one `NodeCountRemaining` call; verified by
decompiling `Assembly-CSharp.dll`). "FarBlockAttack" is PathSmoothing's own naming for it.

PathSmoothing's *other* patch on this method (the void Prefix at
`decompiled/PathSmoothing/PathSmoothing/MainPatches.cs:58-83`) is defeated differently — by
ordering, not by the skipped body. See bug 1.

### 3. Bear doesn't get wall-avoidance during melee approach

`MainPatches.Patches__EntityAlive__FindPath` (`decompiled/PathSmoothing/PathSmoothing/MainPatches.cs:85-108`)
only suppresses smoothing near an obstruction when the entity's active AI task
`is EAIApproachAndAttackTarget` — the **vanilla** type, checked by hard `is`, not by
name/reflection. Only one entity in UL's `entityclasses.xml` swaps to UL's own unrelated
`EAIULM_ApproachAndAttackTarget` class: `animalBear`. Everything else uses the unmodified vanilla
task, so this gap is Bear-only.

### Checked, deliberately not fixed

`EAIULM_ApproachAndAttackTarget.Update()` contains its own `NodeCountRemaining() <= 2` — but so
does **vanilla** `EAIApproachAndAttackTarget.Update()`, at the same repath-trigger site, and
PathSmoothing chooses not to patch it. UL's copy is no worse than vanilla, so it is out of scope
here too. (Similarly `EAIULM_BreakBlock` never calls `NodeCountRemaining` at all — see
`ul-decomp`'s CLAUDE.md.) UL patches none of PathSmoothing's other targets (`EntityAlive.FindPath`,
`ASPPathFinder.OnPathFinished`, `EAIDestroyArea.Continue`, `RaycastModifier.ValidateLine`,
`EntityAlive.CopyPropertiesFromEntityClass`) — grepped, zero hits. Those two bugs are the whole set.

## The patch

`src/PathSmoothingULCompat/` — an `IModApi` mod (same shape as PathSmoothing itself), assembly and
mod name `PathSmoothingULCompat`, Harmony id `PathSmoothing.UndeadLegacyCompat`.

- `ModApi.cs` — `IModApi` entry point; hands off to `Compat.Apply()`.
- `UndeadLegacyVersion.cs` — **advisory only, deliberately not a gate.** Warns when UL is not
  `TestedAgainst` (2.7.15) and carries on; UL's branch is numbered 2.7.x and takes new patch numbers
  routinely, so gating would break the mod on ordinary updates. An unreadable version warns the same
  way. Reads UL's `[BepInPlugin]` attribute via `CustomAttributeData` (so no BepInEx reference is
  needed), falling back to the `pluginVersion` literal via `GetRawConstantValue`. **Do not** use UL's
  assembly version (hardcoded `1.0.0.0`) or its `ModInfo.xml` (read `2.7.01` on a `2.7.15` install).
  Versions normalise to exactly three components, so `2.7.15.0` matches `2.7.15`, and a branch label
  like `2.7.x` reads as `2.7.0` rather than failing.
- `Compat.cs` — orchestration. Gates each fix on its own prerequisites and logs, under the
  `[PathSmoothing/UL] ` prefix, exactly which behaviour is missing when one doesn't apply. Also
  holds `SmoothingActive`.
- `Refs.cs` — every cross-mod lookup. Nothing from either mod is referenced at compile time.
- `MoveHelperPrefixOrderFix.cs` — bug 1, and the one that matters most. Demotes UL's
  `UpdateMoveHelper` prefix to `Priority.Last` so PathSmoothing's smoothing prefix runs before it.
- `EndOfPathCheckFix.cs` — bug 2. Transpiles UL's nested
  `H_ZombieDiggingPatch+EntityMoveHelper_UpdateMoveHelper.Prefix`, rewriting `callvirt int32
  PathEntity::NodeCountRemaining()` into `call float32 EndOfPathDistanceSq(PathEntity)` and the
  following `ldc.i4.1` into `ldc.r4 1.0`. The trailing `cgt`/`ceq` pair is valid on float operands
  unchanged, which is why only two instructions move. Instructions are **mutated in place**, not
  replaced, so labels and exception-block boundaries survive. `Compat` reads back `PatchedSites`
  after `harmony.Patch` and unpatches + logs an error if the rewrite found nothing.
- `UlApproachAndAttackSmoothingFix.cs` — bug 3. A second `void` Prefix on `EntityAlive.FindPath`
  running PathSmoothing's obstruction check (same layer mask `1082195968`) for UL's task type,
  feeding the same `PathSmoothing.Common.DontSmoothEntities`. Self-disables after a first failure
  so a fault can't flood the log.
- `SmoothingToggleTracker.cs` — postfixes on `PathSmoothing.Common.Enable`/`Disable` so the `ps`
  console command switches these patches too. These live under a different Harmony id, so
  `ps 0` (`Harmony.UnpatchID("PathSmoothing")`) would otherwise leave them running over unsmoothed
  paths. That is why the transpiler calls a local shim rather than `Utils.GetPathLengthDistanceSq`
  directly: when smoothing is off the shim hands back the original node-count semantics
  (`<= 1` gives `0f`, else `2f`, against the `1f` threshold).
- `Counters.cs` + `ConsoleCmdPathSmoothingUl.cs` — the `psul` console command (ours; PathSmoothing's
  `ps` is untouched and not shadowed). Reports each fix's install status plus live hit counts.
  `Counters.EndOfPathChecks` exists specifically because the load-time log can only prove the IL
  *matched*; a non-zero count is the only proof that the rewritten call site is actually **reached**,
  which is the residual risk when the patched method is itself another mod's Harmony prefix. It also
  prints the live `UpdateMoveHelper` prefix call order, which is the direct evidence for bug 1.

### What the end-of-path check actually gates

Worth knowing before judging in-game behaviour: the site is `IsUnreachableSideJump`. When a path
stops short of the target across a jumpable gap, `CalcIfUnreachablePos` sets that flag, and
`UpdateMoveHelper` then takes a full `jumpMaxDistance` leap **only if at the end of its path**. Read
off a smoothed path's sparse node list, "end of path" goes true too early, so the zombie launches
short. It also feeds `EAIDestroyArea` via `IsDestroyAreaTryUnreachable`/`UnreachablePercent`, so
zombies that should switch to breaking blocks keep jumping instead — which is where PathSmoothing's
"FarBlockAttackFix" name comes from.

### Building

`dotnet build src/PathSmoothingULCompat/PathSmoothingULCompat.csproj -c Release` — stages a
ready-to-copy mod folder at `dist/PathSmoothingULCompat/`. No NuGet packages, no .NET Framework
targeting pack, no VS: `FrameworkPathOverride` points the `net481` build at the game's own
`7DaysToDie_Data\Managed\` (which ships `mscorlib.dll`, `System*.dll` and `netstandard.dll`).
`GameRoot` defaults to three levels up from the csproj and can be overridden on the command line.

### Runtime facts this design relies on (all verified, re-check before changing)

- **Load order is a non-issue.** `ModManager.loadModsFromFolder` loads *every* mod assembly before
  `LoadMods` iterates `loadedMods.list` calling `InitModCode()`, so all mod assemblies (including
  PathSmoothing's) are in the AppDomain by the time any `InitMod` runs. UL is earlier still: it is a
  `BaseUnityPlugin` found inside `Mods/` by `BepInEx.MultiFolderLoader` and Awoken by the BepInEx
  chainloader, so `H_UndeadLegacy.Awake` calling `harmony.PatchAll()` has already run. No `zz_`
  folder prefix needed.
- **Patching a Harmony patch method works** — UL's Prefix is called by `call` from the generated
  `UpdateMoveHelper` wrapper, and a detour on it is honoured. Nothing else patches that Prefix, so
  there is no detour-ownership conflict.
- **Prefix ordering is index-based when priorities tie**, and every prefix runs regardless of any
  earlier `return false`. Both facts were read out of the deployed `BepInEx/core/0Harmony.dll`
  (`HarmonyManipulator.WritePrefixes`, `PatchInfoSerialization.PriorityComparer`) — don't reason
  about them from upstream Harmony docs, HarmonyX differs.
- **There are two 0Harmony assemblies** — `BepInEx/core/0Harmony.dll` (2.9.0, what UL binds to) and
  `Mods/0_TFP_Harmony/0Harmony.dll` (2.13.0). Both are unsigned, so Mono resolves `0Harmony` by
  simple name and version is ignored; PathSmoothing references 2.10.0.0 and works fine in this
  install, which is the proof. This project references BepInEx's.
- **The shipped `Assembly-CSharp.dll` is publicized** (members carry `[PublicizedFrom]`).
  `EAIManager.tasks` is still reached via `AccessTools.FieldRefAccess` rather than a direct `ldfld`,
  matching how PathSmoothing treats it, so an un-publicized future build fails at resolve time with
  a log line instead of at runtime with a `FieldAccessException`.

## Where things live

- **`src/PathSmoothingULCompat/`** — this project's own code (above). The only thing here that is
  actually ours.
- **`reference/PathSmoothing/`** — the mod being patched, read-only, vendored copy: `PathSmoothing.dll`
  (v1.8.11.0), `ModInfo.xml`, `Config/worldglobal.xml` (its two tunables,
  `PathSmoothing_StopRange`/`PathSmoothing_MotionOffset`, reloadable in-game via `ps reload`).
- **`decompiled/PathSmoothing/`** — `ilspycmd -p --nested-directories` output of the DLL above,
  11 `.cs` files, resolved against `7DaysToDie_Data\Managed\` + `BepInEx\core\` as reference paths.
  Gitignored — regenerate rather than expect it checked into git; not this project's own code, just
  decompiled third-party output kept locally for reference while writing the patch.
- **`dist/`** — build output staging, gitignored.
- **UL's own decompiled source** (`H_ZombieDiggingPatch.cs`, `EAIULM_ApproachAndAttackTarget.cs`,
  `entityclasses.xml`, etc.) lives in the sibling `ul-decomp` project, not here — see its
  `CLAUDE.md` "Findings" section for the analysis this project's fix is based on.

## Related sibling projects (same `GameRoot`, separate git repos, don't assume any code sharing)

- **`ul-decomp`** — owns the UndeadLegacy-side decompile and the root-cause analysis this project's
  fix is built on. Re-read its `CLAUDE.md` "Findings" section before changing the fix design.
- **`ul-updater`** — deploys/verifies the mod install this project's `reference/` copy and the live
  `GameRoot` install both track. See `../ul-updater/CLAUDE.md`.

None of these sibling folders' `.git`/`.claude` contents should be modified from here.
