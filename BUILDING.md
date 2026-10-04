# Building Game Mode Forge

You only need to tell the build where your copy of PUNK is. Everything else is already wired.

## Prerequisites

- **PUNK Playtest v0.12.9**, with **BepInEx** installed and the game **launched at least once**
  (BepInEx unpacks its `core` folder on first run).
- **Visual Studio 2022** (Community is fine) or just the **.NET Framework 4.7.2** targeting pack
  plus MSBuild.

## Why there's a setup step at all

This mod compiles against PUNK's own managed assemblies (`Punk.Main.dll`, `UnityEngine.*`,
`ServiceLocator`) and the BepInEx core. **Those DLLs are the game's property and are deliberately
not in this repo** — so the build has to find them in your own install.

Two folders are needed, both inside your PUNK directory:

| What | Where |
|---|---|
| Game assemblies | `<PUNK>\Punk_Data\Managed` |
| BepInEx core | `<PUNK>\BepInEx\core` |

`Directory.Build.props` at the repo root derives both from a single `PunkDir` property. **You never
edit a `.csproj`.**

## Pick one of these three

`<PUNK>` means the folder that *contains* `Punk_Data` and `BepInEx` — e.g.
`C:\Program Files (x86)\Steam\steamapps\common\PUNK Playtest`.

### 1. A local props file — easiest, and what most people want

Copy `Punk.props.example` to **`Punk.props`** in the repo root and edit the one line inside it.
`Punk.props` is git-ignored, so your path never lands in the repo.

Then build normally — in Visual Studio, or:

```bash
msbuild GameModeForge.sln -p:Configuration=Debug
```

### 2. An environment variable

Set **`PUNK_DIR`** to your PUNK folder. Nothing to create, nothing to ignore. Good if you work on
more than one Forge mod, since all three read the same variable.

### 3. On the command line

```bash
msbuild GameModeForge.sln -p:Configuration=Debug -p:PunkDir="C:\Games\PUNK Playtest"
```

> **Gotcha:** MSBuild splits the `-p:` switch on commas, so a path containing a comma
> (e.g. `D:\Games, Old\PUNK Playtest`) **fails to parse**, quoted or not. Use option 1 or 2 if your
> path has a comma in it.

If the build can't resolve the folder it stops with a message telling you what it looked for,
rather than burying you in a few hundred "type or namespace not found" errors.

## Output & deploying

The solution builds **four DLLs**:

| DLL | Lands in |
|---|---|
| `GameModeForge.dll` | `GameModeForge\bin\Debug\` |
| `NewGamePlus.dll` — New Game Plus, a separate mod | `NewGamePlus\bin\Debug\` |
| `SalvageShop.dll` — the Salvage Shop, a separate mod | `SalvageShop\bin\Debug\` |
| `PauseOptions.dll` — Options in the pause menu, a separate mod | `PauseOptions\bin\Debug\` |

Copy any of them into `<PUNK>\BepInEx\plugins\` (their own subfolder is fine) and launch the
game. None needs another: **New Game Plus**, **Salvage Shop** and **Pause Options** have no
reference to Game Mode Forge and find its log by type name at run time, like every Forge mod finds
another. (Salvage Shop finds New Game Plus the same way, to carry upgrades across a jump, and Weapon
Forge's rank, to warn before a ranked gun is scrapped.) (Weapon Forge
finds New Game Plus the same way, to carry ranked guns across a jump.)

> **There is no hot reload.** Settings are read at startup, so a change needs a full game restart.
> The exception is the BepInEx config file, whose entries are followed live — see below.

## What it does right now

**`GameModeForge.dll` registers no world rules — zero, on purpose.** What it does have is a
**settings tab** in the game's Options screen that switches Weapon Forge, Module Forge, Pause
Options, New Game Plus and Salvage Shop on and off (and shows New Game Plus's and Salvage Shop's
own settings), plus a **restart**
row (main menu only) that relaunches the game — useful on its own, since neither Forge
mod has a hot reload and restarting is how a JSON edit is picked up. New Game Plus is its own DLL
(below).

**The log is `BepInEx\GameModeForge.log`** (the launch before is `GameModeForge.prev.log`). Since
2026-09-25 `LogOutput.log` and the console get only the three load lines below and a copy of every
error, so they stay readable for BepInEx and other mods; every mode this mod registers logs to
`GameModeForge.log` too. `[Logging] OwnLogFile = false` puts everything back in `LogOutput.log`.

A successful load looks like this in `GameModeForge.log`, and the mode count being `0` is correct:

```
=== Game Mode Forge log - this launch started 2026-09-25 16:08:02 - BUILD 2026-09-25 16:08:00 ===
[Info   :Game Mode Forge] Game Mode Forge loaded - full log in BepInEx\GameModeForge.log (...)
[Info   :Game Mode Forge] BUILD 2026-09-25 16:08:00  (if this is older than ...)
[Info   :GameModeForge] mods found: Weapon Forge (on), Module Forge (on)
[Info   :Game Mode Forge] Game Mode Forge patches applied - 0 mode(s) registered
[Info   :GameModeForge] no modes active - the game is stock.
```

The `Game Mode Forge` lines (with spaces) are the ones `LogOutput.log` also gets.

That last line comes from the run-entry patch, so seeing it confirms the `RunData.Initialize` /
`GameSaver.Load` hooks are live. Opening **Options from the main menu** should then add:

```
[Info: GameModeForge] tab 3 built with 2 mod switch(es) plus the restart row
[Info: GameModeForge] settings tab added to the options screen.
```

The tab is built by cloning the game's own Gameplay tab at runtime, so **the log is instrumented at
every step** — it names what it could not find and why. If the build fails it says
`COULD NOT BUILD THE SETTINGS TAB` and leaves the game's own three tabs untouched; a mod that breaks
the settings menu is worse than a mod with no settings.

> **⚠ Mid-run the tab has NO restart row, on purpose.** The pause menu has its own Options button,
> wired in the Game scene as a `UnityEvent` (so it appears at no call site in the code) and shipped
> **inactive** in the playtest; `PauseOptions.dll` switches it on. From there the tab is built
> without its restart row, because "restart the game" mid-run would lose everything since the last
> save — a switch flipped there is written at once and takes effect at the next launch. The log
> says so once per session:
>
> ```
> [Info: GameModeForge] tab 3 built with 3 mod switch(es) - mid-run, so NO restart row: ...
> ```

With Pause Options installed, pausing a run for the first time adds these to `GameModeForge.log`:

```
[Info   :GameModeForge.PauseOptions] Pause Options (BUILD 2026-09-28 14:05:00) logs here, in Game Mode Forge's file.
[Info   :GameModeForge.PauseOptions] Options switched on in the pause menu. Esc / B closes Options and leaves the game paused; ...
```

Without Game Mode Forge the same lines go to `BepInEx\PauseOptions.log`. And in `LogOutput.log`,
its three load lines: `Pause Options loaded`, `BUILD ...`, and `Pause Options patches applied`
(or that it is switched off).

With New Game Plus installed, `LogOutput.log` gets the same three kinds of load line
(`New Game Plus loaded`, `BUILD ...`, `New Game Plus patches applied - kill all four Queens ...`),
and `GameModeForge.log` (or `BepInEx\NewGamePlus.log` without Game Mode Forge) follows the run:

```
[Info   :GameModeForge.NewGamePlus] run started: WORLD 1 - no rules in force.
[Info   :GameModeForge.NewGamePlus] pause menu: WORLD INFO under Continue, and START NEW GAME PLUS ...
[Info   :GameModeForge.NewGamePlus] the 4th Queen is dead - WORLD 1 CLEARED. The victory screen opens in a moment.
[Info   :GameModeForge.NewGamePlus] NEW GAME PLUS chosen (the victory screen): leaving WORLD 1 for NEW GAME PLUS 1 - ...
[Info   :GameModeForge.NewGamePlus] ARRIVED in NEW GAME PLUS 1 - enemies +50% health (on 25% of them), +30% more, +100% damage (hazards too). ...
[Info   :GameModeForge.NewGamePlus] enemy damage +100% in NEW GAME PLUS 1: every enemy's shots, beams, blasts, ... do x2 ...
[Info   :GameModeForge.NewGamePlus] enemy count +30%: every room's spawn budget is x1.3 in NEW GAME PLUS 1 ...
[Info   :GameModeForge.NewGamePlus] the new world's 1 ship(s) are placed with the carried grid(s) - ...
[Info   :GameModeForge.NewGamePlus] enemy health +50% on 25% of enemies (NEW GAME PLUS 1): 61 buffed so far, ...
[Info   :GameModeForge.NewGamePlus] saved on arrival - Save and quit now, relaunch and Continue, ...
[Info   :GameModeForge.NewGamePlus] enemy damage: the first harder shot from an enemy (...) on Ship...: 2 -> 4.
```

Its rules file, `BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.rules.json`, is written
with the defaults the first time a run starts; `New Game Plus Builder.html` (repo root) makes a new
one.

With Salvage Shop installed, `LogOutput.log` gets its three load lines (`Salvage Shop loaded`,
`BUILD ...`, `Salvage Shop patches applied - at a station, the grid tab has a SALVAGE tab ...`), and
`GameModeForge.log` (or `BepInEx\SalvageShop.log` without Game Mode Forge) follows what you do at
the SALVAGE screen:

```
[Info   :GameModeForge.SalvageShop] the SALVAGE tab is built - it is beside SHOP and VAULT on the grid tab at every station.
[Info   :GameModeForge.SalvageShop] rules file read: a scrap pays 2 Ex and a 50% chance of 1 Bond; an upgraded item refunds 100% of what it cost.
[Info   :GameModeForge.SalvageShop] Ex and Bond are still unused by the game (no drop, no price, no unlock) - salvage is their only source.
[Info   :GameModeForge.SalvageShop] scrapped POPPER: +2 Ex, +1 Bond.
[Info   :GameModeForge.SalvageShop] upgraded SHOTGUN: DAMAGE +10% (its step 1, paid 1 Ex; now +10% DAMAGE).
[Info   :GameModeForge.SalvageShop] saved 1 salvage-upgraded item(s).
[Info   :GameModeForge.SalvageShop] 1 salvage-upgraded item(s) restored from your save.
```

If the tab cannot be built the log says `the SALVAGE tab was NOT built` and why, and the shop and
vault are left exactly as the game made them. Its rules file,
`BepInEx\config\com.sugarheady.gamemodeforge.salvageshop.rules.json`, is written with the defaults
the first time the SALVAGE screen opens or a gun is built; `Salvage Builder.html` (repo root) makes
a new one.

## Notes

- `<PathMap>` is set in the `.csproj` on purpose: without it, the compiled DLL embeds the absolute
  path of whoever built it, which is then readable out of any released binary. **These repos are
  public. Please leave it in.**
- Settings live in `BepInEx\config\com.sugarheady.gamemodeforge.cfg`, generated on first run, with
  one entry per registered mode under `[Modes]`. Every mode ships **off**. Edits made while the
  game is running are followed, so a mode can be switched between runs without a restart — but a
  run-entry mode still only takes effect on the *next* run, and says so in the log.
- The `Directory.Build.props` fallback that points at a sibling `WhiteTeslaMod` folder is a
  leftover from the original dev machine. It only activates if that folder exists, so it's inert
  for you and can be ignored.
- Companion mods: **Weapon Forge** and **Module Forge**. All three cooperate at runtime but have
  **no build-time dependency** on each other — they find each other by reflection, so you can build
  and run any one of them alone.
