# Game Mode Forge — working notes for Claude (and humans)

Guidance for anyone, human or AI, making changes here. Read it before writing code.

Target: **PUNK Playtest v0.12.9**, Unity **6000.3.4f1**, .NET Framework **4.7.2**, BepInEx + Harmony.

> **⚠ IT REGISTERS ZERO WORLD RULES, ON PURPOSE.** The scope conversation has not happened, and
> everything about *what a mode may do* is still an open question listed at the bottom. **Do not
> arrive with an implementation of a mode.**
>
> What it *does* have is a **settings tab** in the game's options screen, which switches the sibling
> Forge mods on and off. That is a mod-manager feature rather than a game mode — it exists because
> it was the first thing worth putting in the tab, and the tab had to be proven somehow.
>
> **This repo builds SIX DLLs**: `GameModeForge.dll`, **`PauseOptions.dll`** (`PauseOptions/`,
> 2026-09-28, switches on the pause menu's own Options button), **`NewGamePlus.dll`**
> (`NewGamePlus/`, 2026-09-29), **`SalvageShop.dll`** (`SalvageShop/`, 2026-10-02),
> **`EnemyTraits.dll`** (`EnemyTraits/`, 2026-10-12, New Game Plus stage 3) and
> **`Leaderboard.dll`** (`Leaderboard/`, 2026-10-12, New Game Plus stage 4). See "Pause Options",
> "New Game Plus", "Enemy Traits", "Leaderboard" and "Salvage Shop" below.
>
> **★ STAGE 4 IS BUILT (2026-10-12, his go: "go ahead and build stage 4 of 4 of new game plus"),
> NOT YET PLAY-TESTED**: the Leaderboard (CONCEPT.md 5 / 5.1, what the build chose in section 12),
> and New Game Plus's carry contract 3 (`NgCarry.RunEnding`, for END RUN).
>
> **★ STAGE 3 IS BUILT (2026-10-12, his go: "go ahead and build stage 3 of 4 of new game plus"),
> NOT YET PLAY-TESTED**: Enemy Traits (wind-up, grow, overload, ricochet - CONCEPT.md 3.6 / 3.7,
> what the build chose in section 11), and New Game Plus's fire rate now reaches volleys and beams.
>
> **★ NEW GAME PLUS STAGE 1 IS BUILT (2026-09-29, his go: "check over the concept … otherwise go
> ahead with the build") AND PASSED IN PLAY (R21 part 1). STAGE 2 HAS STARTED: its first piece,
> health bars in layers, is built (2026-10-05, his go: "go ahead and build the health bar
> layers"), and so are its second, the enemy weapon stats ("go ahead and build the enemy weapon
> stats"), and its third, the station unlock cost ("go ahead and build the station unlock cost"),
> all the same day, and **the rest of stage 2 on 2026-10-06** ("lets move on to build whatevers left
> of stage 2"): money drops, harder enemies earlier (both dials), his enemy table and the first-world
> switch. None of stage 2 is play-tested yet. Enemy Traits (stage 3) and
> the Local Leaderboard (stage 4) have since been built too.** Read [`Docs/CONCEPT.md`](Docs/CONCEPT.md) before touching any of them: it holds every decision
> Sugarheady made and the measured facts they stand on, and its build order (section 6) says what
> stage 2 is. The decisions made while BUILDING are in CONCEPT.md's sections 9 (stage 1) and 10
> (stage 2).
>
> **★ THE SALVAGE SHOP IS BUILT (2026-10-02, his go: "read the concept doc ... and then go for the
> build if ready"), NOT YET PLAY-TESTED.** The design is
> [`Docs/SALVAGE SHOP - CONCEPT.md`](Docs/SALVAGE%20SHOP%20-%20CONCEPT.md): section 19 is the build
> in one place, **section 21 is what the build chose** where the concept left it to the build.
> See "Salvage Shop" below.

## The settings tab (built 2026-09-21; first run R19, two layout fixes 2026-09-27 — R20)

A fourth **"GAME MODE FORGE"** tab beside Gameplay / Video / Audio, holding one Off/On row per
installed Forge mod plus a **restart** row.

**★ The restart row restarts whether or not anything changed**, and that is the point of it rather
than an edge case: neither Forge mod has a hot reload, so *restarting is how a JSON edit is picked
up*. It first shipped bouncing back when nothing was pending — a restart button that refused to
restart, which is a refusal with no visible reason and reads as the row being broken. Pinned.

**★ A `.dll` CANNOT BE UNLOADED HERE.** Unity's Mono runs a single AppDomain and .NET Framework
4.7.2 has no collectible load contexts, so "turn the mod off" can only mean *"next launch, do
nothing at all"* — and **relaunching the game is the only real DLL reload there is.** That is why
`ForgeRelaunch` exists and why the whole feature is simple rather than clever.

**Each sibling owns its own switch.** `[General] Enabled` lives in *that mod's* BepInEx config and
is honoured at *its* startup with this mod absent; `ForgeModControl` only reads and writes those
files. So [[mods-must-stand-alone]] is untouched and the switch works by hand with this mod
uninstalled — which is how R19 test 271 is deliberately written.

⚠ **Game Mode Forge is not in its own switch list**, or there would be no way back but a text
editor.

### The five hazards this cost, all pinned by `structuretest.py`

- **⚠ `ShowTabCoroutine` indexes `tabButtonImages[i]` for `i < tabs.Length` with no guard.** Growing
  one array without the other throws inside the game's own coroutine, every time you change tab —
  the `HitscanWeapon.visualsInstances` shape. The builder refuses rather than half-doing it.
- **⚠ `OptionsMenuItemButtons.OnEnable` forces its selection back to 0.** So the saved value must be
  pushed with `SetSelection` **before** subscribing to `SelectionChanged`, or opening the menu
  writes "off" over the player's setting. `GameplayOptionsTab` gets this right; we copied its order.
- **⚠ `RemoveAllListeners` does not remove a prefab-wired persistent listener.** A cloned tab button
  would still call the donor's `ShowTab`. Use `SetPersistentListenerState(i, Off)`.
- **⚠ Commit before restarting, and do not restart on a failed write** — restarting after a failed
  write looks exactly like the switch not working. Likewise, only `Application.Quit()` once a
  relaunch has actually been started.
- **⚠ A save that used Forge content will not load with that mod off.**
  `Vault.RestoreFromMemento` does `registry.Get(id).DeepCopy()` with no null check and
  `ConfigRegistry.Get` returns `default(T)` on a miss. The tab warns when a save **exists**, and
  deliberately over-warns: whether a save *uses* Forge content cannot be answered without loading it.

**The whole tab build is wrapped.** A mod that breaks the settings menu is worse than a mod with no
settings, so any failure must leave the game's own three tabs working and say so in the log.

### Its first real launch (R19 test 272, 2026-09-25) — the wrapper worked, the build did not

The tab never appeared, and the log named the line: `NullReferenceException` from
`Component.gameObject` inside `BuildInner`. **The template row is one of the donor rows** — the Aim
Assist row is in `items` — so the loop that deletes the donor rows had already destroyed it, and the
next line read `template.gameObject` off a destroyed object. The build died on its **last line of
cleanup**, every time. Three more things were behind it that had never run:

- **⚠ The tracked tab image is not the button.** Measured off `MainMenu.unity`: each tab is a
  wrapper (`GameplayButton`, with an Animator) holding a `ButtonBody` — and `tabButtonImages` points
  at the **body**, which also carries the `Button` whose `onClick` calls `ShowTab`. Cloning the body
  alone would have put a second body *inside* the Gameplay button. The wrapper is cloned now.
- **⚠ A static `_built` bool that nothing cleared.** Every main-menu visit is a fresh scene with a
  fresh `OptionsScreen`, so the tab would have appeared once and never again after a run. The guard
  is per screen instance now.
- **⚠ The PAUSE menu has an Options button — and in the playtest it is SWITCHED OFF.**
  `UI/PauseMenu/.../OptionsButton/ButtonBody` calls `OptionsScreen.Open` as a scene `UnityEvent`
  (at no call site in the code), but the `OptionsButton` object ships `m_IsActive: 0` in
  `Game.unity`. R19 said "the pause menu does have Options" as a correction — **that was wrong**,
  read off the wiring without checking whether the button was active, and he found no options
  mid-run. **Pause Options (2026-09-28) switches it on.** From then the tab is built there too, but
  the **restart row** is built only where a `MainMenu` exists in the screen's own scene — his
  answer, "yes, but no restart row", because a restart from a pause loses an unsaved run.
  > **A wired event on an inactive object is a feature that is not there.** Check `m_IsActive` up
  > the whole path before saying a scene exposes something.

### Mid-run the tab writes straight through (2026-09-28)

With no restart row, nothing would ever apply a pending switch, so **mid-run a toggle is written to
its mod's file the moment it is flipped** (`OnModToggled`, `inRun`); a failed write puts the switch
back. That broke an assumption the main-menu flow had quietly relied on:

- **⚠ "What the file says" and "what this launch runs" are now different facts.** Before, a write
  was always followed by a restart. `Entry.Launched` is read ONCE, at this mod's own Awake (the
  first `Refresh`), and never again; `DiffersFromLaunch` compares the menu's choice against it.
- **A row that differs shows the game's own "changed" marker** — `OptionsMenuitemBase.SetDirty`,
  which drives a `Dirty` layer in `AnimatedOptionsItem.controller` (`Visual/Dirty` → active). The
  game authored that marker and **never calls it**; it is ours now. The caption says
  `<Mod> - OFF next launch`, auto-sized to its fixed 324-wide, non-wrapping label.
- **The save warning is measured against `Launched`, not the file** (`WarnFor`): a switch flipped
  mid-run is already in the file, so nothing is "pending" — and the save it endangers is the run
  being played. The restart caption checks the warning FIRST for the same reason. Mid-run the row
  itself carries `(this run may not load)`.
- `Entry.AffectsSaves` is false for Pause Options, which adds nothing to a run.
- Checked: neither sibling writes its own config after Awake (every `Config.Bind` is in the plugin's
  Awake, no runtime `Save`), so a switch written mid-run is not quietly rewritten by the mod's own
  in-memory copy before the next launch.

### ★ …and since R21, in the main menu too (2026-10-05)

R21 273: *"if i change any settings within the Game Mode Forge menu, like toggling on Options and
turning off weapon and module forge, it doesn't save those settings."* The main menu held a flip as
"pending" until the restart row's Apply committed it. His log has the restart (`relaunching this
same copy`) and **no `-> ON` / `-> OFF` line at all**, so Commit found nothing pending - something
cleared the choice between the flip and Apply. Reading did not find what (the log shows one
`OnOpened`, no extra `Refresh`, no tab close). So the window was removed rather than guessed at:
**`OnModToggled` writes through everywhere now**, the restart row only restarts (its Commit is a
backstop), and the `-> ON (takes effect on the next launch)` line appears the moment a switch moves.
`structuretest.py` fails if an `inRun &&` comes back in front of that commit.

### …and the restart row is pinned under the list (R21 part 2, 2026-10-06)

His 273 note: *"is there a way to have the restart now and not now outside the scroll area ...
That way i dont need to scroll all the way to the bottom to toggle it?"* The clip (`RectMask2D`)
was on the tab itself, so anything in the tab's rect but outside the scroll was clipped with it.
`ForgeOptionsMenu.PinBelow` (main menu only - mid-run there is no restart row) moves every row but
the restart into a new child, `ForgeScrollView`, which fills the tab down to one row (+12) above
its bottom and carries a copy of the tab's `VerticalLayoutGroup`; the tab's own layout is switched
off and the restart row is anchored to the tab's bottom edge, full width. `EnableScrolling` puts
the clip and the wheel catcher on the LAYOUT's object, and the scroll arithmetic skips
`tab.pinned` (never scrolled to, never counted). It stays the last of `items`, so the keys and the
pad still reach it. Any failure puts the rows back and the layout on. **`childAlignment` is copied
by reflection**: its type, `TextAnchor`, is in `UnityEngine.TextRenderingModule`, which this csproj
does not reference. `structuretest.py` pins it; not yet seen in game (R21 355).

### …and the scroll rebuilds the right object (R21 part 3, 2026-10-09)

R21 327: *"it wouldn't let me scroll down past 3 options in game mode forge tab ... the other
options are kind of merging together"* - in the MAIN MENU; *"starting a new game and opening the
option menu in the pause menu had it load properly."* The pin above is main-menu only, and it
switches the tab's own `VerticalLayoutGroup` off. `ForgeOptionsTab.Apply` moved the column (the
layout's top padding) and then asked `LayoutRebuilder.MarkLayoutForRebuild` for the **tab's** rect
- which walks up from the rect it is given, finds no ENABLED layout there, and rebuilds nothing.
Mid-run the layout IS the tab's, so the pause menu worked. It rebuilds `_layout.transform` now.
**Inferred from the code and Unity's documented MarkLayoutForRebuild, not seen**: his note is the
only evidence, and it fits exactly (main menu broken, pause menu fine).

> **A refactor that moves the thing a call acts on has to move the call's argument too.**
> `PinBelow` moved the rows into a new child and pointed `_layout` at it; the one line that still
> named `transform` was three methods away.

**Weapon Forge has rows now** (R21 part 3, his 315 ask "Can you make a slider for that?"):
`RowsType = "WeaponForge.ForgeSettingsTab"`, one slider, *Interceptor snap speed* (50-400% by 25),
which writes Weapon Forge's own `[Drone modes] InterceptSpeed`. ⚠ That is the first sibling row
that writes the sibling's **.cfg** mid-run (New Game Plus's and Salvage's write their rules JSON).
BepInEx saves a whole `ConfigFile` from memory, and this tab writes the same file's `[General]
Enabled` from a fresh copy - so Weapon Forge **re-reads the file (`Reload`) before setting the
value**, or its save would put back the Enabled this launch started with.

## Pause Options — the second DLL (2026-09-28)

The ask: *"a separate .dll that connects to GameModeForge that enables the option menu from pause
screen mid run instead of having to save and quit the run to goto the menu."* The four answers:
listed in this tab like the siblings; the tab mid-run **without** the restart row; the code in
**this** repo; its log lines in **GameModeForge.log**.

- **"Connects to" means one thing: it is a row in `ForgeModControl`'s list.** Otherwise it follows
  every sibling rule — no reference, no `[BepInDependency]`, its own `[General] Enabled` in its own
  cfg (`com.sugarheady.gamemodeforge.pauseoptions.cfg`), the gate above every patch, and it works
  with this mod absent. Its enabled-blurb is word for word `ForgeModControl.EnabledBlurb`'s, pinned.
- **Namespace `GameModeForge.PauseOptions`, on purpose:** this mod's `GameErrorCatcher` claims every
  game error with a `GameModeForge.` frame, so a throw out of a Pause Options patch lands in
  `GameModeForge.log` with no code of its own.
- **`PauseLog`** finds `GameModeForge.ForgeLog` by type name and hands each line to a source made by
  its `Source(name)`; with this mod absent it keeps its own `PauseOptions.log` (a copy of `ForgeLog`,
  opened on the first line so a launch with this mod installed never makes an empty one). **The
  route is decided on the first in-game line** — Awake writes only through the registered Logger,
  because at Awake this mod may not have loaded yet. `pauselogprobe.py` runs both routes.

### Measured off Game.unity, and the two stock bugs

- `UI/PauseMenu/Sorter/Menu/Buttons` has an **ENABLED** `VerticalLayoutGroup` (+ `ContentSizeFitter`),
  so the switched-on button lays itself out in the column — unlike the options tab row. **But it
  lays out height only**: `OptionsButton` keeps its own 325.9 width beside 389.084, and the stock
  order is Continue, Restart, SaveAndQuit, Quit (off), a 71-high **`Spacer`**, then Options — R20
  291 saw both the width and the gap. `Tidy` moves Options to straight under `SaveAndQuitButton`
  (the Spacer stays at the bottom, where it was on screen before) and copies that button's width.
  Explicit navigation already threads through it (Save and quit ↔ Options; Options → the inactive
  BugReport, so down stops there). Its caption is already `OPTIONS`.
- The Game scene's `OptionsMenu` is a complete copy of the main menu's: every `OptionsScreen` field
  wired, the same controller, sorting order 10000 over the pause's 300, a full-screen raycast
  blocker, and every wait `WaitForSecondsRealtime` — so it runs with the pause's time stopped.
- `AnimatedScreen.RefreshElementList` is `GetComponentsInChildren<AnimatedScreenElement>(true)` — it
  includes INACTIVE elements, so a button switched on before `UIScreen.Open` animates in with its
  neighbours. That is why the switch-on is a **prefix on `PauseScreen.Open`**: `UIScreen.Open`
  then enables its Selectable and `UIScreen.Close` switches it off again, like every other button.
- **⚠ Bug 1: Esc / B closed Options AND the pause.** `OptionsMenu/Close` and `Menu/Back` are the same
  keys (Escape, gamepad East), and `PauseScreen.Update` closes on `Menu/Back` with no guard.
  `MainMenu.Update` has the guard (`!optionsScreen.isActiveAndEnabled`); the pause does not. Fixed
  by skipping `PauseScreen.Update` (a bool prefix) while our Options screen is active and enabled —
  which also covers the 0.5 s it stays active while animating closed.
- **⚠ Bug 2: a gamepad could press a hidden pause button.** The EventSystem's UI module
  (`DefaultInputActions`) keeps navigating and submitting on the pause buttons under Options; stick
  up then A (also `OptionsMenu/Apply`) pressed "save and quit" behind the screen. Fixed by switching
  the pause's Selectables off (`enabled = false`, the way `UIScreen.ToggleObjects` does) in a
  postfix on `OptionsScreen.Open`, and back on — only the ones it switched off, only if the pause
  is still open, with OPTIONS selected — in a postfix on `OptionsScreen.OnDisable`.
- **It stands aside if the game switches the button on itself** (`activeSelf` already true): the
  playtest is live, and two fixes for one bug fight. It also refuses a button whose persistent
  onClick no longer reaches `OptionsScreen.Open` in the same scene, and puts the button back off if
  a parent turns out to be inactive.
- Mid-run settings: audio applies at once, video on Apply, aim assist (`AssistedBarrel`) and rumble
  (`ShipGamepadRumble`) are read live. `disableCameraSway` is read ONCE, in `GameController`'s
  `OnLevelGenerated` state machine (`Camera.main`'s sway component `.enabled = !disableCameraSway`),
  so a mid-run change shows from the next run or Continue.
  > **⚠ Corrected 2026-09-28: this line first said "read by nothing".** That came from a grep of the
  > decompile, which does not emit `async` state-machine bodies — the read is only in the IL.
  > **Search the state machines too** (`ildump`), the same trap as searching only code and not the
  > scene YAML.

### R19's second run (2026-09-27) — two more, both about WHERE rather than WHETHER

- **⚠ The tab row lays nothing out.** `Buttons` carries a `HorizontalLayoutGroup` with
  `m_Enabled: 0`, so each tab is at a fixed `anchoredPosition` (282.8 wide, 10.12 apart, measured)
  and a cloned button keeps its donor's — the new tab sat exactly on GAMEPLAY and read as having
  *replaced* it (272). `Relayout` now splits the span the three stock tabs filled four ways, which
  also keeps the new tab clear of the right-hand key hint. Tabs change with **Q / E** (LB / RB).
- **⚠ `steam://rungameid/` launches the copy STEAM installed**, not the one running. He tests on a
  separate modded copy, so the restart came back with no mods at all (273). `TryExecutable` goes
  first now; the game's `SteamAPI.RestartAppIfNecessary` only bounces a launch through Steam when
  the process was not started by Steam and has no `steam_appid.txt`, and a copy that runs when he
  starts it by hand passes that test the same way for a child it launches.
- **⚠ …AND THEN THE RIGHT COPY CAME BACK STOCK (R20 273):** *"it didn't load any mods this time
  actually. Just the base game."* Doorstop (the `winhttp.dll` proxy that starts BepInEx) sets
  `DOORSTOP_INITIALIZED` in the process environment and skips any process that already has it — its
  guard against loading twice into a game's helper processes. A process started from this one
  inherits the environment (ShellExecute too), so the relaunch looked already-loaded and ran stock.
  `ClearLoaderMarkers` removes every `DOORSTOP_*` value just before `Process.Start` and puts them
  back if the start throws. **Inferred from Doorstop's documented behaviour, not measured** — the
  game folder is outside this workspace — so the log line names what it cleared, which settles it
  on the next restart.
- The clone is made **under an inactive holder** so nothing on it wakes before the donor rows are
  gone (`AnimatedScreen` collects its elements in `Awake`), and any failure after the clone exists
  **destroys the clone** — the first version left an orphaned inactive copy in the menu.

> **Search the scene YAML, not only the code** — the same trap as the 83 prefabs wiring
> `HealthBase.onDamage` to `SfxPlayer.Play` in Weapon Forge, one project over.

## New Game Plus — the third DLL (stage 1 built 2026-09-29)

The design is `Docs/CONCEPT.md` section 2; this is what the code rests on. Every item is pinned by
`newgameplustest.py`, and the arithmetic is compiled and run by `ngmathprobe.py`.

**Files:** `NgRun` (the world's state, the save's record, arrival), `NgJump` (leaving and
arriving), `NgBuffs` (enemy health + count, the marker), `NgDamage` (enemy damage %, added to
stage 1 on 2026-10-02), `NgLayers` (health bars in layers, stage 2, 2026-10-05), `NgWeapons`
(enemy weapon stats, stage 2, 2026-10-05), `NgStations` (the station unlock cost, stage 2,
2026-10-05), `NgMoney` (money drops) and `NgWorld` (harder enemies earlier and the enemy table,
both 2026-10-06), `NgVictory`,
`NgPause`, `NgWorldInfo`,
`NgRules` (the rules JSON), `NgSettings` (the cfg switches), `NgMath` (pure arithmetic), `NgTab`
and `NgCarry` (the two public contracts), `NgUi`, `NgLog`.

- **★ NEVER `GameController.GameWon`.** Its other subscriber, `LeaderboardScoreSubmitter`,
  uploads the score. (Its object, `GameController/ScoreSubmitter`, ships switched OFF in the
  playtest's Game.unity - measured 2026-10-12 - so today nothing uploads. The rule stands, for when the
  devs switch it on.) The win is detected off `RunData.RegisterBossKilled` reaching
  `GameController.bossCount` (4), and the victory screen is the game's own `UI/GameWonSceen`,
  opened by us, retitled, with a third button cloned from its second.
- **★ THE ORDERING TRAP.** `GameSaver.Load` restores every entity before RunData, so NG+'s record
  (`newgameplus.txt` in the save folder) is read in a **prefix** on Load, with NG+'s reset first
  in the same prefix — the one place the house "read a record in a postfix" rule is wrong. It is
  written in a postfix on `Save(string)`.
- **★ THE GAME MAKES THE SAVE FOLDER ON A WORKER THREAD** (`SaveOnThread` →
  `UniTask.SwitchToThreadPool` → `Directory.CreateDirectory`), racing the rest of `Save`. On a
  run's first save a sidecar written in a Save postfix can find no folder. In practice the
  synchronous map encode in `SaveFoW` (two `EncodeToPNG`s) usually outlasts the pool thread, so
  the race is usually won — nothing guarantees it. NG+ creates the folder itself
  (`Directory.CreateDirectory` is safe to race), as Weapon Forge's wingman record always has.
  **Weapon Forge's rank record does not** — left alone on his call until R21 test 309 shows
  whether the race is ever lost.
- **★ `RunData.Initialize` RUNS FOR A CONTINUE TOO** (every Game scene makes a RunData), before
  `GameSaver.Load`. So its prefix asks `GameScene.arguments.isContinue` and leaves a Continue to
  the Load prefix.
- **★ THE JUMP IS A NEW RUN, NOT SAVE SURGERY.** Capture (grid memento per ship, vault memento, run
  memento — through Odin's `SerializationUtility`, so what crosses is a detached copy exactly as a
  save stores it), `GameScene.GoToGameScene` with a new seed and `isContinue = false`, then in the
  new world `ShipManager.PlaceShipEntity` is replaced: each ship's data is made and **held back**,
  and when the last one exists they are finished (grid restored from memento, stats, fill, carried
  tanks) and added **together**, sorted by instance id — because which ship is player 1 (the lower
  id) is only known once both have ids. A failure part-way still adds every ship on its loadout.
- **★ PAIR SHIPS BY ORDER, NEVER BY ID.** An id is `rnd.Next()` from the world's seed.
- **★ THE ID TRAP, AND THE OWNER TRAP BEHIND IT.** `Enemy.Initialize` runs inside `CreateData`,
  before the id — so nothing is decided there. And at `EntityManager.Add` a player drone is
  indistinguishable from a wild enemy: every path that makes one (the game's gadget, Weapon Forge's
  swarm and wingman bodies) calls `CreateEntity` (which Adds) BEFORE `SetOwner`. So an added enemy
  is queued and decided **two frames later**, and anything owned by a `"Ship"` entity at any depth
  is skipped.
- **★ HEALTH IS THE DAMAGE RESOURCE.** Measured on the embedded modules: drones take damage through
  Caps / Electron / Purple, the Crawler through Tech, and many enemies carry White / Fuel / Caps
  tanks that are ammo. Co-op scales every `ModifyResourceCapacity`; NG+ scales only the one whose
  resource is the prefab's `DamagableResource.resource`. **Crates carry `Enemy` too** — an
  `AIAgent` is required as well.
- **★ `SavableEntity.CreateData` ALREADY RAN `OnCreate`**, so a new enemy's tanks exist and are full
  before NG+ sees it: the multiply is followed by `RecalculateStats` and a refill of a tank that was
  full. A restored enemy keeps its saved value (the save stores tank values, not capacities).
- **The buff is the whole series** (`baseValue`, and `change` in Add mode), and a module instance is
  multiplied once (`ConditionalWeakTable`) — a restored enemy's module is a new instance.
- **Enemy count** scales `difficultyMultiplier` in `EnemyGenerator.PlaceBasedOnEcosystem`; boss
  rooms (multiplier 0) are never called, so it can never add a Queen.
- **Station count across a jump** is `old + (fresh − 1)`: `RunData.Initialize` counts 1 itself.
- **The settings tab scrolls now** (`ForgeOptionsTab`): rows are 150 high, about four fit, and NG+
  adds twelve. A `RectMask2D` clips, the layout's top padding moves the column, a postfix on the
  private `OptionsTab.SetSelected` follows the selection, and `IScrollHandler` takes the wheel —
  no Input System reference needed.
- **Sibling setting rows are a string contract** (`ForgeTabRows`): `Rows / Get / Set / Label` on a
  public static class found by type name (`Entry.RowsType`). No shared type in either direction.
- **The carry contract with Weapon Forge is `NgCarry`**: `Carrying`, `Plus`, `Leaving`, `Arrived`,
  `NewShipId`. Weapon Forge subscribes by type name (`ForgeRankCarry`); nothing here knows it
  exists.

### What R21 part 1 changed (2026-10-05)

His first play of stage 1: the jump, the carry, the buffs, Continue and the victory screen all
passed. Fixed from his notes and his log:

- **★ A borrowed popup's title goes back AFTER it has faded (307).** `NgUi.Confirm` put the pause
  Restart's own title back in the same call as `Prompt.Close` - which only STARTS the fade - so
  END RUN's Yes swapped "End this run?" for "Restart?" on a popup still on screen, and it read as
  a second prompt. `RestoreLater` waits for `Prompt.canvas` to switch off (capped at 3 s) and only
  puts it back if the text is still ours. **Anything that re-titles a game popup must do the same.**
- **★ The victory subtitle is a wrapped block, one rule to a line (300).** It was one line ~1,750
  wide, off any screen narrower than 16:9. `NgRules.SummaryParts` gives the rules one at a time;
  the box is 1,100 x 165 at y -14 (part 2: it was 185 at -20, which reached the buttons), between
  the title (bottom y 79) and the buttons (top y -118), auto-sized 30 down to 14.
- **★ `Font_Minimum` HAS NO "%" (R21 part 2, 307: "the % is like double the size").** Measured off
  the font assets: the game's title font - the victory screen's, the game-over stats', the confirm
  popup's - lacks `# % & < > @ [ ] ^ { } ~` and the backtick, and has NO fallback table of its own, so TextMeshPro
  draws a missing glyph from its global fallback at that font's size. `8-bit-hud SDF` (the options
  rows) has all 95. So `NgUi.SetText` goes through `Drawable`: when the text's font says
  `HasCharacter('%')` is false, `NgMath.PercentAsWord` writes "percent" (never inside a rich-text
  tag). **`NgUi.Fit` sets `fontSize` too**, so a text whose auto-sizing does not take is never left
  at its stock size (the subtitle's is 70) - R21 307 could not tell which happened, and
  `NgUi.SayFit` now logs the size a fitted text came out at, its lines, and any overflow.
- **WORLD INFO hides the pause menu and is near-solid** (his ask): the pause `UIScreen`'s canvas is
  switched off while it is up and back on at Close only if this switched it off and the pause is
  still open; the backdrop is 96% black (the game-over screen's is 75%).
- **The game-over screen names the New Game Plus reached** (his ask): a `GameOverScreen.GetStats`
  postfix adds two lines. Measured: the stats text fills the screen centred at size 50, five
  lines are 300 high and the title-to-buttons gap is 560, so seven fit.
- **★ An enemy destroyed in the two frames it waits is skipped (`_destroyed`).** R21's log had 37
  `could not buff enemy Enemy_Child (NullReferenceException)`: Enemy_Child is the Cross Queen's
  minion (`Weapon_Minion_CrossQueen`), spawned in bursts into the player's fire, and its
  neighbours in the same bursts WERE buffed. A queued enemy's `EntityData.Destroyed` marks it.
  Inferred, not measured - so any other failure is now logged in full (with the stack) once per
  enemy kind and counted in the report after that.
- **The marker is his bubble** (302: *"I dont really see the marked enemies"*): a 13-pixel amber
  ring with a plus, beside the enemy's upper right, breathing - it was a 3-pixel square.

### What R21 part 3 changed (2026-10-09)

- **★ THE MARKER FOLLOWED THE SHIPS' SPAWN METHOD (341).** *"I got them to show up once when i
  saved and quit and then continued after. but so far i dont really see them at all."* The
  re-attach was a postfix on `EntityGameObjectManager.SpawnPrefabForEntity`, and the decompile
  has exactly ONE caller of that: `ShipManager`. Every enemy object - a segment loading
  (`InstantiateGameObjects`) or one made in play (`CreateEntity`) - comes from
  `SpawnObjectForEntity`. So a marker only appeared when the enemy's object happened to exist at the
  moment its buff landed: the Queen's children (spawned beside you) and the enemies round you on a
  Continue. **A name that sounds right is not a caller list - grep who calls it.**
- **The Queen's children wearing the marker (his 344 note) is correct**: they are enemies, so the
  coverage share applies to them like any other.
- **WORLD INFO is a box (339)**: `Stats` on the game-over screen is a FULL-SCREEN rect, centred,
  grey 0.56 at size 50 (measured off Game.unity), so a long list sat over the title and under
  BACK (top y -306). Now 1500 x 645 from y 360 down, top-left, near white, 30 down to 12, the
  title at y 430; `NgUi.SayFit` logs what it came out at. **Every % is written as a word there**
  (338: *"Might need to remove the % In the world info"*), whatever the font check says.

### The rest of stage 2 (`NgMoney`, `NgWorld`, the first-world switch - 2026-10-06)

His go: *"lets move on to build whatevers left of stage 2 of 4 of new game plus"*. The design is
CONCEPT.md 2.5 and section 7; the build's choices are section 10. Rules file **version 5**:
`moneyDrops`, `harderEarlier.budget` / `.outerKinds`, `enemyTable`, `firstWorldBuffs` - every one
OFF by default. Measured, and load-bearing:

- **★ EVERY DROP GOES THROUGH `LootSelector.SelectLoot(DropTable, seed)`** - an enemy's or a
  crate's `LootDropper`, the Wheat plant's, and a cell's own drop table (gold in the rock,
  `LevelSegmentComponent`). So money drops is ONE postfix (`NgMoney`), and the source is the
  TABLE's name (`NgMath.MoneySource`: 44 tables name a money pickup - 34 enemies, `DropTable Unit
  Queen`, 7 crates / boxes, `CellType GoldDropTable`, `WheatDropTable`; the Queen's is checked
  before the units'). A money pickup is a `Prefab` drop whose `ResourcePickup.resource` is
  `Resource Money`. MORE PICKUPS: copies of the pile's own (10 / 20 / 50 kept), the fraction a chance
  seeded from the drop's own seed (`NgMath.MoneyExtra`).
- **★ MONEY PICKUPS COLLIDE WITH EACH OTHER (R21 part 4, 356).** They sit on physics layer 14,
  whose collision row includes 14 itself, and a drop makes every one at the same point. A Level 2
  crate drops 100 by itself, the Queen 200 (plus 25 of something else); at +500% that was 600 and
  1,200 overlapping bodies - seconds, then minutes, of freeze. So copies stop at
  `NgMath.MoneyCopyCap` (the pile's own size, at most 50) and the rest of the money - exactly what
  the missing copies carried - is paid in `NgMath.BigCoins`: copies of the game's biggest money
  prefab (its 50) with `ResourcePickup.amount` set and drawn bigger, one per value, made under an
  inactive `DontDestroyOnLoad` holder. A money pickup has no `SavableEntity` (the loot factory just
  Instantiates it), so nothing about the save sees them.
- **★ HOW A ROOM FILLS:** `EnemyGenerator.PlaceBasedOnEcosystem` - budget = radius x lerp(the
  ecosystem's CLOSE range, its FAR range, the room's `distanceFromCenter`) x the PoI multiplier,
  spent on a weighted draw that skips a kind costing more than is left or whose
  `minDistanceFromCenter` is past the room. A room draws from its biome's FIRST ecosystem only
  (`Biom.GetDominantEcosystem` ignores the position).
- **★ SO `NgWorld` CHANGES THE DATA, NOT THE CODE**, for the length of ONE world's
  `EnemyGenerator.PlaceEnemies` (prefix + FINALIZER): every ecosystem (from the game's own
  `LevelElementsCollection.allBioms` - data, no scene search) is snapshotted (close range, list,
  each kind's value and weight), adjusted, and put back exactly. **Dial A moves each ecosystem's
  CLOSE range toward its FAR range** (`NgMath.NearEnd`): `lerp(lerp(c, f, a), f, d) = lerp(c, f, d +
  a(1 - d))`, so every room moves the same share toward the outer budget and the rooms' own
  distance - which the kinds' lines read - is untouched (`ngmathprobe` proves the identity). **Dial
  B** scales each line (`NgMath.OuterLine`). **The table**: a kind opened in its OWN biome loses its
  line; a kind opened elsewhere is ADDED (`Distribution.Add`) with its HOME power and entity - home
  = the ecosystem where it weighs most - at the row's weight or its home weight, line 0, at the
  row's cost (`power`) or its home cost.
- **★ EVERY ENEMY IN THE GAME IS A KIND (his ask, 2026-10-06: "include all the enemies in the game
  in that list ... including the queen. Just set their weight to 0").** After the ecosystems' kinds,
  `NgWorld.AddEveryEnemy` adds every prefab in the game's savables list (`SavablesCollection` -
  what a load and the debug menu spawn from; via `ServiceLocator`, else
  `EntityGameObjectManager.savablesCollection`) that has `Enemy` AND an `AIAgent`: 66, so 41 with
  no home. A no-home kind's weight is 0 and its cost `NgMath.DefaultTablePower` (10) unless its row
  gives one; **a newcomer at weight 0 is not added** (said once). **The Queen is allowed now** -
  the parser's refusal is gone; `NgMath.IsQueen` only SAYS once a world that every Queen killed
  counts toward the 4 (raising that count is a later talk, no code). The page carries the 41 as
  `extra-enemies-data`, each with what in the game's assets names it (boss room, a minion gun, a
  miniboss / power-star / Crawler / Canari room, the night drop table, your drone gadgets, the
  Beacon item, or nothing but the savables list = debug-only).
- **Placement is synchronous on the main thread**: read in the IL (Cecil) - the level generator's
  `<GenerateLevel>` awaits `EntityGenerator.PlaceEntities`, whose state machine calls
  `Time.realtimeSinceStartup` then `PlaceEnemies` directly; neither switches to the thread pool
  (only `StationGenerator`, `PlantGenerator` tile plants and the save do).
- **No kind needs water or a wall**: the "swimmers" (Fish, Swimmer Maggot, Zipper, Zapper) are a
  movement style at gravity 0, and turrets have gravity 1.6 and fall to the floor - so a kind works
  in any biome.
- **★ THE FIRST-WORLD SWITCH: `NgRun.Ahead` (0 / 1) and `NgRun.RulesPlus = Plus + Ahead`.** Decided
  ONCE from the rules FILE when a run starts, carried by the jump payload (`ahead`), kept in the
  record (`ahead`; a record without it is 0). EVERY rule reads `RulesPlus` - NgRun's properties,
  stations, weapons, money, the dials, WORLD INFO, the victory screen's next world (`RulesPlus +
  1`); every TITLE keeps `Plus` (WORLD 1 CLEARED). `NgMath.WorldWithRules` names both.
- Tab rows: **Money drops** (on/off), **Rooms near the start a plus** / **Far-only enemies closer
  a plus** (0 - 100% in 5s, 0 = off, `NgRules.SetDial`), **Buffs from the first world** - all write
  the rules file. The table is the builder page's only (an 18 x 66 grid since the every-enemy change, from the page's own
  `ecosystems-data`, measured off the EcoSys assets).

### Stage 3's changes to New Game Plus (2026-10-12)

CONCEPT.md 3.6 / 3.7 found two places the stage 2 fire-rate buff never reached; his answers fixed
both, in `NgWeapons`:

- **★ A BEAM'S DAMAGE IS ITS GAP, NOT ITS FIRE RATE.** The 11 enemy beams fire 20-60 times a second,
  but `HitscanWeapon.CanDamage` hits a target at most once a `DamageRepeatDelay` (0.1-0.5 s on all
  11), so `FireRate *= m` changed no beam's damage while WORLD INFO showed the higher number. His
  answer, "Shorten the damage gap": the fire-rate block divides it too.
- **★ A SCRIPTED VOLLEY PRESSES `Shooter.Shoot()` AT ITS OWN PACE.** `ShootComplexAction` (Grouped
  or Once - the Queen's four bullet patterns, 12 crosses, the Fly Zapper) is a component on the
  enemy, its `fireRate` read live in `Update` and its `burstDelay` on every burst (the IL). So the
  fire-rate buff multiplies each pattern's `fireRate` and divides its `burstDelay`, ONCE per pattern
  object (`_paced`), at the decision and on every body that appears later (`NgSpawnPatch` ->
  `NgWeapons.OnSpawned` - a body that reloads is a NEW object). A Continuous pattern holds its
  shooters' triggers, so the gun's own rate already reaches it. The switch is
  `enemyWeapons.volleys` (rules version 6, ON by default), OFF in a world locked before it existed.
- **★ ENEMY TRAITS MOVES THE SAME TWO FIELDS EVERY FRAME, BY A RATIO** of what it last applied
  (`EtMath.Retarget`), so this one-time change survives whichever runs first. Never give either
  side a captured base for these fields.
- **`NgCarry` is contract 2**: `RulesPlus` (the plus this world's RULES are read at, one step on
  with the first-world switch), for Enemy Traits.
- **One marker**: `NgMarker.Attach` also skips an enemy with a child named `NgMarker` - Enemy Traits
  draws the same bubble under that name.
- **WORLD INFO and the victory screen ask Enemy Traits by type name** (`NgTraitsInfo` ->
  `GameModeForge.EnemyTraits.EtInfo.WorldLines()` / `NextLine(int)`); nothing without it.

### Stage 4's change to New Game Plus (2026-10-12)

- **`NgCarry` is contract 3**: `RunEnding`, raised by `NgVictory.EndRunConfirmed` BEFORE
  `DeleteSave` - the run still loaded - through the same one-listener-at-a-time `Raise` as the jump's
  events, so a listener that throws ends nothing but itself. The Leaderboard records END RUN there.
  A death needs nothing from New Game Plus: it is the game's own `GameController.GameOver`.

### The station unlock cost (`NgStations`, stage 2's third piece, 2026-10-05)

His go: *"go ahead and build the station unlock cost"*. The design is CONCEPT.md 2.3 (and the
safety net, section 7 answer 1); the build's choices are section 10. The rules are
`NgStationRules` (`stationCost`, rules file version 4), the arithmetic `NgMath.StationRise` /
`StationPrice`. What it rests on, measured:

- **★ THE GAME ALREADY HAS THE WHOLE MECHANISM, SET TO NOTHING.** Both station prefabs' one
  upgrade, `FuelDispenser`, costs 0 and rises 0. `Station.TryInstallUpgrade(upgrade, installer)`
  charges `CalculateUpgradeCost(upgrade)` out of the installer's tank and refuses when it cannot
  pay; `OnHoverStateChanged` writes the same number into the unlock prompt's `upgradePriceText`
  (active in both prefabs). So ONE postfix on the private `CalculateUpgradeCost` makes the price
  shown and the price charged ours - no UI of our own.
- **★ THE LADDER'S STEP IS OURS AND IN OUR RECORD** (`NgStations.Paid`, `stationsPaid` in
  `newgameplus.txt`): the game counts purchases in `RunData.purchasedStationUpgradeCounts`, which
  is NOT in RunData's memento, so a Continue would make the next station cheap again. It counts in
  the `TryInstallUpgrade` postfix only, so the start station (unlocked with `Station.Data.Install`
  directly by `StationGenerator.InitializeStations`) is never on the ladder. Both run entries reset
  it - the ladder starts again in every world.
- **The safety net** (off by default): the prefix marks the station free when you cannot pay AND
  your fuel is below `Ship.fuelCriticalThreshold` or a ship in `ShipManager.Ships` `IsDead`
  (unlocking any station resurrects a dead co-op ship - `ShipManager.OnUpgradeInstalled`). A free
  unlock still counts as a step. A Finalizer forgets the free mark if the unlock throws.
- **A refusal is said** in the installer's ship log (price and what you have), at most every 3 s.
- **★ INGREDIENT COSTS (his ask the same day, none by default):** Fiber, Ex, Chip, Bond... are the
  game's INGREDIENTS (`IngredientRegistry`: nine; Coral shows as "Fiber", Shell as "Generator" -
  `NgMath.KnownIngredients`, compared with the builder page's list), kept in the Vault and paid
  the shop's own way (`Vault.AmountOf` / `Vault.Remove`). The game's unlock takes ONE resource, so
  the ingredients are checked in a PREFIX on `TryInstallUpgrade` that returns false (and
  `__result = false`) when any is short - the game never takes the money - and taken in the
  postfix only once it unlocked, never when the net opened it free. The net counts the
  ingredients in "cannot afford". The prompt's one text (`PricePrompt`, a single TMP number) keeps
  the money; a postfix on `Station.OnHoverStateChanged` puts the whole price in the ship log
  (once per station per 6 s). An id the game does not know is said once and left out.
- **The summary keeps it out of the "enemies" lines:** `NgRules.WorldParts` - the victory screen
  gives each its own line without "enemies" in front; `Summary` joins it after a semicolon.

### Enemy weapon stats (`NgWeapons`, stage 2's second piece, 2026-10-05)

His go: *"go ahead and build the enemy weapon stats"*. The design is CONCEPT.md 2.5 and section 7;
the build's choices are section 10. The rules are `NgWeaponRules` / `NgStepBuff` in `NgRules.cs`
(the rules file's `enemyWeapons`, version 3): fireRate, reach, speed, push, burn, ignite (percent
buffs, `ignite` a flat burn) and projectiles, burst (whole numbers, `NgMath.StepValue`). All OFF by
default; coverage 100% except projectiles (25%, +25%). What it rests on, measured:

- **★ AN ENEMY'S GUN IS BUILT BEFORE IT IS DECIDED.** `ModuleSlotWeaponHolder` builds as the body
  binds (`OnGridOwnerBound` → `RefreshWeapon` → `RecreateWeapon` → `WeaponFactory.Create`), and 16
  enemies (the Queen, the cross and officer variants) also carry a `StaticWeaponHolder`, which
  builds ONCE, in `Start`. Both run as the body appears; the decision is NgBuffs' two-frame queue
  (the owner trap). So `NgWeapons.Decide` changes the LIVE guns in place (every `WeaponHolder` on the
  body, then `InitializeVisuals` - idempotent, measured: it adds or removes muzzle flashes and beams
  to match the count), and every later build is changed in a `WeaponFactory.Create` postfix inside a
  bracket on both holders (prefix sets whose holder it is, a Finalizer restores it). Each gun object
  is changed once (`ConditionalWeakTable`).
- **★ ONLY DEPTH 1.** `Create` builds a sub-emitter by calling itself; a prefix / Finalizer pair
  counts the depth, and only the outermost Create in the bracket is the holder's gun.
- **The queue runs for either buff.** `NgBuffs.OnAdded` / `OnLoaded` queue when health OR the gun
  stats are wanted; `Apply` decides the guns right after the owner check (yours never), then the
  health only if it is wanted. The health report stays quiet when health is off.
- **★ `knockbackForce` IS NOT A STAT: it is recoil on the shooter** (`Shooter` pushes its own
  body back by it). `pushForce` is the shove a hit gives - that one is `push`.
- **★ The AI empties an ammo tank, then waits for it to refill** (`ShootAction`), so fire rate
  also divides an ammo gun's `Cost` (10 enemy guns use ammo) and its `BurstDelay`.
- **Reach** lengthens whichever limit the gun has (range, lifetime, a beam's `Range`). The AI opens
  fire at its own `shootingDistance` (Max 100 on 36 of 61), which is not touched.
- **Burn vs ignite (his "Both, as two stats"):** 8 enemy guns burn (the lasers incl. the Queen's,
  the fire rockets, the fireball); `burn` multiplies theirs, `ignite` gives every other gun a burn
  of its own. **Extra projectiles fan out** `FanDegrees` (8°) apart on a gun with no spread - about
  two dozen fire one bullet straight, and the game spaces bullets `Spread / (count - 1)`.
- **★ Minion and mine launchers (`MinionSpawnerWeapon`) are never changed** (his "Leave them
  alone"): what they launch is an enemy with buffs of its own.
- **The summary is ONE part for the guns** ("with guns +20% fire rate, ..."): with all eight on,
  one line each would overflow the victory screen's 1,100 x 185 rules box. WORLD INFO lists them and
  counts the guns changed.
- `ngrulesprobe.py` (new) compiles against the BUILT NewGamePlus.dll and feeds the real parser
  every builder preset (no problems allowed), a round trip, a locked record and whole-number
  rounding. It needs the framework's `netstandard.dll` facade, because the game's Newtonsoft.Json is
  a netstandard 2.0 build.

### Health bars in layers (`NgLayers`, stage 2's first piece, 2026-10-05)

His R21 300 note (the Queens' bar at NEW GAME PLUS 2 took a third of the screen) and his answers
(CONCEPT.md section 7, answer 7, and "Own colour last"): a buffed enemy's bar stays its normal
size, the extra health stacked in layers of that size; the last layer is the game's own bar and
colour, the ones above go green, yellow, orange, red, purple, then round again; the partial layer is
the top one; an `xN` counts the layers left. The arithmetic is `NgMath` (`LayerCount`,
`LayerIndex`, `InLayer`, `LayerColourSlot`, `LayerCountText`), run by `ngmathprobe.py`. What it
rests on, measured off the decompile, the prefabs and Game.unity:

- **★ NEITHER BAR IS REDRAWN: THEY ARE HANDED A STAND-IN TANK.** `ResourceBar` (an enemy's, one
  segment per point, rows of `HealthbarOwner.maxUnitsPerRow`) and `BossHealthbar` (the Queens', 7.5
  px a point, rows of 200 - a Queen has 1,600) draw whatever `ResourceTank` they are given, and
  `ResourceTank` is a plain class with a public setter for `Capacity` and `Value`. A prefix on each
  bar's `Assign` swaps in a stand-in whose capacity is one layer; a prefix on each bar's `Update`
  sets its value to the health inside the current layer just before the bar reads it. **The real
  tank is never written** (pinned).
- **★ WRAP AT ASSIGN, LAYER WHEN THE BUFF LANDS.** The enemy is buffed two frames after it appears
  (the owner trap), and a hurt restored enemy gets its bar at once, so every enemy health bar is
  wrapped while health is buffed this world and the stand-in mirrors the real tank until
  `NgBuffs.IsBuffed`. One layer = capacity / the world's health multiplier (the co-op normal in
  co-op). The switch (`[Display] HealthBarLayers`, default on) is read in the per-frame sync, so it
  flips both ways mid-fight.
- **Which bar:** `HealthbarWidget.GenerateResourceBars` is bracketed (prefix sets
  `NgLayers.Generating`, a Finalizer clears it) so the `Assign` prefix knows the owner; only the
  tank of the owner's `DamagableResource.resource` is wrapped - shields keep theirs - never a
  `ShowAsBoss` owner's widget, never anything a ship owns. The Queen comes from
  `BossStateManager.CurrentBoss` (via `ServiceLocator`), checked against the tank the manager
  handed over (`HealthbarManager.resourceToDisplayForBoss` is Resource Health, which is hers).
- **★ THE EMPTY HALF OF A SEGMENT IS FAINT.** Every resource's bar texture is two halves: solid
  (full) at the bottom, a faint outline or a 20% block (empty) at the top - and the game's full and
  empty COLOURS are the same (red/red, orange/orange...). So "the rest of the bar is the next
  layer" needs a texture whose top half is solid: `Graphics.CopyTexture` copies the stock texture,
  then its bottom half over its top half - **a GPU copy, because the game's textures are not
  readable** (no `GetPixels`, pinned). The shader's `_ResourceColorEmpty` is the next layer's
  colour. The last layer gets the stock texture and colours back. If the copy cannot be made, the
  stock texture stays and the log says so once.
- **★ THE 1.5 SECONDS AFTER A SEGMENT CHANGES ARE DRAWN BY `ResourceUnit`s** (the white hit flash,
  then the empty sprite fading in), and the shader masks that stretch (`_MaskRange`). `Setup` gives
  them the resource's colours, so `ResourceBarRow.CreateUnit` is postfixed to repaint each one, and
  its empty sprite becomes the full sprite (solid) in the next layer's colour. Their clips
  (`HUD_resourceUnit_getEmpty` / `getFull`) animate only `m_Color.a` and a position, so the RGB
  paint holds - write RGB, keep the alpha (pinned).
- **★ THE QUEEN'S ROW IS A FILL OVER A GREY `Empty` IMAGE, with a white `Delta` strip between.**
  A layer above the last turns `Empty` into the next layer's colour. When a layer breaks, `delta`
  is set to the new value - otherwise the strip would sit at the old layer's bottom and creep up at
  20 a second (80 s for a Queen) before showing any damage again. `HealthbarManager` re-assigns on
  every boss-state entry (its tank is never our stand-in), and our own re-assign (when the layer
  size changes) is recognised and let through.
- **The `xN`** is a TextMeshProUGUI made by type name (the house idiom), in the font of a live
  `InGameHUD` text, beside each bar's top right; hidden on the last layer.
- **★ Its size is a setting (R21 part 2, his note: "the 2x bubble is pretty big, can you shrink it
  down to 1/4 the size? and maybe add a option for the player to change the scale").**
  `[Display] HealthBarCountSize`, a percent, 100 by default, 0 hides it; the tab's **Layer count
  size** slider steps it 0-400 by 25. The label is still MADE at its first size and scaled by
  `localScale` (pivot on the bar's corner): an enemy's at `NgMath.EnemyCountBase` (0.25 - so 100%
  is a quarter and 400% the first size), the Queen's at `BossCountBase` (1). Each view remembers the
  percent it was sized for (`LabelSized`); both syncs compare it to the live setting each frame, so
  a change applies at once, and a size raised from 0 makes the label it never made.
- **The colours are a setting (his ask the same day).** `[Display] HealthBarPalette` (enum
  `NgLayerPalette`: Classic, ColourSafe, Ice, Fire, Neon, Custom - in `NgMath.PaletteNames`
  order, pinned, because the tab's **Layer colours** slider steps by number) and
  `HealthBarLayerColours` (Custom's list: names or hex, up to 12, parsed by `NgMath.ParseColourList`).
  The palettes are hex in `NgMath.PaletteHex` and the builder page carries the same JSON
  (`palettes-data`, compared exactly). Resolved only when either setting changes; a version number
  makes every layered bar repaint at once. **A colour too like the one under it is skipped**
  (`NgMath.LayerSlot`, distance < 0.2 in 0-1 RGB) - a Caps drone's own orange under COLOUR-SAFE's
  orange, or a custom "red" over a red bar, would read as one bar. The last layer is always the
  enemy's own colour, whatever the palette (his "Keep own colour last").
- `ngpatchprobe.py` checks all six patch targets (two private, two matched by a `ref` parameter's
  NAME, `resourceTank`) against the game's DLL.

### Enemy damage % (`NgDamage`, added to stage 1 on 2026-10-02)

On every enemy, at everything it hits; the world's hazards too while `environment` is on; never
anything of yours. The design is `Docs/CONCEPT.md` 2.5, and the build's choices are in section 9.
The traps it rests on:

- **★ ONE MULTIPLY, AT TakeDamage.** Each of the six `HealthBase` entry points (plus the slime
  cell and the burn tick) decides whose hit it is and opens a scope. The victim's `TakeDamage`
  (`Damage(float)` for a burn tick) uses it ONCE and marks it used, because a shielded
  `TakeDamage` calls itself again per damage, and `Damage` follows every `TakeDamage`. The scope
  also checks the victim. Never write a shot's `Damage`: Weapon Forge's damage stack owns that
  field.
- **★ AN EXPLOSION HANDS EVERY VICTIM THE SAME LIST.** Scale it into a NEW list. Scaling it in
  place compounds, victim after victim.
- **★ 13 `ExplosionComponent` prefabs explode with NO OWNER**: death blasts, six ammo blasts and
  six plant blasts. So they are sorted as they are made. `Awake` runs inside the `Instantiate`,
  therefore inside the `Die()` or the shot method that made it, and the innermost maker wins. The
  answer is used in `Start`, a frame later. The makers are the two `Die`s and the shot methods that
  `Instantiate` effects: `Projectile.HandleRange` / `HandleLifetime` / `OnObjectHit` and
  `PhysicsProjectile.HandleLifetime` / `Impact`.
- **★ A BURN TICK CARRIES NO SOURCE.** Each victim keeps who lit it LAST (`ApplyBurn`, called
  right after the base entry returns, or the steam cell). The tick takes that side.
- **★ A KNOCKED-BACK BULLET KEEPS ITS BONUS**: an enemy's shot is stamped at `Shoot`. Weapon
  Forge's reflectors rewrite owner, layer and mask, so the hit-time owner says "yours". Re-decide
  on EVERY `Shoot`, because a lobbed shot's first `Shoot` is a blank one.
- **★ STATIC INITIALIZERS RUN IN WRITTEN ORDER.** `_missingFields` sits above the `FieldRef`s that
  append to it. Below them it is null when a ref fails, and the throw takes down the whole class
  (pinned).
- **Unknown = untouched**, said once per kind per world and counted in WORLD INFO. Every patch is
  gated on `NgDamage.Active`, false in a world with no enemy damage (the first world, by default),
  so most of the time the hot-path patches (`DamagableResource.Update`, the shot methods) return
  at once.
- **A saved world's rules are parsed LOCKED** (`Parse(text, problems, true)`): a buff the record
  does not name is OFF, so a world locked before a buff existed keeps its rules. Only the FILE
  gives a new buff its default.

## Enemy Traits — the fifth DLL (built 2026-10-12)

New Game Plus stage 3: things enemies can do that make a world harder - batch 1 is **wind-up**,
**grow**, **overload** and **ricochet**. The design and every answer are `Docs/CONCEPT.md` 3.1-3.7;
what the build chose is section 11. Pinned by `enemytraitstest.py`; the arithmetic is run by
`etmathprobe.py`, the rules file and the page's presets by `etrulesprobe.py`, every patch and
private member against the game by `etpatchprobe.py` (+ `etreflectprobe.cs`).

**Files:** `EnemyTraitsPlugin` (the gate), `EtMath` (every number; no Unity), `EtRules` (the rules
JSON: every number an `EtNum` with its per-plus box), `EtRun` (the world's locked rules, the record,
`EtNow` = the rules at this world's plus), `EtDecide` (the queue and the roll), `EtUnit` (one
enemy's meters, rewards and paces), `EtRewards` + `EtWeaponStats` (Module Forge's reward set and
stat table, copied, with the bridge), `EtShots` (grow and ricochet), `EtMarker` (+ `EtGauge`),
`EtTab`, `EtInfo`, `EtInterop`, `EtSettings`, `EtLog` (NgLog, renamed - pinned equal).

- **★ A TRAIT BELONGS TO THE ENEMY UNIT, NEVER THE WEAPON ASSET** - one WeaponData and one ammo
  prefab serve every enemy of a kind. Decided by instance id in `EtDecide` (New Game Plus's two-frame
  queue, copied: the owner trap, crates have no AIAgent, an enemy destroyed while queued is skipped)
  and worn on the body by an `EtUnit` - added at the decision, or when the body appears
  (`SpawnObjectForEntity`; a body that reloads is a NEW object and is dressed again).
- **★ A SHOT TRAIT BELONGS TO THE SHOT** (3.4: a knocked-back bullet keeps swelling and bouncing):
  an `EtShot` component stamped once at `Shoot`, never looked up through the owner again. Shots are
  never pooled (measured), so nothing is cleaned up.
- **★ THE PLUS IS NEW GAME PLUS'S RULES PLUS**, `NgCarry.RulesPlus` by type name (contract 2) - never
  the title plus; an older New Game Plus without it is refused and said. Without New Game Plus every
  world is plus 0. Asked lazily (`EtRun.Now`, cached per plus) - two frames into a world, long after
  both mods' run entry - and NEVER on a hot path (`EtRun.Cached`).
- **★ LOCKED PER WORLD**: the file is read at `RunData.Initialize` (a new run, every jump); a
  Continue reads `enemytraits.txt` from the save folder (Load prefix, reset first; written in a
  Save postfix after `CreateDirectory`). Locked: a trait the record does not name is OFF. Its own
  roll seed, in the record.
- **★ WHAT "FIRING" IS (3.7, "Pattern = firing")**: a held trigger as the game defines it (down,
  the owner can pay, no blocker) OR a running volley pattern (`ShootComplexAction` enabled, not
  Continuous, past its own `delay` from `enterTime` - gaps included). Volleys "off" counts held fire
  only. **One meter per enemy** (3.6): every gun on the body feeds it and takes the reward.
- **★ THE REWARDS ARE PUBLISHED, NEVER WRITTEN** (`EtWeaponStats`): into Weapon Forge's
  `ForgeWeaponStats` when installed, else Module Forge's `ModuleForgeWeaponStats`, else our copy -
  because their `slow` already owns a slowed enemy's fire rate, and two tables writing one field
  ratchet. A table that is there but unreachable means NO reward (never a second table). Nothing is
  published in a body's first 0.3 s (New Game Plus writes the same gun's fire rate at two frames).
  Minion / mine launchers are left alone. A rebuilt gun, and a body that goes, hand their guns back.
- **★ TWO PACES NO TABLE KNOWS, BY RATIO** (`EtMath.Retarget`): a beam's `DamageRepeatDelay` /
  the fire-rate reward (3.7: a beam's damage is its gap), and - on volleys "speed" only - a volley
  pattern's `fireRate` x it and `burstDelay` / it. A ratio of what Enemy Traits last applied keeps New
  Game Plus's one-time change to the same fields in either order.
- **★ A BULLET'S BOUNCE SETTINGS ARE SHARED.** `ProjectileBounceData` is a CLASS handed by reference
  WeaponData -> gun -> every bullet, so the ricochet gives each bullet a NEW instance (terrain only);
  out of bounces, a fresh "off" one. Writing `enabled` on the one it came with would make every
  bullet of that kind bounce. Counted in an `OnObjectHit` prefix that returns at once while no
  stamped shot is alive (`LiveRicochet`, floored at 0).
- **★ ROCKETS: ONLY TERRAIN IS A BOUNCE, AND A BOUNCE DROPS THE LOCK** (3.7). The shot's own
  impact (a struct - its own copy) is switched off so the physics bounces it; on anything that is not
  terrain, or with no bounces left, it is put back ON in a PREFIX on the game's own
  `PhysicsProjectile.OnCollisionEnter2D`, so the game's handler ends the shot exactly as it always
  would (the bounce is that method's postfix). **★ Never a collision message of our own** (the first
  build's, fixed in the audit): two components on one object hear a collision in no promised order,
  and the game's handler reads the impact switch - had it heard the contact after ours switched the
  impact back on and ran it, the rocket would have gone off twice. A physics shot GROWS over its
  lifetime (its only bound); "toward you" re-aims a bullet only off the open side of the wall.
  13 enemy kinds lock their rockets onto you (`HomingTargetFromAIAgent`), and an unlocked rocket
  steers at a point 10,000 units along its launch - so a bounce turns homing off, drops the target and
  moves that point along the rebound (nose too), or it would turn straight back into the wall. With
  "toward you" it locks onto the nearest ship the owner is an enemy of (never for a shot that is
  yours now, or a charmed enemy's). A rocket keeps its speed (a mirror); a lobbed shot comes off at
  `bounciness`. The drifting mines (`Ammo FlyAlfa`: impact already off) and the two bouncing bullets
  keep their own.
- **Grow** is Module Forge's: `Radius` from the captured base (the real hitbox), the transform for
  the art; size only.
- **★ NEVER A HUD OR SHIP-LOG LINE FOR AN ENEMY, AND NO TELL** (3.7, his "Nothing"): only the
  opt-in marker (New Game Plus's bubble, child `NgMarker` - one marker whichever DLL draws it) and
  the opt-in overload gauge (`[Display] OverloadGauge`, read live). Pinned: no `LogOutput`, `ShipHud`
  or tint in this DLL.
- **Without New Game Plus and with the defaults there are no traits**, and the log
  (`GameController.StartGame` postfix) and the tab's trait rows say so - the FLAT preset is the
  answer.
- **Tab rows** (`EtTab`, the NgTab contract): one per trait, the volleys (off / count / speed), the
  limit - all written to the rules file, applying from the next world, every other key kept - and the
  marker and gauge (the cfg, at once).

## Leaderboard — the sixth DLL (built 2026-10-12)

New Game Plus stage 4: a local board of finished runs. The design and his four answers are
`Docs/CONCEPT.md` 5 / 5.1; what the build chose is section 12. Pinned by `leaderboardtest.py`; the
ranking, the tally and every word the board draws by `lbmathprobe.py`, the file by `lbstoreprobe.py`
(the BUILT DLL, in a temp folder), every patch and member by `lbpatchprobe.py` (+ `lbreflectprobe.cs`).

**Files:** `LeaderboardPlugin` (the gate; hears `GameController.GameOver`), `LbMath` (the row type
`LbEntry`, the ranking, the tally, every word; no Unity, no Newtonsoft), `LbStore` (the board's
file), `LbRun` (one run: the chain tally, the save-folder record, the two ends), `LbCarry` (New
Game Plus's contract, by type name), `LbMods` (which Forge mods were running), `LbMenu` (the
LEADERBOARD button and the board), `LbUi`, `LbTab`, `LbSettings`, `LbInterop`, `LbLog` (EtLog,
renamed - pinned equal).

- **★ THE GAME'S OWN BOARD IS SWITCHED OFF, AND NEVER TOUCHED.** `LeaderboardScoreSubmitter`'s object
  (`GameController/ScoreSubmitter`) is inactive in Game.unity, `HighscoresScreen` is not in the
  build, and the main menu's "view highscores" button sits in an inactive footer calling a
  `MainMenu.ShowLeaderboard` that no longer exists. Nothing here names `PlatformFeatures`,
  `LeaderboardScoreSubmitter` or `GameWon` (pinned).
- **★ TWO ENDS, NOT FOUR** (his answer: "Death + END RUN"). Every end deletes the save
  (`GameSaver.DeleteSave`), but only a death and END RUN make a row. A death is the game's own
  `GameController.GameOver`, subscribed (no patch) - raised after `RegisterDeath` and `DeleteSave`
  in the same method, the run still loaded (IL-checked by `lbreflectprobe.cs`). END RUN is New Game
  Plus's `NgCarry.RunEnding` (contract 3), raised before its `DeleteSave`. **The GameOver handler
  never throws**: the game-over screen listens to the same multicast delegate, and a throw would
  stop it. Nothing patches the pause Restart, the main menu's New Game or `DeleteSave`.
- **★ A ROW IS THE WHOLE CHAIN, WHATEVER THE "TOTALS" CARRY SWITCH SAYS.** `LbRun` keeps its own
  tally: `NgCarry.Leaving` HOLDS what the old world added (its totals less what it started from);
  the new world's run entry folds it in only while `NgCarry.Carrying` (a jump that never arrives
  leaves the old world counting the ordinary way); the new world's start is measured at
  `NgCarry.Arrived` - New Game Plus carries the run record BEFORE raising it (`NgJump.Flush`) - with
  `StartGame` as the backstop. `LbMath.Added` is never negative. A Continue with no record (a save
  from before the install) counts the game's totals as they are.
- **★ ITS OWN RECORD IN THE SAVE FOLDER** (`leaderboard.txt`): the tally, the start, the loadout
  (`RunArguments.startingLoadout` is empty on a Continue). Written in a `Save(string)` postfix after
  `CreateDirectory`; reset in the Load PREFIX and read in its POSTFIX (nothing about enemies hangs
  on it); `RunData.Initialize` leaves a Continue to the Load patches.
- **★ THE BOARD'S FILE NEVER LOSES A ROW.** `BepInEx\config\...leaderboard.runs.json` - outside the
  save folder, which every end deletes. Written to `.tmp` and swapped in (`File.Replace`). A file
  that does not read - not JSON, an unreadable row, a newer version - is renamed aside
  (`.bad-<when>`, never deleted) and a new board begins with the rows that did read; a lone `.tmp`
  is taken back. Only a finished run writes; the screen only reads.
- **★ WHICH MODS WERE RUNNING, NOT INSTALLED.** A switched-off DLL is still loaded. Each mod's own
  `[General] Enabled` is read ONCE, at this plugin's Awake, as TEXT (`LbMath.EnabledInCfg` - binding an
  entry on another mod's file can write it), and counts only with the mod's type loaded; New Game
  Plus answers for itself (`NgCarry.Enabled`).
- **★ THE BUTTON IS A COPY OF SETTINGS, AND IT FOLLOWS SETTINGS.** `Footer v2` is a
  `HorizontalLayoutGroup` that does not size its children (widened for four), and its navigation is
  explicit (threaded both ways). Each footer button is an `AnimatedScreenElement` whose `Visible`
  the menu's `AnimatedScreen` sets from a list it collects ONCE, in Awake - and an element under an
  AnimatedScreen never shows itself - so the copy mirrors SETTINGS' `Visible` every frame
  (`LbFollowVisible`; `Show()` once if the animators cannot be read).
- **★ THREE THINGS THE MAIN MENU DOES TO A SCREEN OVER IT.** Esc opens "quit the game?"
  (`MainMenu.Update` checks only `optionsScreen`) -> the menu's Update is skipped while the board is
  up and for 0.4 s after, and the board reads `exitAction` itself. The game's `UiManager` pulls a
  pad's selection back to the TOP `DefaultSelector`'s default -> the board carries its own, added
  while the board is still asleep so it knows its default before it pushes. A pad could reach a
  button behind -> the scrollbar and BACK point only at each other (the scrollbar, with nothing above
  or below it, moves itself on up / down - Unity's own `Scrollbar.OnMove`).
- **★ THE MAIN MENU'S FONT (Font_Minimum) LACKS `#`, `%`, `&`, `<`, `>`, `@`, `[`, `]`, `^`, `{`, `}`,
  `~` AND THE BACKTICK.** Every word the board draws comes from `LbMath`, and `lbmathprobe.py` fails
  on any of them outside a tag; a loadout name (Weapon Forge's are typed by the player) goes through
  `LbMath.Clean`. A rank is "1.", never "#1".
- **"Record runs" is read when a run ends** (`[Leaderboard] RecordRuns`, bound above the gate; the
  tab's one row, through the `ForgeTabRows` contract).

## Salvage Shop — the fourth DLL (built 2026-10-02)

The design is `Docs/SALVAGE SHOP - CONCEPT.md` (section 19 the build, section 21 what it chose);
this is what the code rests on. Every item is pinned by `salvagetest.py`, the arithmetic is compiled
and run by `salvagemathprobe.py`, and **every Harmony patch target is checked against the game's
own Punk.Main.dll by `salvagepatchprobe.py`** (a wrong one throws at PatchAll and the mod does
nothing for a launch). All three were mutation-tested: 12 code / arithmetic breaks and 4 patch-target
breaks, every one caught; the audit's three fixes added 9 more breaks, all caught.

**⚠ Edit these files with the Edit tool or Python, never PowerShell `Get-Content` / `Set-Content`.**
The one PowerShell edit of the build read `SalvageCatalog.cs` as ANSI and wrote it back as UTF-8
with a BOM: four `★`s became `â˜…`. `salvagetest.py` now fails any `SalvageShop/*.cs` with a BOM or
mojibake. The files are LF throughout (Git Bash's `grep -c $'\r$'` misreports them as CRLF).

**Files:** `SalvageShopPlugin` (the gate), `SalvageMath` (every number; no Unity),
`SalvageRules` (the rules JSON), `SalvageCatalog` (what a module is, what it is offered, the words),
`SalvageRecords` (one upgrade per Module instance), `SalvageApply` (where each upgrade is applied),
`SalvageAddOns` (the safe explosion / spark / burn add-ons), `SalvageSave` (the record, the run-entry
reset, the New Game Plus carry), `SalvagePanel` + `SalvageSlot` + `SalvageScreen` (the SALVAGE tab),
`SalvageCard`, `SalvageTab` (the GMF tab rows), `SalvageCheck`, `SalvageUi`, `SalvageLog`.

- **★ A GUN IS UPGRADED WHERE THE GAME BUILDS IT.** A postfix on `WeaponFactory.Create`, while
  `ModuleSlotWeaponHolder.RecreateWeapon` or `WeaponBasedActiveModule.OnContainingClusterRefreshed`
  has pushed the module it is building for (popped in a FINALIZER). That is where the game applies
  its own weapon modules, and it is before Weapon Forge captures any base, so nothing downstream can
  tell an upgraded gun from a stock one. Matched against the module's **live** `WeaponData`
  (Weapon Forge's rank swaps it for a clone in a prefix of its own), and only for that data, so a
  sub-emitter's nested Create never takes the parent's upgrade.
- **★ THE SLOT HOLDS A REFERENCE.** The module never leaves the grid or the vault while it is in the
  slot - so there is no Install / Uninstall / Vault.Store anywhere in the mod (pinned), closing the
  menu can lose nothing, and "it goes back where it came from" is true by construction. Scrapping
  (`Vault.Remove`, after `MarkModuleSeen` - or the vault's "new" badge stays lit) is the only thing
  that takes one away.
- **★ THE GAME'S DROP TARGETS ARE HARD-CODED.** `ModuleGridInput.OnMouseMoved` tests vault, shop,
  then defaults to the grid; a prefix makes the SALVAGE **panel** (all of it, not just the slot) the
  active widget under the mouse. Over the panel's background the grid would otherwise be active and
  a click would pick up a grid module drawn underneath. `ModuleGridScreen.OnModuleDropped` already
  cleans up for an unknown target whose `CanMoveTo` says yes, so its prefix only fills the slot.
  A pad never drops anywhere but the grid, so A has its own path (`OnConfirmPressed`).
- **★ THE ADD-ON COST TRAP.** `ProjectileWeapon.FireSingle` checks `GetResource(res) >= cost` and
  then does `GetTank(res).Value -= cost`. A ship with no tank of that resource passes the check at
  a cost of 0 and the subtraction dereferences null, in the game's own fire code, every shot. A
  salvage explosion or spark costs 0 by default and reduce cost can take a stock add-on to 0, so
  `SalvageExplosion` / `SalvageSpark` / `SalvageBurn` (subclasses of the game's own three) re-implement
  `IHasPerProjectileCost` to tell the game "0 on HEALTH" and pay their real cost in `ModifyProjectile`,
  tank checked first. They stay the game's types, so the card's explosion lines count them.
- **★ THE SAME TRAP ON A GADGET (found in the audit, 2026-10-02).** `WeaponBasedActiveModule
  .Activate` checks `GetResource < ActivationCost`, FIRES, then `GetTank(res).Value -= cost` with
  no `HasTank`. At a cost of 0 on a ship without that tank the throw lands after the shot and
  before `ModuleActivator` stamps `lastUseTime`, so a held button fires every frame. The gun's own
  cost is safe (`Shooter` checks `HasTank`). So reduce cost on a gadget goes through
  `SalvageMath.ActivationCostAfter`, which stops at `GadgetCostFloor` (0.001), never 0.
- **★ A BUTTON ON THE PANEL EATS THE DROP (found in the audit).** The game drops a carried module in
  `ModuleGridScreen.OnPointerUp`, and Unity sends the pointer-up to whatever took the pointer-down;
  a `Selectable` under the pointer takes it. So every clickable on the panel (both mode buttons, the
  stat lines) has a `CanvasGroup` whose `blocksRaycasts` is off while
  `input.IsMovingModule` (`SalvagePanel.Update`), and the click falls through to the background.
  Anything clickable added to the panel must go through `TakesClicks` (pinned by count).
- **★ THE LIST SHOWED FIVE ROWS AND A MOUSE COULD NOT REACH A SIXTH (R21).** Only the pad's
  up / down moved the window. UPGRADE shows `UpgradeRows` (9 - every weapon stat), SALVAGE shows
  one and is shorter (more vault under it); `Layout()` sizes the panel, hides the rows past the
  mode's count (a hidden row must not take a click) and moves the footer, on open and on every
  mode change. `SalvageScroll` (an `IScrollHandler` on the panel root - a wheel event climbs to the
  first handler above whatever is under the pointer) moves the VIEW (`_top`), never the pick.
- **★ `Clone()` DROPS `Module`**, and every add-on's damage reads `Module.Level`: each copy gets the
  original's module.
- **★ A GADGET'S COOLDOWN IS ITS DATA'S**, copied into the instance's `ActiveModule.Cooldown` /
  `ActivationCost`, which nothing ever resets: written from the data every time (idempotent).
- **★ A LEVEL IS BaseLevel AND Level.** The grid recomputes `Level = BaseLevel + boost` only for a
  boostable module and only on a change. A level step only moves a module whose effect series scale
  (read by reflection off its own effects - Burst and ExtraProjectile are flat).
- **★ THE SAVE KEEPS NONE OF IT** (no level, effects rebuilt from the asset). `salvage.txt` in the
  save folder, one line per item keyed `grid:x,y` (+ ship id) / `vault:i` and checked by module id;
  read in a Load POSTFIX (the reset is a PREFIX on Load and on RunData.Initialize), written in a
  Save(string) postfix after `Directory.CreateDirectory` (the game makes the folder on a worker
  thread), deleted when empty. After a load every ship grid is rebuilt through the game's private
  `ModuleGrid.OnModulesChanged`, because a gadget built its gun before the record was read.
- **New Game Plus** carries it through the same `NgCarry` contract Weapon Forge's rank uses
  (`Leaving` takes the save's own lines, `Arrived` attaches them with ship ids mapped).
- **An upgrade bought shows** through `ModuleGrid.OnModulesChanged` (guns rebuilt, levels recomputed,
  the grid widget told) and the owner's `modulesChanged` (stats on its next update); a vault gun's
  card numbers through rebuilding its private `baseWeapon` under the same build hook; the card itself
  through hiding it and calling the screen's private `UpdateHoveredModuleIndfo` (it early-returns for
  the module it already shows).
- **A bad rules file keeps the LAST GOOD rules** (salvage has upgrades already bought that must keep
  working - the opposite call to New Game Plus, which adds nothing when in doubt), said loudly in the
  log and on the screen.
- **★ THE GAME'S TIMEOUT FLAGS SWITCH A LIFETIME ON (R21 part 3, 325 / 329).** *"Leveling up spark
  ... no longer makes a projectile and just spawns a spark right next to the player"*, *"Blade
  explodes instantly next to the player. Same with Popper. Works fine with missiles."*
  `AddExplosionEffect.addTimeoutExplosion` and `AddDischargeEffect.timeout` do not just say "go off
  when the shot times out": `ModifyProjectile` sets `LifetimeData.enabled = true`. Popper, Blades,
  Worm, Shotgun, DiscGun and Bolt ship `lifetimeData.enabled 0, time 0` (they end by RANGE), so the
  shot ended on its first `Update` (`Time.time > shotTime + 0`) at the muzzle - a spark beside you,
  or an explosion that reached you once its radius grew (about step 3). Rockets (time 10) and
  Debris (time 2) had a lifetime already. Salvage's own add-ons never set either flag now;
  `SalvageAddOns.OnTimedExpiry` adds the expiry blast only to a shot whose lifetime is ALREADY on,
  which is what 16.3 decided. The harness had pinned `addTimeoutExplosion = true` - **pin what the
  code DOES, not the line it contains** (Weapon Forge's `config.Get` lesson, again).
- **QUICK SALVAGE IS GONE (R21 part 3, his call on 327).** A quick scrap ran inside the drop, took
  the item out of the vault, and left it in the mouse's hand - so it could be put back on the grid
  and the Ex kept. Every scrap asks now; a confirmed scrap (`Scrap`) is the only way an item leaves.
  Pad X on the SALVAGE tab takes the module out (keyboard X already did). An old `QuickSalvage` line
  in a .cfg is ignored.
- **THE PANEL'S FRAMES ARE CLOSED (325).** The mode buttons, the slot and the panel were drawn with
  the TAB sprites (`MainMenu_placeholders_11` / `_12`: no bottom edge, measured off the atlas). The
  buttons use the game's BUTTON sprite (`_3`, every menu ButtonBody), the slot and panel the VAULT's
  frame (`_10`); found by name (`SalvageUi.FindSprite`), the tab sprites kept as the fallback. The
  mode in use is lit and the other dimmed, rather than swapping sprites.
  **★ AND IN R21 PART 4 (368) THE BUTTONS STILL HAD NO BOTTOM LINE**, with the closed sprite in
  place. They were still COPIES of the VAULT tab - its `AnimatedButton` animator (ToggleOn moves and
  resizes `ButtonBody` and swaps its sprite) and its tab layout came along. Not sourced by reading;
  the copy was removed instead: the two mode buttons are built FRESH (an Image with the button frame,
  a `Button`, the tab text copied for its font), and the log names which frame they got.
- **Static initializers:** every `FieldRefAccess` goes through a helper that catches, never a bare
  static field initializer (the NgDamage `_missingFields` lesson: a throw there takes the class down).

## What this mod is

The fourth Forge mod. Weapon Forge and Module Forge are **per-file** mods: a weapon or a module
describes itself, and nothing is true of the world unless something you are carrying says so.
**A game mode is the opposite — a rule about the run.**

## ★ THE ONE PIECE OF ARCHITECTURE THAT CANNOT BE RETROFITTED

**"Does this rule exist" and "should it act right now" are two questions, and the second one needs
an owner.** This project has already paid for conflating them once:

`contactDamage`'s world modes armed **every unit in every run for a whole session because a file
existed in the folder**. `Register` ran once per weapon file at startup and latched a static. The
fix was to split the two questions apart. A mod whose entire purpose is world rules needs that
split from the first line, not retrofitted — so it is the first line.

`ForgeModeRegistry` holds two collections whose reset behaviour is **opposite**:

| | what it is | reset? |
|---|---|---|
| `_modes` | which rules EXIST — load time | **never.** Clearing it unregisters the mod for the session |
| `_live` | which rules are IN FORCE this run | **always**, at both run-entry points |

The "never" half is not a style preference — it is the exact trap documented on
`ForgeCrit.Reset()` in Weapon Forge, which deliberately does not clear its weapon table because
those configs are registered once at startup from the weapon files.

`_inRun` is false between runs, so **"no rule is in force" is a state this mod can be in**, rather
than rules being on merely because they exist.

### An unknown mode answers NO, and which way that goes is not a house style

`IsLive` answers **false** for an unregistered id. This mod can only ever *add* rules to a game
that works without them, so every uncertainty resolves to "do nothing."

Compare Module Forge's powered-module gate, which resolves every uncertainty to **live** — because
that gate can only ever *take a working module away* from someone. **Which way an unknown resolves
is decided by what the code does with the answer.** Copying the other mod's default without asking
that question is how a safe-looking default becomes the bug.

## House rules inherited from the other three repos

These are not up for rediscussion; they are why the mods work together.

- **Find other mods by TYPE NAME ONLY** — never an assembly reference, never `[BepInDependency]`,
  never a plugin GUID. This mod must build and run with all the others absent.
- **Find types QUIETLY.** `AccessTools.TypeByName` logs a warning when a type is absent, and "the
  other mod is not installed" is a completely normal state. Scan
  `AppDomain.CurrentDomain.GetAssemblies()` — `ForgeInterop.FindTypeQuietly`.
- **Latch a cross-mod lookup on SUCCESS ONLY.** Load order is not guaranteed, so "the type is not
  there" has a different answer depending on when it is asked. A failure is retried once per run.
- **If reflection fails, do nothing.** Never risk two mods fighting over the same patch.
- **Exactly one owner per shared mechanic.** Harder here than for the existing pair — see the open
  questions.
- **A bad config must never crash the game.** Skip it, log the reason, carry on.
- **Diagnostic code must not throw.** Weapon Forge lost five test rounds to a warning-only read
  that threw. `ForgeDiagnosticGates` catches and returns.
- **Do not commit game code.** The findings here came from reading a local decompile of the game's
  assemblies. That source is not in this repo and must not be added — referring to type and member
  *names* is what makes collaboration possible; pasting method bodies in is not.
- **This repo is PUBLIC.** No absolute paths, no real names, no employer. `<PathMap>` stays in the
  csproj — without it the DLL embeds the builder's Windows username, readable with `strings`.
- **Copy the DLL after every build**: repo root, plus a dated copy in `MODS - Versions/` stamped
  from the DLL's own mtime.

## The two run-entry seams

`ForgeModeResetPatch` prefixes both, and they are the only "a run is starting" signal there is:

```
new run   -> RunData.Initialize
continue  -> GameSaver.Load
```

**ONE list, two entry points.** Both twins held their reset list twice, byte for byte, one copy per
patch — and anything added to a duplicated list is one paste from being half-wired, failing only on
the entry point nobody tested. Both were deduplicated in September 2026. Starting with one
`ResetAll` is free; discovering the need again is not.

**Prefix on both, deliberately** — per-run state must be cleared before the run's own objects
register into it. The mirror rule, from Weapon Forge's wingman sidecar: anything that *reads* a
saved record has to be a **postfix** on `GameSaver.Load`, because this prefix is what clears the
state it would be writing into.

## Findings about the game, measured 2026-09-21

These came out of scoping New Game Plus and they shape what this mod can cheaply do.

- **★ THE GAME HAS NO WIN CONDITION WIRED UP.** `GameController.GameWon` is a declared
  `static Action` with **two subscribers and no caller** (`GameWonScreen`,
  `LeaderboardScoreSubmitter`); `isGameWon` is declared and never assigned. The 4/4 boss counter is
  real and memento-backed (`RunData.RegisterBossKilled`, one `bossEntityId`, `bossCount = 4`) and
  logs to the ship feed, but **reaching 4/4 does nothing.** `GameWonScreen` is fully built, with
  working Restart and Quit buttons, behind an event that never fires.
  **⚠ The game is in open playtest and the devs will wire this up**, so hang anything off
  `RegisterBossKilled`, not off `GameWon`.
- **★ A FOURTH OPTIONS TAB IS A REAL SEAM.** `OptionsScreen.Awake` builds `this.tabs` as a plain
  array from three serialized fields, and every navigation path uses `tabs.Length`
  (`(currentTab + 1) % tabs.Length`). So appending to that array in an `Awake` postfix puts a
  fourth tab in the cycle for free.
  **⚠ AND THERE IS A BOUNDS TRAP.** `ShowTabCoroutine` walks `tabButtonImages[i]` for
  `i < tabs.Length` with no guard, so growing `tabs` to 4 while `tabButtonImages` stays 3 throws
  `IndexOutOfRange` **inside the game's own coroutine**. Exactly the shape of
  `HitscanWeapon.visualsInstances` in Weapon Forge. A fourth tab must clone a tab button too.
  `OptionsTab` is `public abstract` with abstract `OnOpened`/`OnClosed` and a private serialized
  `OptionsMenuitemBase[] items`; `OptionsMenuItemButtons` / `ItemList` / `ItemSlider` are the three
  item kinds to clone.
- **`PUNK Mods Menu (framework)` v2.1.0 is a third-party plugin in the author's install**, loading
  alongside `PUNK Debug Menu Key (F1)` v2.1.0, whose own log line says *"Toggle it in the Mods
  menu."* **Its DLL is not in this repo or the reference folder, so nothing here has read it.**
  Whether the menu ask is "plug into that" or "build our own tab" is UNANSWERED — do not assume
  either. Note the debug plugin loads *before* the framework and still registers, which suggests
  deferred registration, but that is an inference from a log line and not a measurement.
- **Cross-run persistence exists**: `MetaProgressManager`, on PlayerPrefs — a total death count and
  a semicolon-joined list of unlocked loadout names.
- **`GameSaver.Save(string)` / `Load(string)` are public and take an arbitrary folder name**, and
  `GameScene.GoToGameScene(RunArguments)` is public static with a public `seed`. But `Load` is
  **all-or-nothing** — level, graph, every entity, `RunData` and `Vault` together.
- **Enemy difficulty already has a lever**: `EnemyGenerator.PlaceBasedOnEcosystem` computes
  `remainingPowerLevel = node.radius * num * difficultyMultiplier` and spends it drawing enemies.
  That is a **spawn-budget** scaler, which is not the same thing as per-unit stat buffs.

## Findings about the game, measured 2026-09-24

These came out of noting the salvage-shop idea (trade in weapons and gadgets for materials, spend
them on permanent upgrades to what you keep). Nothing is built.

- **The game has no sell / scrap / recycle mechanic at all.** `Vault.Remove(Module)` is public, so
  removing an item is one call; the screen is the new part. Scrap from the vault, not the grid —
  taking a module off the grid runs `OnUninstalled`, which effects rely on.
- **★ THE SAVE DOES NOT KEEP A MODULE'S LEVEL.** `Module.Memento` holds the data id, connections,
  `powerCore`, `levelModificationField` and `powerLevel` — no `Level`, no `BaseLevel` — and
  `RestoreFromMemento` sets `Level = Data.level`. `DeepCopy` drops `BaseLevel` as well
  (`CopyValues` copies only `Level`). So anything this mod does to a module's level **silently
  reverts on continue** unless the mod stores and re-applies it itself.
- **A level only moves a stat that scales**: `FloatSeries.GetElement` is `baseValue + change * i`,
  so a series with `change: 0` ignores level. And a stock **weapon's** own stats are plain numbers
  on `WeaponData`, not level-driven at all — every stock weapon module ships `canBeBoosted: 0`.
- **★ `WeaponFactory.Create` IS WHERE THE GAME APPLIES ITS OWN WEAPON UPGRADES.** It builds a fresh
  `WeaponBase`, then runs every cluster module's `IWeaponModifier.Modify` on it
  (`ModifyWeaponProperty` writes `FireRate`, `Damage` and ten others directly, right there). It runs
  on every cluster refresh, and before any Weapon Forge feature captures a base — so a change made
  in a postfix there looks like a stock augmentation to everything downstream. **That bears on open
  question 6 below**: the one place a weapon can be changed without needing to know whether Weapon
  Forge is installed is the place the game itself does it.
- **⚠ Adding an ingredient to the Vault unlocks stock shop items.** `Vault.Add(Ingredient)` calls
  `RunData.RegisterIngredientAcquired`, which feeds `ingredientsEverOwned` — the shop's unlock gate.
- **⚠ A currency of this mod's own must not live in the Vault.** `Vault.RestoreFromMemento` does
  `ingredients.Add(registry.Get(id), n)`; with this mod off, a mod-registered ingredient resolves to
  null and the `Dictionary.Add` throws — the save will not load. Same hazard as switching a Forge mod
  off. Keep mod state in the mod's own sidecar.

## The log file — `ForgeLog` (built 2026-09-25)

His ask: a separate log per mod, and *"the mods that fall under the gamemodeforge logs should fall
under the logs for that mod."* So **every mode this mod ever registers logs to
`BepInEx\GameModeForge.log`** (the launch before: `GameModeForge.prev.log`), never to LogOutput.log.
A deliberate copy of Weapon Forge's `ForgeLog`; only the names differ.

- **Every log source is `ForgeLog.Source("GameModeForge[.X]")`, never `Logger.CreateLogSource`.**
  In BepInEx 6.0.0-be.785 `CreateLogSource` is `new ManualLogSource` + `Logger.Sources.Add`, and that
  Add is the only wire to LogOutput.log and the console. `logfiletest.py` fails on a stray one. A new
  mode should log through `GameModeForgePlugin.Log` or its own `ForgeLog.Source`.
- **The plugin's registered `Logger` ("Game Mode Forge", with spaces) is what LogOutput.log keeps**:
  loaded (+ where the file is), BUILD, patches applied — plus a copy of every Error/Fatal from the
  file side. `ForgeLog.Start` is the FIRST line of `Awake`, so those are copied into the file too.
- **It copies the game's own errors whose stack trace has a `GameModeForge.` frame.** BepInEx's
  `WriteUnityLog` defaults false, so "Unity Log" is blacklisted from LogOutput.log and a throw out of
  one of our patches was in no file at all.
- **The restart row and the log file interact**: `ForgeRelaunch` starts the new game BEFORE this one
  quits, so `OnApplicationQuit` closes the file and the new launch waits up to 2s for it before
  deciding the game is running twice (`GameModeForge.2.log`).
- **The settings tab cannot wipe a sibling's `[Logging]` section.** `ForgeModControl` binds only
  `[General] Enabled` on a fresh `ConfigFile`, and be.785's `ConfigFile.Save` writes `Entries`
  concatenated with `OrphanedEntries` — read off its IL, not assumed.
- `logfileprobe.py` compiles the shipped file against the real `BepInEx.Core.dll` and runs seven
  launch scenarios, one process each.

## Platform gotchas

**⚠ THE GAME DECLARES ITS OWN `ConfigFile` IN THE GLOBAL NAMESPACE** — it is the abstract base of
`ConfigRegistry<TItem, TId>` — and **a global-namespace type beats one imported by a `using`.** So
a bare `ConfigFile` resolves to PUNK's, not BepInEx's, and the compiler error is *"cannot create an
instance of the abstract type"*, which sends you looking at BepInEx. Fully qualify
`BepInEx.Configuration.ConfigFile`. Pinned.

**⚠ `GetComponentInParent<T>()` skips inactive parents** unless given the include-inactive overload,
and the cloned tab is inactive while it is being built. `ForgeOptionsMenu.InsideButton` walks the
transform chain with `GetComponent` by hand instead, which has no such condition — getting this
wrong would put a row's caption on a button face.

**⚠ `Resources.FindObjectsOfTypeAll` sees PREFABS as well as inactive scene objects.** Right when
you want a prefab, wrong when you want a live widget: opening a `Prompt` that is a prefab does
nothing and reads as the confirm silently failing. Filter on `gameObject.scene.IsValid()`.

## Open questions — bring these to the design conversation

Nothing below is decided, and the code deliberately does not assume an answer.

1. **What is a game mode allowed to touch** — spawns, damage globally, loot, the ship, all of it?
2. **Does a toggle apply mid-run or only at run entry?** Held open as `ForgeModeScope` per mode
   rather than one global answer. Run entry is the cheaper default.
3. **One mod with many switches, or named modes that bundle switches?**
4. **Does it need to be visible to the other mods at all, or is one-way (it reads them) enough?**
   `ForgeInterop` currently assumes one-way.
5. **The menu.** Blocked on reading the Mods Menu framework DLL. The game's own options screen is
   the fallback and is a workable seam (above).
6. **★ The hard one: a mode that changes how weapons behave must do so without knowing whether
   Weapon Forge is installed.** "Exactly one owner per shared mechanic" is easy for the existing
   pair and genuinely hard here.

## Harness

`python Harness/run.py` runs every check and prints one total. The Leaderboard has four:
`leaderboardtest.py` (the code), `lbmathprobe.py` (runs LbMath: the ranking, the tally, every word
against the main menu's font), `lbstoreprobe.py` (the BUILT DLL's board file, in a temp folder) and
`lbpatchprobe.py` (every patch and member against Punk.Main.dll, and the order inside the game's own
game over). Enemy Traits has four:
`enemytraitstest.py` (the code), `etmathprobe.py` (runs EtMath, NgMath's roll beside it),
`etrulesprobe.py` (the BUILT DLL's parser on every builder preset) and `etpatchprobe.py` (every
patch, and every private member reached by name, against Punk.Main.dll). New Game Plus has four:
`newgameplustest.py` (the code's shape and every trap above), `ngmathprobe.py` (compiles the
shipped `NgMath.cs` and runs its arithmetic; mutation-tested with five deliberate breaks, all
caught), since the health bar layers (2026-10-05) `ngpatchprobe.py` (every Harmony patch in
the BUILT NewGamePlus.dll against Punk.Main.dll, with salvagepatchprobe's checker - before it,
nothing checked New Game Plus's ~40 patch targets, and one wrong one disables the whole mod for a
launch), and since the enemy weapon stats (the same day) `ngrulesprobe.py` (the BUILT DLL's rules
parser, run on every builder preset). Salvage Shop has three: `salvagetest.py`, `salvagemathprobe.py` and
`salvagepatchprobe.py` (reads the BUILT DLL and Punk.Main.dll with Mono.Cecil - never loads them,
because the game's assembly has an interface method with a body that the desktop .NET Framework
refuses to load). Same layout rules as the other two
repos, and they are enforced by layout rather than stated:

- **Everything directly in `Harness/` only asserts. Mutators go in `Harness/edits/`**, which the
  runner never walks — because a filename cannot tell you whether a script asserts or mutates, and
  running every script in a folder once silently duplicated four methods in `WeaponBuilder.cs`.
- **`STALE` is a distinct result from `FAIL`.** A harness whose subject was archived is not a code
  failure, and collapsing the two makes a red total meaningless.
- **A pin that does not run is a comment.** These live in the repo for that reason.
