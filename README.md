# PathSmoothing — Undead Legacy compatibility patch

A small 7 Days To Die mod that makes [PathSmoothing](https://www.nexusmods.com/users/102102433)
(by redbeardt) work correctly when **Undead Legacy Experimental** is installed.

It is additive: it does not modify, replace or redistribute either mod's DLL. Install all three and
the patch wires itself in at load time. With either of the other two missing it logs a line and
does nothing.

## What it fixes

**1. Zombies zig-zagging along the grid path (the main one).**
PathSmoothing smooths movement by overwriting `EntityMoveHelper.moveToPos` in a void Harmony prefix
— pointing an entity straight at its target instead of at the next grid path node — and relying on
the method body it precedes to consume that value. Undead Legacy's prefix *is* that body: it
reimplements the method and returns false.

Both prefixes register at default priority, and Harmony breaks that tie by registration order. UL
registers about a second earlier (BepInEx chainloader vs ModManager's `InitMod`), so UL runs first.
Every prefix still runs — HarmonyX calls them all and only checks the "run original" flag
afterwards — but the order makes PathSmoothing's write useless:

1. `ASPPathNavigate.UpdateNavigation()` sets `moveToPos` to the next grid path node. Every tick,
   immediately before `UpdateMoveHelper`.
2. UL's prefix reads `moveToPos` — the grid node — moves the entity, returns false.
3. PathSmoothing's prefix writes `moveToPos` = straight at the target.
4. Next tick, step 1 overwrites it before anything reads it.

The smoothing target is recomputed every tick and never once consumed, so entities follow the raw
grid path — visibly stepping diagonally between voxel rows and wobbling side to side on open ground.

This patch demotes UL's prefix to `Priority.Last` so it sorts after PathSmoothing's, restoring the
contract PathSmoothing was written against: a prefix that replaces the method body belongs after the
void prefixes meant to run before that body. PathSmoothing's own registration is left untouched.

**2. End-of-path check.**
PathSmoothing transpiles vanilla `EntityMoveHelper.UpdateMoveHelper`, swapping its
`path.NodeCountRemaining() <= 1` end-of-path test for a real path-length measurement. Smoothing
changes node spacing, so counting nodes stops being a usable proxy for "am I nearly there".

Undead Legacy prefixes the *same* vanilla method with `H_ZombieDiggingPatch`, a from-scratch
reimplementation of zombie movement that returns `false` on all 14 of its exit paths. Harmony
transpilers only ever rewrite the vanilla body, which now never runs — so PathSmoothing's fix is
silently dead for every entity, and UL's reimplementation carries its own untouched copy of the
original check.

This patch transpiles UL's prefix the same way PathSmoothing transpiles the vanilla method, putting
the fix back on the code path that actually executes.

The check gates one specific decision: when a zombie's path stops short of its target across a
jumpable gap (`IsUnreachableSideJump`), it may take a full `jumpMaxDistance` leap — but only once it
has reached the end of its path. Read through a smoothed path's sparse node list, "end of path"
goes true while the zombie is still well short of the ledge, so it launches early and falls short.
That also feeds `EAIDestroyArea` via `IsDestroyAreaTryUnreachable`/`UnreachablePercent`, so a
zombie that should give up jumping and start breaking blocks keeps flinging itself instead.

**3. Bear wall-avoidance.**
PathSmoothing suppresses smoothing when something solid sits between an attacker and its target,
but only for entities running the vanilla `EAIApproachAndAttackTarget` task, tested with a hard
`is`. UL retargets exactly one entity — `animalBear` — to its own `EAIULM_ApproachAndAttackTarget`,
an unrelated class, so the Bear loses that wall avoidance. This patch runs the same obstruction
check for UL's task type and feeds the same `DontSmoothEntities` set.

Both fixes follow PathSmoothing's own `ps` / `pathsmoothing` console toggle, so `ps 0` returns the
original behaviour just as it does for PathSmoothing's own patches.

## Undead Legacy version support

**Tested against Undead Legacy 2.7.15.**

The version check is advisory only — nothing is gated on it. UL's current branch is numbered 2.7.x
and takes new patch numbers routinely, so refusing to install on an unrecognised number would break
the mod on ordinary updates. On anything other than the tested version the mod logs a warning and
carries on:

```
[PathSmoothing/UL] Undead Legacy 2.7.17 detected (from [BepInPlugin] attribute), but this patch has
only been tested under Undead Legacy 2.7.15. It will still install, and each fix logs an error if
the code it targets no longer matches - but re-verify movement behaviour, and check 'psul' if
zombies look wrong.
```

On 2.7.15 exactly it just logs an ordinary info line. An unreadable version warns the same way as a
mismatch.

The real safety net is structural rather than version-based: each fix checks the code it targets and
logs an error naming the lost behaviour if it no longer matches. The transpiler in particular
matches an exact IL pattern and removes itself when it does not find it. What that cannot catch is a
*semantic* change — same code shape, different meaning — which is what the warning is for.

The version is read from UL's `[BepInPlugin]` attribute, falling back to its `pluginVersion`
constant. Its assembly version is hardcoded `1.0.0.0` and its `ModInfo.xml` lags reality (it read
`2.7.01` on a `2.7.15` install), so neither of those is used.

### Re-verifying after an Undead Legacy update

1. Re-decompile `UndeadLegacy.dll` and diff `H_ZombieDiggingPatch` — specifically its
   `EntityMoveHelper_UpdateMoveHelper.Prefix`, and whether it still returns `false` everywhere.
2. Confirm the `NodeCountRemaining() <= 1` check is still there and still the
   `IsUnreachableSideJump` gate.
3. Check whether `EAIULM_ApproachAndAttackTarget` still exists and is still only used by
   `animalBear` in `entityclasses.xml`.
4. Launch, run `psul`, confirm all four fixes report applied and the prefix order is right.
5. Bump `TestedAgainst` in `src/PathSmoothingULCompat/UndeadLegacyVersion.cs` to silence the
   warning, then bump `ModInfo.xml` / `AssemblyInfo.cs`.

## Installing

Requires PathSmoothing and Undead Legacy to already be installed. Copy the built mod folder into
the game's `Mods/`:

```
Mods/PathSmoothingULCompat/
├── ModInfo.xml
└── PathSmoothingULCompat.dll
```

Load order does not matter: ModManager loads every mod assembly before it calls any `InitMod`, and
UL's Harmony patches are applied earlier still (it loads as a BepInEx plugin).

## Verifying it works

### 1. Load time — did the patches install?

Check the log for lines prefixed `[PathSmoothing/UL]`, in either
`%APPDATA%\7DaysToDie\logs\output_log_client__*.txt` or `BepInEx\LogOutput.log`:

```
[PathSmoothing/UL] Prefix-order fix applied: Undead Legacy's UpdateMoveHelper prefix now runs after PathSmoothing's, ...
[PathSmoothing/UL] End-of-path fix applied to Undead Legacy's UpdateMoveHelper prefix (1 check(s) rewritten).
[PathSmoothing/UL] Approach-and-attack fix applied: EAIULM_ApproachAndAttackTarget now suppresses smoothing near obstructions.
```

"1 check(s) rewritten" means the IL pattern was found and rewritten in the deployed
`UndeadLegacy.dll`. Anything at error level means a fix did **not** apply and says which behaviour
is therefore missing — the expected outcome if a future UL or PathSmoothing release moves the code
being targeted.

### 2. Runtime — is the rewritten code actually running?

The log only proves the rewrite matched. To prove the rewritten call site is being *reached*, use
the `psul` console command (F1):

```
psul
```

```
PathSmoothing/UL compatibility patch
  Undead Legacy version  : 2.7.15 - tested (read from [BepInPlugin] attribute)
  prefix-order fix       : applied - UL's UpdateMoveHelper prefix now sorts last
  end-of-path fix        : applied - 1 check(s) rewritten in UL's UpdateMoveHelper prefix
  approach-and-attack fix: applied - EAIULM_ApproachAndAttackTarget now suppresses smoothing
  'ps' toggle tracking   : applied - 'ps' also switches these patches
  PathSmoothing switched : on
  UpdateMoveHelper prefixes, in call order:
    PathSmoothing.Patches__EntityMoveHelper__UpdateMoveHelper (priority 400) -> UndeadLegacy.EntityMoveHelper_UpdateMoveHelper (priority 0)
  entities moving direct : 6, smoothing suppressed: 1
  end-of-path checks run : 34 (7 reported end-of-path)
  UL approach checks run : 0 (0 suppressed smoothing)
```

**For the zig-zag, the prefix call order is the proof.** `PathSmoothing` must come *before*
`UndeadLegacy` in that list. It is read live from `Harmony.GetPatchInfo`, so it reflects what
Harmony will actually call, not what was intended.

**For the end-of-path fix, `end-of-path checks run` above zero is the proof.** That counter can only
increment from inside the rewritten instruction sequence in Undead Legacy's own prefix.

`psul reset` zeroes the counters so a single scenario can be measured on its own. `psul` is this
mod's command and is separate from PathSmoothing's `ps`.

To make the counter move, give a zombie a jumpable gap to cross: stand somewhere it must path to a
ledge and then leap 2–5 blocks across (a small roof, a platform with a gap, the far side of a
trench) rather than walk straight to you. `UL approach checks run` only moves when a **Bear** is
chasing something, since `animalBear` is the only entity UL gives its own approach task.

### 3. Behaviour — A/B test

The isolating test is this mod's presence, with PathSmoothing left installed either way:

- **`Mods/PathSmoothingULCompat` removed** — zombies zig-zag diagonally between voxel rows on flat
  open ground instead of running straight at you, and commit to gap-jumps early, falling short.
- **restored** — they run straight at you on open ground, and jump from the ledge.

Note that `ps 0` is *not* the same test. It switches PathSmoothing off entirely, and with smoothing
off the node count is accurate again, so the original check is the correct one — which is exactly
why this patch follows that toggle. It is a good sanity check that the toggle tracking works
(`psul` will show `PathSmoothing switched: off`), not an isolation of this fix.

## Building

Needs only the .NET SDK — no Visual Studio, no .NET Framework targeting pack, no NuGet packages.
The project compiles against the BCL and Unity assemblies shipped in the game install
(`FrameworkPathOverride` points at `7DaysToDie_Data/Managed/`).

```
dotnet build src/PathSmoothingULCompat/PathSmoothingULCompat.csproj -c Release
```

The build stages a ready-to-copy mod folder at `dist/PathSmoothingULCompat/`.

The repo lives inside the game install, so paths resolve relative to it by default. To build
against a copy elsewhere, pass the game root explicitly:

```
dotnet build src/PathSmoothingULCompat/PathSmoothingULCompat.csproj -c Release "-p:GameRoot=D:\path\to\7 Days To Die\"
```
