# Game Mode Forge

Toggleable **world rules** for PUNK — the fourth mod in the Forge family, alongside
[Weapon Forge](https://github.com/Sugarheady/WeaponForge) and
[Module Forge](https://github.com/Sugarheady/ModuleForge).

> `GameModeForge.dll` itself defines no world rules — it is the **settings tab** in the game's
> Options screen that switches the Forge mods on and off, and shows their settings.
>
> This repo also builds five separate DLLs:
>
> - **[New Game Plus](#new-game-plus-a-separate-dll)** — after the four Queens, a new world with
>   your whole build, and tougher enemies each time.
> - **[Enemy Traits](#enemy-traits-a-separate-dll)** — things enemies can do that make a world
>   harder: wind-up, grow, overload and ricochet, from NEW GAME PLUS 1.
> - **[Leaderboard](#leaderboard-a-separate-dll)** — a board of your finished runs, on your PC: the
>   furthest world first, then the score.
> - **[Salvage Shop](#salvage-shop-a-separate-dll)** — at a station, scrap the weapons and gadgets
>   you do not want for Ex and Bond, and spend them upgrading what you keep.
> - **[Pause Options](#pause-options-a-separate-dll)** — Options in the pause menu, so you can
>   reach settings without leaving a run.

## The settings tab

A fourth tab beside Gameplay / Video / Audio. One Off/On row per installed Forge mod, plus a
**restart** row. It is in the **main menu's** Options, and — with Pause Options installed — in the
**pause menu's** Options too (see below for how the two differ).

**A switch is saved the moment you flip it**, in the main menu and mid-run alike. The row shows
the game's own "changed" marker and says when it happens, e.g. `Pause Options - ON next launch`,
and the log says `Pause Options -> ON (takes effect on the next launch)` straight away. So the
restart row, or quitting and starting the game yourself, both pick it up.

**The restart row sits under the list and never scrolls**, so "Not now / Restart now" is always
on screen however many rows the list has. Up and down still reach it after the last row.

**The restart row works on its own.** Neither Forge mod has a hot reload — their JSON is read once
at startup — so restarting is how you pick up an edit to a weapon or module file, and the row does
that without needing a switch to have been touched.

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
your last save. A switch you flip there is saved straight away, as in the main menu, and **takes
effect the next time the game starts**. Turning a content mod off mid-run adds
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
| Station unlock cost | whether unlocking a station costs money (off by default; its numbers are in the builder page) — see "Station unlock cost" below |
| Money drops | more money pickups each world (off by default; its numbers and sources are in the builder page) — see "Money drops" below |
| Rooms near the start a plus | harder enemies earlier, dial A: 0–100% a world in steps of 5, 0 = off (the default) — see "Harder enemies earlier" below |
| Far-only enemies closer a plus | dial B, the same way |
| Buffs from the first world | a new run plays every world one step on (off by default) — see below |
| Marker on buffed enemies | a small amber ring with a plus in it beside each enemy New Game Plus has buffed (off by default) |
| Health bars in layers | a buffed enemy's health bar stays its normal size, its extra health stacked in coloured layers (on by default; applies at once) — see "Health bars in layers" below |
| Layer colours | the layers' palette: CLASSIC, COLOUR-SAFE, ICE, FIRE, NEON or CUSTOM (your own list in the cfg); applies at once |
| Layer count size | how big the **xN** beside a layered bar is: 0–400% in steps of 25, 100% by default, 0 hides it; applies at once |
| Carry: … | one row per thing that carries to the next world (see below), all on |
| New world: health, fuel, ammo | **Refill** (default) or **Keep** what you had |

### Salvage Shop's rows

Under the **Salvage Shop** switch. Saved the moment you change them; no restart.

| row | what it does |
|---|---|
| Scrap refund of upgrades | 0–100% in steps of 5 (default 100%): how much of what an item's upgrades cost comes back when you scrap it |
| Bond chance a scrap | 0–100% in steps of 5 (default 50%) |
| Ex a scrap | 0–20 (default 2) |

### Enemy Traits' rows

Under the **Enemy Traits** switch. Saved the moment you change them; no restart. The trait rows,
the volleys and the limit are rules of a world, so they apply from the next one (the world you are
in keeps the traits it began with); the marker and the gauge apply at once.

| row | what it does |
|---|---|
| Trait: wind-up / grow / overload / ricochet | each trait on or off (all on by default). Its numbers are in the builder page. A row says so when the trait needs New Game Plus |
| Volleys | **count and speed up** (default), **count, game's pace**, or **do not count** — see "Volleys" below |
| Traits an enemy: limit | off by default; on, an enemy keeps at most so many of the traits it rolled |
| Marker on enemies with traits | New Game Plus's amber ring beside an enemy that got a trait not every enemy gets (off by default) |
| Overload gauge on enemies | a very small bar over every enemy with overload: blue as it fills, gold while it is overloaded (off by default) |

### The Leaderboard's row

Under the **Leaderboard** switch. Saved the moment you change it; no restart.

| row | what it does |
|---|---|
| Record runs | on (default): every run that ends in a death or with END RUN goes on the board. Off: nothing is added - for test runs. Read when a run ends; the runs already on the board stay |

## New Game Plus (a separate DLL)

`NewGamePlus.dll`. Kill all four Queens of a world and a victory screen opens — the game's own,
built and never used, retitled **WORLD 1 CLEARED** (then **NEW GAME PLUS 1 CLEARED**, …):

- **KEEP PLAYING** — you stay in the cleared world. From then on the pause menu has a
  **START NEW GAME PLUS** button, which asks before it goes.
- **NEW GAME PLUS** — a brand new world: new map, new enemies, four new Queens. **You keep your
  build**, and the enemies are tougher than last time.
- **END RUN** — asks first, then deletes the save and takes you back to the loadout picker. With
  the Leaderboard installed, the run goes on the board first.

The world waits while the screen is up. Under the title it names the next world and lists its
rules, one to a line, shrinking to fit between the title and the buttons. That text is in the
game's own title font, which has no `%` sign, so there it reads "+50 percent health" rather than
borrowing a `%` from another font at the wrong size.

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

Three headline buffs, each set for "from which world" and how much it adds each world, and the
enemy weapon stats below them:

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

**Enemy weapons** — eight more buffs, one for each thing an enemy's gun does, **all off by default**
and set in the builder page (or the rules file). Each one rolls on its own, so one enemy may fire
faster and another fly its shots further; **coverage** is the share of enemies that get it — 100% by
default, except extra projectiles, which start at 25% and rise 25% a world. Fixed per enemy, so a
Continue gives the same guns; the Queens get them too, your own drones and minions never do.

| stat | what it does (default when on, a world) |
|---|---|
| fire rate | shots a second (+20%); bursts tighten the same, and a gun that runs on ammo costs that much less a shot, so it really fires more |
| reach | how far a shot goes — its range, its lifetime or a beam's length (+20%). Enemies still open fire at their usual distance |
| shot speed | how fast a shot flies, a rocket's engine too (+15%) |
| push | how hard a hit shoves you (+25%) |
| burn | the guns that already set you alight — the lasers (the Queen's too), the fire rockets, the fireball — burn hotter (+50%) |
| ignite | every other gun sets you alight: this much burn a hit (+1). A laser hits many times a second, so it burns fastest |
| extra projectiles | whole bullets: **add** more, and **add** more again every **every** worlds (add 2, every 2: +2, +2, +4, +4…). A gun that fires straight fans them 8° apart |
| burst shots | whole shots added to each burst, the same way |

Damage is not among them — **enemy damage** above is the one damage control. The minion and mine
launchers (the Queen's, the red crosses', the officer's mines) are left alone: what they launch is an
enemy, with buffs of its own. With the marker on, an enemy that got a stat not every enemy gets
wears it too.

**Fire rate reaches beams and volleys** (since stage 3). An enemy **beam** fires 20–60 times a second
but can only hit you once every 0.1–0.5 s, so its fire rate alone changed nothing; now the gap
between its hits shrinks by the same amount, so +20% fire rate is +20% beam damage. A **volley** —
the Queen's bullet patterns, the crosses' and the Fly Zapper's — fires on its own timer, not its
gun's; with **volleys too** on (the default, in the builder page) its pattern speeds up by the same
amount. A world that began before this keeps playing without the volley part.

The buffs add up — +50% a world is +50%, +100%, +150%. **New Game Plus Builder.html** (in this
repo) edits the full schedule, with presets and a table of every world; it makes
`BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.rules.json`. Its **TEST** preset starts
health and count in the first world, so you can try them without killing four Queens first;
damage, every enemy weapon stat (on every enemy), the station unlock cost, money drops, both
harder-earlier dials and a two-row enemy table (the Raven brought into the start biome, the Cross
Laser anywhere in Flesh) start at NEW GAME PLUS 1 there.

A world's rules are fixed when it begins — at the jump, or at the start of a run — and kept with
the save, so an edit applies to the next world and never changes the one you are in. Arriving in a
new world shows a line naming it and its rules, and the game saves straight away.

### Station unlock cost

**Off by default.** Switched on (the tab's **Station unlock cost** row, or the builder page),
unlocking a station — using it the first time — costs money, on a ladder:

- the first station you pay for in a world costs **100**, and each one after it **300 more**
  (100, 400, 700, 1,000…);
- the ladder **starts again at 100 in every new world**, and the amount it climbs by grows **100
  a world** (300 in the first world it is on, 400 in the next, 500…);
- **the station you start beside is always free**, and scanner stations are on the same ladder;
- the price shows on the station's own unlock prompt, and if you cannot pay, your ship's log says
  what it costs and what you have.

From NEW GAME PLUS 1 by default; every number is in the builder page. A locked station gives no
fuel, so the builder page also has a **safety net**, off by default: a station you cannot afford
opens free when your fuel is critical or a co-op partner is down. A free one still counts as a step
on the ladder. Like every rule, it is fixed when a world begins, and a Continue keeps your place on
the ladder.

**Ingredients too (none by default).** The builder page can add any of the game's nine ingredients
to the price — Fiber, Ex, Chip, Bond, Face, Gland, Powerstar, Generator, Strange Ball — each with
its own **amount**, how much more it takes **each station** after the first in a world, and how much
more **each world**, up to a cap. They come out of your vault, and only once the station opens;
if you are short of any of them, the station stays locked and keeps your money too, and your ship's
log says what you need. Looking at a locked station puts its whole price in your ship's log (its
own prompt shows the money). The safety net, when on, opens it free of everything.

### Money drops

**Off by default.** Switched on (the tab's **Money drops** row, or the builder page), money drops
grow each world: **+25% a world** from NEW GAME PLUS 1, with no cap unless you set one. It is
**more pickups** — the game's own 10, 20 and 50 — so +25% on a pile of 3 is 3 and a 75% chance of a
fourth. Every source pays it, each with its own switch in the builder page: **enemies**, **the
Queens**, **crates and boxes**, and **the world** (gold in the rock, the Wheat plant). Money a weapon
or module pays for a kill (Weapon Forge, Module Forge) is that mod's own and is not changed. WORLD
INFO counts the extra pickups.

**Big coins past double** (R21 part 4). Money pickups bump into each other, and hundreds made at
one spot froze the game (a Queen at +500% for minutes). So a drop gets at most as many extra
pickups as it already had, and never more than 50; the money past that comes as a few **big
coins** - the game's 50, drawn bigger, worth 100 to 100,000. The money is the same either way, and
WORLD INFO counts the big coins too.

### Harder enemies earlier

Two dials, **both off by default**, each decided when a world is made. Every room has a budget it
spends on enemies — small near the start, bigger further out — and some enemy kinds only appear in
rooms past a line ("outer rooms only"):

- **Rooms near the start** moves the near rooms' budget toward the far rooms': 25% of the way a
  world when on, all the way by the fourth.
- **Far-only enemies closer in** moves each "outer rooms only" line toward the start: 25% a world,
  gone by the fourth.

Both are sliders in the tab (0 = off) and in the builder page, and both stack with enemy count; a
crowded room still stops early, and the Queens are never touched.

### Your enemy table

**Off and empty by default.** In the builder page, a table of **every enemy in the game** and
every biome: put a **plus number** in a box and from that world on, the kind may appear in that
biome, anywhere in it — other biomes' enemies near the start included. A green box is the kind's
own biome, where a number drops its "outer rooms only" line (a blank one follows the dial above).
**weight** is how often it is drawn where it is new (blank = its weight at home); **cost** is what
it takes from a room's enemy budget there (blank = its cost at home).

The first 25 rows are the enemies the biomes place. **Below them is every other enemy** (41 more):
the Queen, the minions and mines other enemies launch, the ones only special rooms place (miniboss
rooms, the power-star dungeons, the Crawler and Canari rooms), your own drones and the Beacon, and
the ones only the debug menu spawns. They have no home biome, so **their weight is 0 — never drawn
— until you give their row one**, and their cost is 10 unless you give one. A row opened at weight
0 is said once in the log.

> ⚠ **Every Queen killed counts toward the 4**, so a Queen from the table can end a world before
> its boss rooms (the log says so when one is brought in). The Queen, your drones and the Beacon
> have never been tried outside their own place.

A world's rules are fixed when it is made, so the table applies from the next world.

### Buffs from the first world

**Off by default.** The tab's **Buffs from the first world** row (or the builder page) makes a
**new run** play the whole chain one step on: the first world by NEW GAME PLUS 1's rules, NEW GAME
PLUS 1 by NEW GAME PLUS 2's, and so on — every buff and the station cost included. The titles still
count real worlds (WORLD 1 CLEARED); the arrival line and WORLD INFO say whose rules are in force.
It is read when a run starts, and that run keeps it all the way through.

### Health bars in layers

A buffed enemy's health bar no longer grows with its health. It stays the size it always was, and
the extra health is **stacked in layers** of that size, Street Fighter style:

- the **last layer** is the bar the enemy always had, in its own colour (red for most enemies and
  the Queens);
- each layer above it has its own colour — **green, yellow, orange, red, purple**, then round
  again — and the rest of the bar shows the next layer down, so you can see what is under it;
- the **top layer is the partial one**: at +50% health the bar starts half full of green over a
  full red bar;
- an **xN** beside the bar counts the layers left, and disappears on the last one. **Layer count
  size** in the tab (or `[Display] HealthBarCountSize` in the cfg) sets how big it is: 100% (the
  default) is a quarter of the size it first shipped at, 400% is that first size, and 0 hides it.
  The Queens' xN follows the same percent.

The Queens' bar along the top of the screen works the same way, so a Queen at NEW GAME PLUS 2 has
her normal-size bar with an **x2**, not one twice as tall. An enemy New Game Plus did not buff
keeps the game's own bar. Switch it off with **Health bars in layers** in the tab (or
`[Display] HealthBarLayers` in the cfg) and the game's own bars are back at once.

**The colours are yours to pick.** **Layer colours** in the tab steps through five palettes and
your own:

| palette | the layers above the last, in order |
|---|---|
| CLASSIC (default) | green, yellow, orange, red, purple |
| COLOUR-SAFE | orange, sky blue, green, yellow, blue — told apart with any colour vision |
| ICE | white, cyan, blue, indigo, violet |
| FIRE | yellow, gold, orange, red, crimson |
| NEON | lime, cyan, magenta, yellow, pink |
| CUSTOM | your list: `[Display] HealthBarLayerColours` in the cfg |

A custom list is up to 12 colours separated by commas, each a name (green, yellow, orange, red,
purple, blue, cyan, sky blue, teal, lime, gold, crimson, pink, magenta, indigo, violet, white,
silver, grey, brown, navy, black) or a hex colour like `#FF8800`. **New Game Plus Builder.html**
has colour pickers for it and a layer count size slider, and gives you the three cfg lines to
paste. One it cannot read is skipped
and named in the log. Whatever the palette, the last layer is always the enemy's own colour, and a
colour too like the one under it is skipped, so two layers in a row never look the same. Every
change applies at once, mid-fight included.

### WORLD INFO

A **WORLD INFO** button sits straight under Continue in the pause menu, from the start of every
run. It shows the run's stats (the game-over screen's, plus which world you are in, Queens killed
across every world, and your money) and the rules in force in this world. The pause menu is hidden
while it is open, behind a nearly solid backdrop. **Esc** (**B**) closes it and leaves the game
paused.

With Enemy Traits installed, WORLD INFO lists this world's traits too, with their numbers and how
many enemies have each, and the victory screen names the next world's.

The **game-over screen** adds **New Game Plus reached** (and, past the first world, the Queens
killed across every world) under the game's own lines.

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
  closing the menu) loses nothing. The list shows nine stats at once — every weapon stat — and the
  **mouse wheel** over the panel scrolls a longer one (the footer says `1-9/15 WHEEL`). In
  SALVAGE mode the panel is shorter, so more of the vault shows under it.

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
**A** sends the highlighted module to the slot; the stick picks a stat; **Y** confirms; **B** (or
**X**) takes it out. Mouse and keyboard: drag into the slot; click a stat to pick it and again to
buy it (or press **R**); right-click the slot (or **X**) to take it out; **G** switches the mode.
While the SALVAGE tab is open, a pad's **A** fills the slot rather than moving a module to your
ship — use the VAULT tab for that.

**Every scrap asks** (Y / R), and an upgraded item or a ranked gun (Weapon Forge) asks a second
time and says what is lost. (Quick salvage, which skipped the ask, was removed after Round 21: a
quick scrap left the item in your hand, so it could be put back and the Ex kept.)

**Every number is a setting.** **Salvage Builder.html** (in this repo) edits all of them, with
presets and a price table: the scrap payout, the refund, the shared ladder, every stat's own
amount and its own ladder, and an optional per-shot cost for the added explosion and spark. It
makes `BepInEx\config\com.sugarheady.gamemodeforge.salvageshop.rules.json`. The file is re-read
when it changes — no restart.

**Saving and New Game Plus.** Upgrades are saved beside the save (`salvage.txt`, deleted with it)
and come back on a Continue. They carry across a New Game Plus jump with their items. With
`SalvageShop.dll` switched off a save still loads — the items are simply un-upgraded — and
switching it back on brings the upgrades back.

## Enemy Traits (a separate DLL)

`EnemyTraits.dll`. New Game Plus's third stage: things enemies can do that make a world harder. With
the shipped rules all four are on **every** enemy, the Queens included, from **NEW GAME PLUS 1** —
the first world is the game as it comes.

- **Wind-up** — the longer an enemy fires, the better its guns get: full after 2 s of firing, x1.5
  fire rate and half the scatter, the fire rate +0.25 a world (up to x3). It winds down over a
  second once the enemy stops.
- **Overload** — a hidden meter fills over 4 s of firing (or with its shots, or its hits on you).
  Full, the enemy overloads: x2 fire rate and half the scatter for 3 s, fading, the fire rate +0.25 a
  world (up to x4). No overheat, no lockout — only the good half. Between overloads the meter cools
  after a second of not firing.
- **Grow** — enemy shots swell as they fly, hitbox included: x1 to x2 over the shot's range (a
  rocket's or lobbed shot's: over its life), the end size +0.5 a world (up to x4). Size only; damage is New Game Plus's enemy damage.
- **Ricochet** — enemy shots bounce off walls: 1 bounce, +1 a world (up to 5). Only a wall is a
  bounce: touching you, your drones, minions or wingmen ends the shot as usual. Rockets and lobbed
  shots bounce too. A rocket that bounces **loses its lock** on you and flies off the wall; with
  **toward you** on (off by default) bullets and rockets turn back toward you after a bounce. The
  two bullets that already bounce, and the Queen's and the bombers' drifting mines, keep their own.

### Volleys

Some enemies fire **scripted volleys** on a timer of their own — the Queen's bullet patterns, the
crosses, the Fly Zapper. While a volley pattern runs, the enemy counts as firing for wind-up and
overload, gaps included. With **speed** (the default) a wound-up or overloading enemy's patterns
come faster too; **count** leaves their pace alone; **off** leaves volleys out of the meters.

### Fire rate on beams and ammo

A meter's fire-rate reward also makes an enemy **beam** hit you more often (a beam's damage comes in
steps, not shots), and an enemy gun that runs on **ammo** cheaper a shot — the AI empties its tank
and waits for it, so otherwise a faster gun would only empty sooner.

### Who gets them

Each trait has a **coverage**, the share of enemies that get it (100% by default), fixed per enemy,
so the same enemy keeps the same traits after a Continue. The optional **limit** (off by default)
caps how many traits one enemy can have. Your own drones and minions never get a trait; a bullet
you knock back keeps its grow and its bounces. Nothing on screen tells you an enemy is wound up or
overloading — switch on the **overload gauge** in the tab to watch the meters.

### The rules file

**Enemy Traits Builder.html** (in this repo) edits every number, each with a value, what it gains a
world, and a cap, plus the rewards (Module Forge's windup rewards: fire rate, accuracy, damage,
bullets, cost, speed, range, size, burst, burn, blast, push, warm-up, lifetime). It makes
`BepInEx\config\com.sugarheady.gamemodeforge.enemytraits.rules.json`. Presets: **DEFAULT**, **A NEW
TRAIT EACH PLUS** (wind-up from NEW GAME PLUS 1, grow from 2, ricochet from 3, overload from 4),
**TEST** (all four from the first world, strong and quick - to try them without killing four Queens),
**FLAT** (all four from the first world, the shipped numbers)
and **OFF**.

A world's traits are fixed when it begins — at each New Game Plus jump, or at the start of a run —
and kept with the save, so an edit applies to the next world.

### Without New Game Plus

Enemy Traits works alone, as a flat "harder enemies" mode: without New Game Plus every world is
plus 0, so only traits set to start at plus 0 come — pick **FLAT** in the builder page. With the
shipped rules and no New Game Plus there are no traits at all, and the log and the tab rows say so.

### Saving

Save and quit, relaunch, Continue: the same traits on the same enemies. With `EnemyTraits.dll`
switched off a save still loads, with the enemies as the game made them.

## Leaderboard (a separate DLL)

`Leaderboard.dll`. New Game Plus's fourth stage: a board of your finished runs, kept on your PC.
The main menu gets a **LEADERBOARD** button in its bottom row, beside SETTINGS.

### What goes on it

A run goes on the board when it **ends**: in a **death**, or with **END RUN** on New Game Plus's
victory screen. A pause-menu Restart, or a New Game over an old save, is not a finished run and is
left off. A death after KEEP PLAYING counts like any death, and its row says the world was cleared.

Each row keeps:

- the **world** reached (WORLD 1, NEW GAME PLUS 1, ...) - with a note when New Game Plus's
  first-world switch ran the rules a step ahead;
- the **score** - the money you held when the run ended, the game's own score (spending lowers it);
- the run **time**, **enemies killed**, **stations unlocked** and **Queens killed** - across every
  world of a New Game Plus run, whatever its "run time, enemies killed" carry switch says;
- **solo or co-op**, when it ended and **how** (died - with the game's death count - or END RUN),
  the **starting loadout**, and which **Forge mods** were running (WF, MF, NG+, ET, SALVAGE).

### The board

Ranked by the **furthest world**, then the **score** (then the shorter time, then the older run).
Without New Game Plus every run is world 1, so it is the score. The top 100 are drawn; every run is
kept. Scroll with the mouse wheel, or on a pad select the scrollbar and press up / down. Esc, B or
BACK closes it.

### Its file

`BepInEx\config\com.sugarheady.gamemodeforge.leaderboard.runs.json` - outside the save folder,
which every end of a run deletes. Delete the file to clear the board. A file it cannot read is never
deleted: it is renamed aside (`...runs.json.bad-<date>`), the log says so, and a new board begins.

### Saving

Save and quit, relaunch, Continue: the run's row still counts every world it reached, and its
loadout (the Leaderboard keeps a small record of its own in the save folder). With `Leaderboard.dll`
switched off a save still loads; that run is simply not counted.

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
`EnemyTraits.dll`, `Leaderboard.dll`, `SalvageShop.dll` and `PauseOptions.dll` beside it if you want them. Each works with the other Forge mods present or
absent — there is no hard dependency in any direction.

## Settings

`BepInEx\config\com.sugarheady.gamemodeforge.cfg`, generated on first run. One switch per mode
under `[Modes]`, and **every mode ships off**. The file is followed while the game runs, so a mode
can be changed between runs without restarting; a rule that latches at run entry says so in the log
rather than appearing to do nothing.

Pause Options has one setting, its own on/off switch: `[General] Enabled` in
`BepInEx\config\com.sugarheady.gamemodeforge.pauseoptions.cfg`. The settings tab edits it for you.

New Game Plus has two files beside it: `com.sugarheady.gamemodeforge.newgameplus.cfg` (its on/off
switch, what carries, the marker, health bars in layers and their colours) and `com.sugarheady.gamemodeforge.newgameplus.rules.json` (how
much tougher each world is — made by New Game Plus Builder.html, or the tab's sliders). A rules
file without an `enemyDamage` section (written before enemy damage existed) gets enemy damage's
default, and the log says so.

Enemy Traits has two files beside it too: `com.sugarheady.gamemodeforge.enemytraits.cfg` (its
on/off switch, the marker and the overload gauge) and
`com.sugarheady.gamemodeforge.enemytraits.rules.json` (the traits — made by Enemy Traits
Builder.html, or the tab's rows). A rules file that is not valid JSON means no traits that world,
said loudly in the log.

The Leaderboard has its cfg, `com.sugarheady.gamemodeforge.leaderboard.cfg` (its on/off switch and
`[Leaderboard] RecordRuns`), and the board itself, `com.sugarheady.gamemodeforge.leaderboard.runs.json`.

Salvage Shop has two files beside it too: `com.sugarheady.gamemodeforge.salvageshop.cfg` (its
on/off switch) and `com.sugarheady.gamemodeforge.salvageshop.rules.json` (every
number — made by Salvage Builder.html, or the tab's rows). A rules file that is not valid JSON is
refused: the last rules that read are kept, and the SALVAGE screen says the file is unreadable.

## The log

Game Mode Forge writes its own log, `BepInEx\GameModeForge.log` (the launch before is kept as
`GameModeForge.prev.log`), and so does every mode it registers. `LogOutput.log` stays readable for
BepInEx and other mods: it gets only Game Mode Forge's load lines and a copy of every error. If the
game itself throws an error whose stack trace names this mod, a copy goes in `GameModeForge.log` —
BepInEx leaves the game's own errors out of `LogOutput.log` by default. Set
`[Logging] OwnLogFile = false` to put everything back in `LogOutput.log`.

Pause Options', New Game Plus's, Enemy Traits', the Leaderboard's and Salvage Shop's in-game lines
go into `GameModeForge.log` too (under `GameModeForge.PauseOptions`, `GameModeForge.NewGamePlus`,
`GameModeForge.EnemyTraits`, `GameModeForge.Leaderboard` and `GameModeForge.SalvageShop`); without
Game Mode Forge installed they go to `BepInEx\PauseOptions.log`, `BepInEx\NewGamePlus.log`,
`BepInEx\EnemyTraits.log`, `BepInEx\Leaderboard.log` and `BepInEx\SalvageShop.log` instead. A run
that goes on the board is one line: `ON THE LEADERBOARD, place 3 of 12: ...`.

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
