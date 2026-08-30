# PathSmoothing — Undead Legacy compatibility patch

A small 7 Days To Die compatibility mod which allows **[PathSmoothing by redbeardt](https://www.nexusmods.com/users/102102433)** to work correctly
when **[Undead Legacy by SubQuake](https://ul.subquake.com)** is installed.

Both mods patch the same vanilla movement method, and UL's patch wins — leaving PathSmoothing
installed but largely inert. This patch puts it back in the loop.

It is additive: it does not modify, replace or redistribute either mod's DLL. Install all three and
it wires itself in at load time. With either of the other two missing, it logs a line and does
nothing.

Tested against **Undead Legacy 2.7.15 through 2.7.19**. Other versions still work — the mod warns
rather than blocking, and each fix reports an error if the code it targets no longer matches.

## What it fixes

1. **Zombies zig-zagging along the grid path.** PathSmoothing's smoothing is overwritten before UL
   ever reads it, so entities follow the raw grid path — visibly stepping diagonally between voxel
   rows on open ground instead of running straight at you.
2. **Gap-jumps committed too early.** Zombies leap for an out-of-reach target before reaching the
   ledge and fall short, instead of giving up and switching to breaking blocks.

Both follow PathSmoothing's own `ps` / `pathsmoothing` console toggle, so `ps 0` returns the
original behaviour just as it does for PathSmoothing's own patches.

## Installing

Requires PathSmoothing and Undead Legacy to already be installed. Copy `dist/PathSmoothingULCompat/`
(checked into this repo, so no build needed) into the game's `Mods/`:

```
Mods/PathSmoothingULCompat/
├── ModInfo.xml
└── PathSmoothingULCompat.dll
```

Load order does not matter.

## Verifying it works

### Did the patches install?

Check the log for lines prefixed `[PathSmoothing/UL]`, in either
`%APPDATA%\7DaysToDie\logs\output_log_client__*.txt` or `BepInEx\LogOutput.log`. Anything at error
level means a fix did **not** apply, and names the behaviour that is therefore missing — the
expected outcome if a future UL or PathSmoothing release moves the code being targeted.

### Is it actually doing anything?

Run `psul` in the console (F1) — this mod's command, separate from PathSmoothing's `ps`:

```
PathSmoothing/UL compatibility patch is WORKING
  prefix order      : PathSmoothing -> UndeadLegacy (correct)
  end-of-path fix   : applied, 1 check rewritten
  smoothing (ps)    : on
  psul info         : version, counters and diagnostics
```

`WORKING` means both fixes are installed **and** the call order is right. The order is read live
from Harmony, so it reflects what will actually be called rather than what was intended:
`PathSmoothing` has to come before `UndeadLegacy`, or its smoothed move target is overwritten before
UL ever reads it. With PathSmoothing switched off (`ps 0`) the block reports `IDLE`, which is by
design rather than a fault.

`psul info` adds the Undead Legacy version, the full patch state and the counters:

```
PathSmoothing/UL compatibility patch is WORKING
  prefix order      : PathSmoothing -> UndeadLegacy (correct)
  end-of-path fix   : applied, 1 check rewritten
  smoothing (ps)    : on
  Undead Legacy     : 2.7.19 - tested (read from [BepInPlugin] attribute)
  prefix-order fix  : applied - UL's UpdateMoveHelper prefix now sorts last
  end-of-path fix   : applied - 1 check(s) rewritten in UL's UpdateMoveHelper prefix
  'ps' tracking     : applied - 'ps' also switches these patches
  prefix call order : PathSmoothing.Patches__EntityMoveHelper__UpdateMoveHelper (priority 400) -> UndeadLegacy.EntityMoveHelper_UpdateMoveHelper (priority -2147483648)
  entities          : 6 moving direct, 1 smoothing suppressed
  end-of-path checks: 34 (7 reported end-of-path)
```

**`end-of-path checks` above zero** is the proof for fix 2 — that counter can only increment from
inside the rewritten code in UL's own movement prefix. To make it move, give a zombie a jumpable gap
to cross rather than a clear run at you. `psul reset` zeroes the counters so a single scenario can be
measured on its own.
