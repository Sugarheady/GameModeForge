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
> **This repo builds FOUR DLLs**: `GameModeForge.dll`, **`PauseOptions.dll`** (`PauseOptions/`,
> 2026-09-28, switches on the pause menu's own Options button), **`NewGamePlus.dll`**
> (`NewGamePlus/`, 2026-09-29) and **`SalvageShop.dll`** (`SalvageShop/`, 2026-10-02). See "Pause
> Options", "New Game Plus" and "Salvage Shop" below.
>
> **★ NEW GAME PLUS STAGE 1 IS BUILT (2026-09-29, his go: "check over the concept … otherwise go
> ahead with the build"), NOT YET PLAY-TESTED. Enemy Traits and a Local Leaderboard are still
> planned.** Read [`Docs/CONCEPT.md`](Docs/CONCEPT.md) before touching any of them: it holds every decision
> Sugarheady made and the measured facts they stand on, and its build order (section 6) says what
> stage 2 is. The decisions made while BUILDING are in CONCEPT.md's section 9.
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
stage 1 on 2026-10-02), `NgVictory`, `NgPause`, `NgWorldInfo`,
`NgRules` (the rules JSON), `NgSettings` (the cfg switches), `NgMath` (pure arithmetic), `NgTab`
and `NgCarry` (the two public contracts), `NgUi`, `NgLog`.

- **★ NEVER `GameController.GameWon`.** Its other subscriber, `LeaderboardScoreSubmitter`,
  uploads the score. The win is detected off `RunData.RegisterBossKilled` reaching
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

**Files:** `SalvageShopPlugin` (the gate, quick salvage), `SalvageMath` (every number; no Unity),
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
  quick salvage line, the stat lines) has a `CanvasGroup` whose `blocksRaycasts` is off while
  `input.IsMovingModule` (`SalvagePanel.Update`), and the click falls through to the background.
  Anything clickable added to the panel must go through `TakesClicks` (pinned by count).
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

`python Harness/run.py` runs every check and prints one total. New Game Plus has two:
`newgameplustest.py` (the code's shape and every trap above) and `ngmathprobe.py` (compiles the
shipped `NgMath.cs` and runs its arithmetic; mutation-tested with five deliberate breaks, all
caught). Salvage Shop has three: `salvagetest.py`, `salvagemathprobe.py` and
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
