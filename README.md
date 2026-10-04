# Game Mode Forge

Toggleable **world rules** for PUNK — the fourth mod in the Forge family, alongside
[Weapon Forge](https://github.com/Sugarheady/WeaponForge) and
[Module Forge](https://github.com/Sugarheady/ModuleForge).

> `GameModeForge.dll` itself defines no world rules — it is the **settings tab** in the game's
> Options screen that switches the Forge mods on and off, and shows their settings.
>
> This repo also builds three separate DLLs:
>
> - **[New Game Plus](#new-game-plus-a-separate-dll)** — after the four Queens, a new world with
>   your whole build, and tougher enemies each time.
> - **[Salvage Shop](#salvage-shop-a-separate-dll)** — at a station, scrap the weapons and gadgets
>   you do not want for Ex and Bond, and spend them upgrading what you keep.
> - **[Pause Options](#pause-options-a-separate-dll)** — Options in the pause menu, so you can
>   reach settings without leaving a run.

## The settings tab

A fourth tab beside Gameplay / Video / Audio. One Off/On row per installed Forge mod, plus a
**restart** row. It is in the **main menu's** Options, and — with Pause Options installed — in the
**pause menu's** Options too (see below for how the two differ).

**The restart row works on its own.** Neither Forge mod has a hot reload — their JSON is read once
at startup — so restarting is how you pick up an edit to a weapon or module file, and the row does
that without needing a switch to have been touched. If a switch *has* been changed, it is written
before the game restarts.

**It restarts the copy of the game you are running**, by launching that same executable — so a
separate modded copy comes back as the modded copy. Steam's launch link is only the fallback if
the executable cannot be started, because Steam always launches the copy *it* installed.

**And it comes back with its mods.** BepInEx's loader (Doorstop) marks the running game's
environment when it starts, and skips any game that already carries that mark — which a game
started *from* this one inherits. The restart clears the loader's `DOORSTOP_` values first, and
the log line before the restart names the ones it cleared.

Switch tabs with **Q / E** on a keyboard (**LB / RB** on a gamepad), or click the tab.

Switching a mod off means it patches nothing and builds nothing — the game runs as if the DLL were
not installed. **It takes effect at the next launch**, because a .NET assembly cannot be unloaded
once it is in memory; restarting the game is the only genuine way to reload one, and the tab does
that for you.

Each mod owns its own switch, in its own BepInEx config, and honours it with this mod absent. The
tab only edits those files.

> **⚠ A saved run that used Forge weapons or modules will not load while that mod is off.** Switch
> it back on and the save opens again.

### Mid-run (from the pause menu)

The same tab, **without the restart row** — restarting from a pause would lose everything since
your last save. So a switch you flip there is **saved straight away and takes effect the next time
the game starts**. The row shows the game's own "changed" marker and says so, e.g.
`Weapon Forge - OFF next launch`. Turning a content mod off mid-run adds
`(this run may not load)`, because the run you are playing is the save it puts at risk: switch
it back on before you continue that run.

### More rows than fit

With New Game Plus installed the tab holds more rows than the screen does, so it **scrolls**:
moving up and down keeps the selected row in view, and the mouse wheel scrolls it too.

### New Game Plus's rows

Under the **New Game Plus** switch come its own settings. Unlike a mod switch they are saved the
moment you change them and need no restart — New Game Plus reads them when you next jump.

| row | what it does |
|---|---|
| Enemy health a plus | 0–500% in steps of 25. Sets the health buff's start and its added-a-plus to the same number |
| Enemy count a plus | 0–200% in steps of 10, the same way |
| Enemy damage a plus | 0–500% in steps of 25, the same way |
| Enemy damage: hazards too | whether the world's own hazards take the damage % too (on by default) |
| Marker on buffed enemies | a tiny marker above each enemy New Game Plus has buffed (off by default) |
| Carry: … | one row per thing that carries to the next world (see below), all on |
| New world: health, fuel, ammo | **Refill** (default) or **Keep** what you had |

### Salvage Shop's rows

Under the **Salvage Shop** switch. Saved the moment you change them; no restart.

| row | what it does |
|---|---|
| Scrap refund of upgrades | 0–100% in steps of 5 (default 100%): how much of what an item's upgrades cost comes back when you scrap it |
| Bond chance a scrap | 0–100% in steps of 5 (default 50%) |
| Ex a scrap | 0–20 (default 2) |
| Quick salvage (no confirm) | off by default — the same switch as the tickbox on the SALVAGE screen |

## New Game Plus (a separate DLL)

`NewGamePlus.dll`. Kill all four Queens of a world and a victory screen opens — the game's own,
built and never used, retitled **WORLD 1 CLEARED** (then **NEW GAME PLUS 1 CLEARED**, …):

- **KEEP PLAYING** — you stay in the cleared world. From then on the pause menu has a
  **START NEW GAME PLUS** button, which asks before it goes.
- **NEW GAME PLUS** — a brand new world: new map, new enemies, four new Queens. **You keep your
  build**, and the enemies are tougher than last time.
- **END RUN** — asks first, then deletes the save and takes you back to the loadout picker.

The world waits while the screen is up.

### What carries

Each of these has its own switch (in the settings tab, or
`BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.cfg`), and all are on:

- your **ship grid** — the slot layout *and* every module installed (in co-op, both ships, each to
  the same player);
- the **vault** — spare modules, ingredients, consumables;
- your **money** (which is also the score);
- the **shop's stock**, and its **risen prices** (off puts them back to base);
- the **stations-unlocked count** (the shop's tier) — the new world's stations themselves are new
  and locked; unlock each one by using it, as always;
- the **ingredients you have ever held** (the shop's unlock gate);
- the run's **time and kill count**.

You arrive on **full health, fuel and ammo** unless you switch that to keep what you had. Drones
and wingmen do not carry — summon them again. **With Weapon Forge installed, a ranked gun keeps
everything**: its rank, its XP, every choice taken, and any choice still owed.

### Tougher each time

Three buffs, each set for "from which world" and how much it adds each world:

- **enemy health** — +50% a world by default, from NEW GAME PLUS 1, on a share of the enemies that
  starts at 25% and rises each world to 100%, with no cap. The Queens get it too; your own drones
  and minions never do. Which enemies are buffed is fixed per enemy, so a Continue gives the same
  ones.
- **enemy count** — every room spends +30% a world more on enemies, from NEW GAME PLUS 1. It can
  never add a Queen.
- **enemy damage** — +100% a world by default (double, then triple…), from NEW GAME PLUS 1, on
  **every** enemy, the Queens included, with no cap. It counts everything an enemy does — shots,
  beams, blasts, contact hits, electric sparks and the fire it sets — and it hits everything
  harder: you, your drones, minions and wingmen, a co-op partner. A bullet you knock back keeps
  its extra damage. With **hazards too** on (the default), the world's own damage takes the same %
  on everything it hits: hazard blocks, the white bulbs, exploding plants, and the steam that sets
  you alight. Nothing of yours is made stronger or weaker, and crates and boxes are left alone —
  they hold loot, and none of them does any damage.

The buffs add up — +50% a world is +50%, +100%, +150%. **New Game Plus Builder.html** (in this
repo) edits the full schedule, with presets and a table of every world; it makes
`BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.rules.json`. Its **TEST** preset starts
health and count in the first world, so you can try them without killing four Queens first;
damage starts at NEW GAME PLUS 1 there too.

A world's rules are fixed when it begins — at the jump, or at the start of a run — and kept with
the save, so an edit applies to the next world and never changes the one you are in. Arriving in a
new world shows a line naming it and its rules, and the game saves straight away.

### WORLD INFO

A **WORLD INFO** button sits straight under Continue in the pause menu, from the start of every
run. It shows the run's stats (the game-over screen's, plus which world you are in, Queens killed
across every world, and your money) and the rules in force in this world. **Esc** (**B**) closes it
and leaves the game paused.

### Saving

Save and quit, relaunch, Continue: you are back in the same New Game Plus world, with the same
rules and the same enemies buffed. Dying ends the whole chain, exactly as a normal run's death
deletes its save. With `NewGamePlus.dll` switched off a New Game Plus save still loads — as a
normal-strength run — and switching it back on picks the world up again.

## Salvage Shop (a separate DLL)

`SalvageShop.dll`. Crates and the shop hand you duplicates and dead weight, and the game has no
way to get rid of them. At any station, the grid tab's right-hand panel now has a third sub-tab,
**SALVAGE**, beside SHOP and VAULT. It has two modes and one slot, and your vault stays on screen
underneath it:

- **SALVAGE** — drag a weapon or gadget from your **vault** into the slot (anywhere on the panel
  works). It asks "salvage it?" and, when you say yes, scraps it for **Ex** and maybe **Bond**: by
  default **2 Ex**, and a **50%** chance of **1 Bond**. It does not say what you will get. Only
  the vault, never what is equipped; only weapons and gadgets.
- **UPGRADE** — drag any module you own, in the vault or on your ship, into the slot. Pick a stat,
  buy a step; buy as many as you like. The module never leaves where it is, so taking it out (or
  closing the menu) loses nothing.

**Ex and Bond** are two ingredients the game ships and never uses — nothing drops them, nothing
costs them — so they show on the HUD's ingredient bar like any other, a save holding them loads
with the mod off, and salvage is the only way to get them.

**What a step costs:** the ladder runs **per item** — each module counts its own steps. Step n
costs **n Ex**, and Bond joins after step 5: steps 6–9 add 1 Bond, 10–14 add 2, 15 adds 3… No
cap. Scrapping an upgraded item gives back **100%** of what its upgrades cost (a setting), on top
of its scrap.

**What a step gives:**

| module | stats (one a step, your pick) |
|---|---|
| weapons and weapon gadgets | **damage** +10% (the hit only) · **explosion** +1 damage, +0.5 radius · **spark** +1 damage, +1 chain · **fire rate** +10% (a gadget: shorter cooldown) · **range** +10% (lifetime on a lobbed or rocket shot) · **projectile speed** +10% (a rocket's thrust too) · **spread** −10% · **+1 projectile** · **reduce cost** −10% |
| ship upgrades (UP, REGEN, SHIELD) | the **capacity** or the **regen a second** of any of HEALTH, FUEL, GEL, CAPS, STAMINA, TECH, ELECTRON; a shield also takes **+1 level** |
| the Power Core, the augmentations that scale | **+1 level** |
| drone gadgets | **+1 level**, which is **+1 drone** |

Steps of one stat add up, of the gun's own number: three damage steps are +30% of its hit. Only
stats that mean something on that module are offered — no explosion or spark on the beam or on the
two gadgets that fire a unit, no spread on a 360° ring. An added explosion or spark goes off on hit
and when a timed shot expires, in the gun's own damage type, and costs nothing a shot (it was
bought with scrap). **Reduce cost** cuts the gun's own cost and the per-shot cost of every add-on
module on it; ten steps make it free to fire. On a gadget, ten steps take the activation cost down
to 0.001 rather than 0: free in practice, and a gadget whose resource your ship has no tank for
still cannot fire, as in the stock game. Sub-emitter fragments are never upgraded (the game
never upgrades them either).

**The card shows it all:** a **SALVAGE** line per stat, a level as the game's own **+N** badge, an
equipped gun's live numbers and a vault gun's too.

**Keys.** Pad: **RT** steps VAULT → SHOP → SALVAGE (**LT** back), and on SALVAGE switches the mode;
**A** sends the highlighted module to the slot; the stick picks a stat; **Y** confirms; **B** takes
it out; **X** ticks quick salvage. Mouse and keyboard: drag into the slot; click a stat to pick it
and again to buy it (or press **R**); right-click the slot (or **X**) to take it out; **G** switches
the mode; **C** ticks quick salvage. While the SALVAGE tab is open, a pad's **A** fills the slot
rather than moving a module to your ship — use the VAULT tab for that.

**QUICK SALVAGE** skips the ask — except for an upgraded item or a ranked gun (Weapon Forge),
which always ask a second time and say what is lost.

**Every number is a setting.** **Salvage Builder.html** (in this repo) edits all of them, with
presets and a price table: the scrap payout, the refund, the shared ladder, every stat's own
amount and its own ladder, and an optional per-shot cost for the added explosion and spark. It
makes `BepInEx\config\com.sugarheady.gamemodeforge.salvageshop.rules.json`. The file is re-read
when it changes — no restart.

**Saving and New Game Plus.** Upgrades are saved beside the save (`salvage.txt`, deleted with it)
and come back on a Continue. They carry across a New Game Plus jump with their items. With
`SalvageShop.dll` switched off a save still loads — the items are simply un-upgraded — and
switching it back on brings the upgrades back.

## Pause Options (a separate DLL)

`PauseOptions.dll` puts an **OPTIONS** button in the pause menu, straight under *Save and quit*
and the same width as it, so you can change volume, video or gameplay settings — or the Forge
switches above — without leaving the run. (The game's own layout puts a spacer before it and makes
it narrower; Pause Options moves it up and matches the width.)

The button is the game's own: the pause menu already has it, wired to the same Options screen the
main menu uses, and the playtest simply ships it switched off. Pause Options switches it on and
fixes two things the stock wiring gets wrong once it is on:

- **Esc (B on a gamepad) closes Options and leaves the game paused.** The game uses the same key for
  "close Options" and "back out of the pause menu", so without the fix one press did both and the
  run carried on behind you.
- **The pause buttons are locked while Options is open.** Otherwise a gamepad could move the
  hidden pause-menu highlight and press *Save and quit* or *Restart* behind the Options screen.
  When Options closes, the pause menu comes back with OPTIONS selected.

Everything in Options works mid-run: volume changes at once, video changes when you press Apply,
aim assist and rumble change at once. Camera sway is the one exception: the game reads it once,
when a run's world is built, so a change made mid-run shows from the next run or Continue.

**It works with or without Game Mode Forge.** With it, Pause Options is one more row in the
settings tab, and its log lines go into `GameModeForge.log`; without it, the button still works
and the lines go to `BepInEx\PauseOptions.log`. If a game update ever switches the button on
itself, Pause Options notices, does nothing, and says in the log that it can be removed.

## What it is for

The other Forge mods are **per-file**: a weapon or a module describes itself, and nothing is true of
the world unless something you are carrying says so.

**A game mode is the opposite — a rule about the run.** Things like:

- a New Game Plus that rerolls the world after the final bosses and keeps your build — **built**,
  as its own DLL (above)
- stations that each print their own assortment at their own rising prices
- ghosts of your past runs, carrying the build you died with
- a salvage shop that breaks down weapons and gadgets you don't want into materials for upgrading
  the ones you keep

The rest are not built. They are the examples that decided this mod should exist.

## Requirements

- PUNK Playtest v0.12.9
- BepInEx

Install by dropping `GameModeForge.dll` into `BepInEx\plugins\` — and `NewGamePlus.dll`,
`SalvageShop.dll` and `PauseOptions.dll` beside it if you want them. Each works with the other Forge mods present or
absent — there is no hard dependency in any direction.

## Settings

`BepInEx\config\com.sugarheady.gamemodeforge.cfg`, generated on first run. One switch per mode
under `[Modes]`, and **every mode ships off**. The file is followed while the game runs, so a mode
can be changed between runs without restarting; a rule that latches at run entry says so in the log
rather than appearing to do nothing.

Pause Options has one setting, its own on/off switch: `[General] Enabled` in
`BepInEx\config\com.sugarheady.gamemodeforge.pauseoptions.cfg`. The settings tab edits it for you.

New Game Plus has two files beside it: `com.sugarheady.gamemodeforge.newgameplus.cfg` (its on/off
switch, what carries, the marker) and `com.sugarheady.gamemodeforge.newgameplus.rules.json` (how
much tougher each world is — made by New Game Plus Builder.html, or the tab's sliders). A rules
file without an `enemyDamage` section (written before enemy damage existed) gets enemy damage's
default, and the log says so.

Salvage Shop has two files beside it too: `com.sugarheady.gamemodeforge.salvageshop.cfg` (its
on/off switch and quick salvage) and `com.sugarheady.gamemodeforge.salvageshop.rules.json` (every
number — made by Salvage Builder.html, or the tab's rows). A rules file that is not valid JSON is
refused: the last rules that read are kept, and the SALVAGE screen says the file is unreadable.

## The log

Game Mode Forge writes its own log, `BepInEx\GameModeForge.log` (the launch before is kept as
`GameModeForge.prev.log`), and so does every mode it registers. `LogOutput.log` stays readable for
BepInEx and other mods: it gets only Game Mode Forge's load lines and a copy of every error. If the
game itself throws an error whose stack trace names this mod, a copy goes in `GameModeForge.log` —
BepInEx leaves the game's own errors out of `LogOutput.log` by default. Set
`[Logging] OwnLogFile = false` to put everything back in `LogOutput.log`.

Pause Options', New Game Plus's and Salvage Shop's in-game lines go into `GameModeForge.log` too
(under `GameModeForge.PauseOptions`, `GameModeForge.NewGamePlus` and `GameModeForge.SalvageShop`);
without Game Mode Forge installed they go to `BepInEx\PauseOptions.log`, `BepInEx\NewGamePlus.log`
and `BepInEx\SalvageShop.log` instead.

## Building

See [BUILDING.md](BUILDING.md). You point one property at your PUNK install and never edit a
`.csproj`.

## Contributing

[CLAUDE.md](CLAUDE.md) is the working-notes file for this repo — the architecture, the rules it
inherits from its siblings, what has been measured about the game, and the list of design questions
that are still open. Read it before writing code. The full design of New Game Plus, and of the
add-ons still planned, is [Docs/CONCEPT.md](Docs/CONCEPT.md); the Salvage Shop's is
[Docs/SALVAGE SHOP - CONCEPT.md](Docs/SALVAGE%20SHOP%20-%20CONCEPT.md).

`python Harness/run.py` runs every check that does not need the game.
