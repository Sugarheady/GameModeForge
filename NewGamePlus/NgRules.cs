using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameModeForge.NewGamePlus
{
    // One buff's schedule. Every buff has the same controls (CONCEPT.md 2.4):
    // on/off, the plus it starts at, a start value, an amount added per plus, a
    // cap - and, for a buff given to SOME enemies, the coverage ramp.
    public sealed class NgBuff
    {
        public bool enabled;
        public int fromPlus;
        public float start;
        public float perPlus;
        public float max;
        public float coverageStart;
        public float coveragePerPlus;

        // Enemy damage only: does the world's own damage (hazard blocks, the
        // white bulbs, exploding plants, steam) take the % too? (Never crates -
        // they are nobody's, his call 2026-10-02.)
        public bool environment = true;

        public NgBuff Clone()
        {
            return (NgBuff)MemberwiseClone();
        }

        public float ValueAt(int plus)
        {
            return NgMath.Value(enabled, fromPlus, start, perPlus, max, plus);
        }

        public float CoverageAt(int plus)
        {
            return NgMath.Coverage(enabled, fromPlus, coverageStart, coveragePerPlus, plus);
        }
    }

    // A WHOLE-NUMBER buff - extra projectiles and burst shots (stage 2, his
    // answer 2026-10-05): `add` more from `fromPlus`, and `add` more again
    // every `every` pluses after it, up to `max` (0 = none). With coverage,
    // like every weapon stat.
    public sealed class NgStepBuff
    {
        public bool enabled;
        public int fromPlus = 1;
        public int add = 1;
        public int every = 1;
        public int max;
        public float coverageStart = 100f;
        public float coveragePerPlus;

        public NgStepBuff Clone()
        {
            return (NgStepBuff)MemberwiseClone();
        }

        public int ValueAt(int plus)
        {
            return NgMath.StepValue(enabled, fromPlus, add, every, max, plus);
        }

        public float CoverageAt(int plus)
        {
            return ValueAt(plus) > 0 ? NgMath.Coverage(enabled, fromPlus, coverageStart, coveragePerPlus, plus) : 0f;
        }
    }

    // ENEMY WEAPON STATS - stage 2's second piece (built 2026-10-05, his go:
    // "go ahead and build the enemy weapon stats"). DECIDED (CONCEPT.md 2.5
    // and section 7):
    //   - damage is NOT here - enemy damage % is the one damage control;
    //   - each stat is its own buff with its own roll, in the file and the
    //     builder page only, OFF by default;
    //   - coverage defaults to 100% (answer 2), except extra projectiles: 25%,
    //     rising 25% a plus ("yes 25% rising each ngp, adjustable");
    //   - projectiles and burst are whole numbers, "N every K pluses";
    //   - BURN makes the guns that already burn hotter (%), IGNITE gives every
    //     other gun a burn of its own a hit (his "Both, as two stats");
    //   - the five minion and mine launchers are left alone (his "Leave them
    //     alone") - what they launch is an enemy with buffs of its own.
    // knockbackForce is NOT a stat: measured, it is recoil on the SHOOTER
    // (`Shooter` pushes its own body back by it), so buffing it would only
    // shove the enemy away from you.
    public sealed class NgWeaponRules
    {
        public NgBuff fireRate;
        public NgBuff reach;
        public NgBuff speed;
        public NgBuff push;
        public NgBuff burn;
        public NgBuff ignite;          // a flat burn a hit, not a percent
        public NgStepBuff projectiles;
        public NgStepBuff burst;

        // ★ STAGE 3 (2026-10-12, CONCEPT.md 3.6): a scripted VOLLEY - the
        // Queen's bullet patterns, 12 crosses, the Fly Zapper - presses
        // `Shooter.Shoot()` at its own pace, so the fire-rate buff never
        // reached it. With this on (the default, his "option to disable"), the
        // buff speeds a volley pattern up by the same multiplier. A world
        // locked before it existed keeps playing without it.
        public bool volleys = true;

        public static readonly string[] PercentNames = { "fireRate", "reach", "speed", "push", "burn", "ignite" };
        public static readonly string[] StepNames = { "projectiles", "burst" };

        private static NgBuff Pct(float each)
        {
            return new NgBuff
            {
                enabled = false, fromPlus = 1, start = each, perPlus = each, max = 0f,
                coverageStart = 100f, coveragePerPlus = 0f
            };
        }

        public static NgWeaponRules Defaults()
        {
            return new NgWeaponRules
            {
                fireRate = Pct(20f),
                reach = Pct(20f),
                speed = Pct(15f),
                push = Pct(25f),
                burn = Pct(50f),
                ignite = Pct(1f),
                projectiles = new NgStepBuff
                {
                    enabled = false, fromPlus = 1, add = 1, every = 1, max = 0,
                    coverageStart = 25f, coveragePerPlus = 25f
                },
                burst = new NgStepBuff
                {
                    enabled = false, fromPlus = 1, add = 1, every = 1, max = 0,
                    coverageStart = 100f, coveragePerPlus = 0f
                }
            };
        }

        public NgWeaponRules Clone()
        {
            return new NgWeaponRules
            {
                fireRate = fireRate.Clone(), reach = reach.Clone(), speed = speed.Clone(),
                push = push.Clone(), burn = burn.Clone(), ignite = ignite.Clone(),
                projectiles = projectiles.Clone(), burst = burst.Clone(),
                volleys = volleys
            };
        }

        public NgBuff Percent(string name)
        {
            switch (name)
            {
                case "fireRate": return fireRate;
                case "reach": return reach;
                case "speed": return speed;
                case "push": return push;
                case "burn": return burn;
                case "ignite": return ignite;
            }

            return null;
        }

        public NgStepBuff Step(string name)
        {
            return name == "projectiles" ? projectiles : name == "burst" ? burst : null;
        }

        // A percent stat's value this world.
        public float ValueOf(string name, int plus)
        {
            NgBuff b = Percent(name);
            return b != null ? b.ValueAt(plus) : 0f;
        }

        // The share of enemies a stat reaches this world (a stat with no
        // strength covers nobody).
        public float CoverageOf(string name, int plus)
        {
            NgBuff b = Percent(name);

            if (b != null)
                return b.ValueAt(plus) > 0f ? b.CoverageAt(plus) : 0f;

            NgStepBuff s = Step(name);
            return s != null ? s.CoverageAt(plus) : 0f;
        }

        public bool AnyAt(int plus)
        {
            foreach (string n in PercentNames)
                if (CoverageOf(n, plus) > 0f)
                    return true;

            foreach (string n in StepNames)
                if (CoverageOf(n, plus) > 0f)
                    return true;

            return false;
        }

        // "+20% fire rate", "+1 projectile (on 25% of them)" - one rule each.
        public void AddSummary(List<string> parts, int plus)
        {
            Line(parts, "fireRate", plus, NgMath.Pct(ValueOf("fireRate", plus)) + " fire rate");
            Line(parts, "reach", plus, NgMath.Pct(ValueOf("reach", plus)) + " reach");
            Line(parts, "speed", plus, NgMath.Pct(ValueOf("speed", plus)) + " shot speed");
            Line(parts, "push", plus, NgMath.Pct(ValueOf("push", plus)) + " push");
            Line(parts, "burn", plus, NgMath.Pct(ValueOf("burn", plus)) + " burn");
            Line(parts, "ignite", plus, "shots set you alight (" +
                 Math.Round(ValueOf("ignite", plus), 1).ToString(CultureInfo.InvariantCulture) + " burn a hit)");

            int p = projectiles.ValueAt(plus);
            Line(parts, "projectiles", plus, "+" + p + (p == 1 ? " projectile" : " projectiles"));

            int b = burst.ValueAt(plus);
            Line(parts, "burst", plus, "+" + b + (b == 1 ? " burst shot" : " burst shots"));
        }

        private void Line(List<string> parts, string name, int plus, string text)
        {
            float c = CoverageOf(name, plus);

            if (c <= 0f)
                return;

            parts.Add(text + (c < 100f
                ? " (on " + Math.Round(c).ToString(CultureInfo.InvariantCulture) + "% of them)"
                : ""));
        }
    }

    // THE STATION UNLOCK COST - stage 2's third piece (built 2026-10-05, his
    // go: "go ahead and build the station unlock cost"). DECIDED (CONCEPT.md
    // 2.3 and section 7): opt-in, OFF by default; the first station you pay
    // for costs 100 money and each after it 300 more; the ladder starts again
    // in every world and the rise grows 100 each plus; from plus 1; one ladder
    // for both station kinds; the station you start beside is free; and the
    // safety net (a station you cannot afford opens free when your fuel is
    // critical or a co-op partner is down) is an option, OFF by default - his
    // words, "that incentives just waiting around the station until your low".
    // One ingredient a station takes as well as money (his ask the same day:
    // "an option to include resource cost to unlocking stations ... Default is
    // none"). `id` is the game's ingredient id or its display name ("Coral" or
    // "Fiber"), matched when a station is priced.
    public sealed class NgStationIngredient
    {
        public string id = "";
        public int amount = 1;
        public int perStation;
        public int perPlus = 1;
        public int max;

        public NgStationIngredient Clone()
        {
            return (NgStationIngredient)MemberwiseClone();
        }

        public int AmountAt(int fromPlus, int plus, int paid)
        {
            return NgMath.IngredientCost(amount, perStation, perPlus, max, fromPlus, plus, paid);
        }
    }

    public sealed class NgStationRules
    {
        public bool enabled;
        public int fromPlus = 1;
        public int first = 100;
        public int rise = 300;
        public int risePerPlus = 100;
        public bool safetyNet;

        // DECIDED: none by default.
        public List<NgStationIngredient> ingredients = new List<NgStationIngredient>();

        public NgStationRules Clone()
        {
            var c = (NgStationRules)MemberwiseClone();
            c.ingredients = new List<NgStationIngredient>();

            foreach (NgStationIngredient i in ingredients)
                c.ingredients.Add(i.Clone());

            return c;
        }

        public bool ActiveAt(int plus)
        {
            return enabled && plus >= fromPlus;
        }

        public int RiseAt(int plus)
        {
            return NgMath.StationRise(rise, risePerPlus, fromPlus, plus);
        }

        // What the next station costs, with `paid` already paid for this world.
        public int PriceAt(int plus, int paid)
        {
            return NgMath.StationPrice(first, rise, risePerPlus, fromPlus, plus, paid);
        }
    }

    // MONEY DROPS - stage 2 (DECIDED 2026-09-29: "Yes please! fantastic
    // idea!", and 2026-10-02): opt-in, OFF by default; MORE PICKUPS, using
    // the game's own 10 / 20 / 50; +25% a plus; no default cap ("Obviously the
    // game is going to get harder and harder"); every source pays it, with an
    // on/off per source - enemies, the Queens, crates and boxes, the world
    // itself (gold in the rock, the Wheat plant). Money a Weapon Forge or
    // Module Forge kill reward pays is that mod's own and is not scaled.
    public sealed class NgMoneyRules
    {
        public bool enabled;
        public int fromPlus = 1;
        public float start = 25f;
        public float perPlus = 25f;
        public float max;              // 0 = no cap
        public bool enemies = true;
        public bool queens = true;
        public bool crates = true;
        public bool world = true;

        public NgMoneyRules Clone()
        {
            return (NgMoneyRules)MemberwiseClone();
        }

        public float PercentAt(int plus)
        {
            return NgMath.Value(enabled, fromPlus, start, perPlus, max, plus);
        }

        public bool Pays(int source)
        {
            switch (source)
            {
                case NgMath.MoneyEnemies: return enemies;
                case NgMath.MoneyQueens: return queens;
                case NgMath.MoneyCrates: return crates;
                default: return world;
            }
        }

        // "every source", or the ones that pay.
        public string SourcesText()
        {
            var on = new List<string>();

            for (int s = 0; s < NgMath.MoneySourceNames.Length; s++)
                if (Pays(s))
                    on.Add(NgMath.MoneySourceNames[s]);

            return on.Count == NgMath.MoneySourceNames.Length ? "every source"
                : on.Count == 0 ? "no source" : string.Join(", ", on.ToArray());
        }
    }

    // HARDER ENEMIES EARLIER - stage 2, the world-generation piece (DECIDED
    // 2026-10-02: "Make both options available", BOTH OFF by default; and
    // 2026-10-05, the defaults when on: "sounds good to me"). Two dials, each a
    // percent of the way, rising each plus, capped at 100:
    //   budget     - the rooms near the start move toward the outer rooms'
    //                spawn budget (25% a plus: all the way by plus 4);
    //   outerKinds - each "outer rooms only" line on an enemy kind moves toward
    //                the centre (25% a plus: gone by plus 4).
    // Both stack with enemy count; a room still stops at the first enemy it
    // cannot fit.
    public sealed class NgHarderRules
    {
        public NgBuff budget;
        public NgBuff outerKinds;

        public static NgHarderRules Defaults()
        {
            return new NgHarderRules { budget = Dial(), outerKinds = Dial() };
        }

        private static NgBuff Dial()
        {
            return new NgBuff
            {
                enabled = false, fromPlus = 1, start = 25f, perPlus = 25f, max = 100f,
                coverageStart = 100f, coveragePerPlus = 0f
            };
        }

        public NgHarderRules Clone()
        {
            return new NgHarderRules { budget = budget.Clone(), outerKinds = outerKinds.Clone() };
        }

        // Each dial's share of the way this world, 0 - 1.
        public float BudgetAt(int plus)
        {
            return NgMath.DialFraction(budget.ValueAt(plus));
        }

        public float OuterAt(int plus)
        {
            return NgMath.DialFraction(outerKinds.ValueAt(plus));
        }

        public bool AnyAt(int plus)
        {
            return BudgetAt(plus) > 0f || OuterAt(plus) > 0f;
        }
    }

    // HIS ENEMY TABLE - stage 2 (DECIDED 2026-10-05: "lets try it"): every
    // biome, every kind the world generator places, other biomes' included,
    // each with "may appear here from plus N". One row a kind: its weight when
    // it is brought into a biome that is not its own (0 = the weight it has at
    // home), its cost there, and the plus it opens in each biome. In its OWN
    // biome an entry drops its "outer rooms only" line from that plus on; a
    // kind left blank in its own biome follows dial B. ★ Since 2026-10-06 (his
    // ask) every OTHER enemy in the game may be a row too, the Queen included:
    // no home, so weight 0 (never drawn) until its row gives one.
    public sealed class NgTableRow
    {
        public string kind = "";
        public float weight;

        // Its cost against a room's budget where it is new (0 = its home
        // cost, or NgMath.DefaultTablePower for a kind with no home biome).
        public int power;

        public Dictionary<string, int> from = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public NgTableRow Clone()
        {
            var c = (NgTableRow)MemberwiseClone();
            c.from = new Dictionary<string, int>(from, StringComparer.OrdinalIgnoreCase);
            return c;
        }
    }

    public sealed class NgEnemyTable
    {
        public bool enabled;
        public List<NgTableRow> rows = new List<NgTableRow>();

        public NgEnemyTable Clone()
        {
            var c = new NgEnemyTable { enabled = enabled };

            foreach (NgTableRow r in rows)
                c.rows.Add(r.Clone());

            return c;
        }

        // How many (kind, biome) places are open this world.
        public int OpenAt(int plus)
        {
            if (!enabled)
                return 0;

            int n = 0;

            foreach (NgTableRow r in rows)
                foreach (int f in r.from.Values)
                    if (NgMath.TableOpen(f, plus))
                        n++;

            return n;
        }

        public bool AnyAt(int plus)
        {
            return OpenAt(plus) > 0;
        }
    }

    // THE RULES FILE - `BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.rules.json`,
    // made by `New Game Plus Builder.html` (DECIDED: both files live in
    // BepInEx\config, section 4). The in-game tab's two sliders write the same
    // file (NgTab), so there is one place each number lives.
    //
    // ★ READ WHEN THE RULES LOCK, NOT AT STARTUP. A world's rules are locked
    // when it begins - at a jump for a new plus, at the run's start for plus 0 -
    // and kept in the save's own NG+ file after that (2.6). So an edit made
    // with the game running applies from the NEXT jump or run, with no restart,
    // and never changes a world already being played.
    //
    // ★ A BAD FILE MEANS NO RULES, SAID LOUDLY. Not the defaults: a world
    // quietly built on numbers the player did not write is the silent-fallback
    // bug this project has paid for most. This mod only ever ADDS difficulty to
    // a game that works without it, so every doubt resolves to "add nothing".
    public sealed class NgRules
    {
        // 2: enemyDamage (2026-10-02). A version-1 file is read as it is, and
        // its missing enemyDamage takes the default - see Parse.
        // 3: enemyWeapons (2026-10-05). Every weapon stat is OFF by default, so
        // a file without the section simply has none.
        // 4: stationCost (2026-10-05), OFF by default the same way.
        // 5: moneyDrops, harderEarlier, enemyTable, firstWorldBuffs (2026-10-06,
        // the rest of stage 2), every one OFF by default.
        // 6: enemyWeapons.volleys (2026-10-12, stage 3), ON by default - the
        // fire-rate buff reaches scripted volleys.
        public const int CurrentVersion = 6;

        public int version = CurrentVersion;
        public NgBuff enemyHealth;
        public NgBuff enemyCount;
        public NgBuff enemyDamage;
        public NgWeaponRules enemyWeapons = NgWeaponRules.Defaults();
        public NgStationRules stationCost = new NgStationRules();
        public NgMoneyRules moneyDrops = new NgMoneyRules();
        public NgHarderRules harderEarlier = NgHarderRules.Defaults();
        public NgEnemyTable enemyTable = new NgEnemyTable();

        // HIS FIRST-WORLD SWITCH (2026-10-02): the whole chain one step on - the
        // first world plays by NEW GAME PLUS 1's rules. Read from the FILE when
        // a run starts and kept for the whole chain (NgRun.Ahead), like every
        // rule it locks with the run.
        public bool firstWorldBuffs;

        // Where the file lives. Resolved through BepInEx, never hard-coded.
        public static string FilePath
        {
            get
            {
                return Path.Combine(Paths.ConfigPath,
                                    NewGamePlusPlugin.Guid + ".rules.json");
            }
        }

        // DECIDED defaults (2.4): the test numbers are +50% health and +30%
        // enemies; coverage starts at 25% and rises each plus to 100%. Every
        // buff starts at the first NG+ world, so the first world plays as the
        // game made it.
        //
        // DECIDED 2026-10-02: enemy health has NO cap by default (a cap of 0
        // is "none"), and enemy damage is +100% a plus on EVERY enemy - no
        // coverage, no cap - with the environment included. His words: "The
        // damage buff should affect every enemy right out the bat." Enemy
        // count keeps its cap (a room only holds so many).
        public static NgRules Defaults()
        {
            return new NgRules
            {
                enemyHealth = new NgBuff
                {
                    enabled = true, fromPlus = 1, start = 50f, perPlus = 50f, max = 0f,
                    coverageStart = 25f, coveragePerPlus = 25f
                },
                enemyCount = new NgBuff
                {
                    enabled = true, fromPlus = 1, start = 30f, perPlus = 30f, max = 300f,
                    coverageStart = 100f, coveragePerPlus = 0f
                },
                enemyDamage = new NgBuff
                {
                    enabled = true, fromPlus = 1, start = 100f, perPlus = 100f, max = 0f,
                    coverageStart = 100f, coveragePerPlus = 0f, environment = true
                },
                enemyWeapons = NgWeaponRules.Defaults(),
                stationCost = new NgStationRules(),
                moneyDrops = new NgMoneyRules(),
                harderEarlier = NgHarderRules.Defaults(),
                enemyTable = new NgEnemyTable(),
                firstWorldBuffs = false
            };
        }

        // No rule in force anywhere: what a save made before New Game Plus
        // was installed gets - it never locked any.
        public static NgRules None()
        {
            NgRules r = Defaults();
            r.enemyHealth.enabled = false;
            r.enemyCount.enabled = false;
            r.enemyDamage.enabled = false;
            return r;
        }

        public NgRules Clone()
        {
            return new NgRules
            {
                version = version,
                enemyHealth = enemyHealth.Clone(),
                enemyCount = enemyCount.Clone(),
                enemyDamage = enemyDamage.Clone(),
                enemyWeapons = enemyWeapons.Clone(),
                stationCost = stationCost.Clone(),
                moneyDrops = moneyDrops.Clone(),
                harderEarlier = harderEarlier.Clone(),
                enemyTable = enemyTable.Clone(),
                firstWorldBuffs = firstWorldBuffs
            };
        }

        // ---- the numbers for one world ---------------------------------

        public float HealthPercent(int plus)
        {
            return enemyHealth.ValueAt(plus);
        }

        public float HealthCoverage(int plus)
        {
            // A buff with no strength covers nobody, whatever its ramp says.
            return HealthPercent(plus) > 0f ? enemyHealth.CoverageAt(plus) : 0f;
        }

        // Enemy count is a WORLD-WIDE multiplier on every room's spawn budget,
        // so it has no coverage (2.5).
        public float CountPercent(int plus)
        {
            return enemyCount.ValueAt(plus);
        }

        // Enemy damage is on EVERY enemy (DECIDED 2026-10-02), so like count
        // it has no coverage: one number for the whole world.
        public float DamagePercent(int plus)
        {
            return enemyDamage.ValueAt(plus);
        }

        public bool DamageEnvironment
        {
            get { return enemyDamage.environment; }
        }

        public bool AnyAt(int plus)
        {
            return HealthCoverage(plus) > 0f || CountPercent(plus) > 0f || DamagePercent(plus) > 0f ||
                   enemyWeapons.AnyAt(plus) || stationCost.ActiveAt(plus) ||
                   moneyDrops.PercentAt(plus) > 0f || harderEarlier.AnyAt(plus) || enemyTable.AnyAt(plus);
        }

        // "enemies +100% health (on 50% of them), +60% more, +200% damage
        // (hazards too)", for the arrival alert, the victory screen and WORLD
        // INFO.
        public string Summary(int plus)
        {
            List<string> parts = SummaryParts(plus);
            List<string> world = WorldParts(plus);

            if (parts.Count == 0 && world.Count == 0)
                return "no rules in force";

            string enemies = parts.Count > 0 ? "enemies " + string.Join(", ", parts.ToArray()) : "";

            return world.Count == 0
                ? enemies
                : (enemies.Length > 0 ? enemies + "; " : "") + string.Join("; ", world.ToArray());
        }

        // The rules that are not about enemies ("stations cost 100 money, +300
        // more each") - the victory screen gives each its own line, without the
        // "enemies" in front.
        public List<string> WorldParts(int plus)
        {
            var parts = new List<string>();

            if (stationCost.ActiveAt(plus))
            {
                var also = new List<string>();

                // What the FIRST station of that world takes of each ingredient.
                foreach (NgStationIngredient i in stationCost.ingredients)
                {
                    int n = i.AmountAt(stationCost.fromPlus, plus, 0);

                    if (n > 0 && !string.IsNullOrEmpty(i.id))
                        also.Add(n.ToString(CultureInfo.InvariantCulture) + " " + NgMath.IngredientName(i.id) +
                                 (i.perStation > 0 ? " (+" + i.perStation.ToString(CultureInfo.InvariantCulture) + " each)" : ""));
                }

                parts.Add("stations cost " + stationCost.first.ToString(CultureInfo.InvariantCulture) +
                          " money, +" + stationCost.RiseAt(plus).ToString(CultureInfo.InvariantCulture) +
                          " more each" + (also.Count > 0 ? ", and " + string.Join(", ", also.ToArray()) : "") +
                          (stationCost.safetyNet ? " (free when you are stranded)" : ""));
            }

            float money = moneyDrops.PercentAt(plus);

            if (money > 0f)
                parts.Add("money drops " + NgMath.Pct(money) + " (" + moneyDrops.SourcesText() + ")");

            // Harder enemies earlier: one line for both dials and the table.
            var harder = new List<string>();
            float a = harderEarlier.BudgetAt(plus), b = harderEarlier.OuterAt(plus);
            int open = enemyTable.OpenAt(plus);

            if (a > 0f)
                harder.Add("rooms near the start " + Math.Round(a * 100f).ToString(CultureInfo.InvariantCulture) +
                           "% of the way to the far rooms' enemies");

            if (b > 0f)
                harder.Add("far-only enemies " + Math.Round(b * 100f).ToString(CultureInfo.InvariantCulture) +
                           "% closer in");

            if (open > 0)
                harder.Add("your enemy table opens " + open.ToString(CultureInfo.InvariantCulture) +
                           (open == 1 ? " place" : " places"));

            if (harder.Count > 0)
                parts.Add("harder earlier: " + string.Join(", ", harder.ToArray()));

            return parts;
        }

        // The same, one rule at a time ("+300% health", "+300% more",
        // "+100% damage (hazards too)") - the victory screen stacks them, one
        // to a line (R21: on one line the next world's rules ran off the
        // screen).
        public List<string> SummaryParts(int plus)
        {
            var parts = new List<string>();

            float h = HealthPercent(plus);
            float hc = HealthCoverage(plus);

            if (h > 0f && hc > 0f)
            {
                parts.Add(NgMath.Pct(h) + " health" +
                          (hc < 100f ? " (on " + Math.Round(hc).ToString(CultureInfo.InvariantCulture) +
                                       "% of them)" : ""));
            }

            float c = CountPercent(plus);

            if (c > 0f)
                parts.Add(NgMath.Pct(c) + " more");

            float d = DamagePercent(plus);

            if (d > 0f)
                parts.Add(NgMath.Pct(d) + " damage" + (DamageEnvironment ? " (hazards too)" : ""));

            // ★ THE GUN STATS ARE ONE PART, wrapped, not one line each: with all
            // eight on, the victory screen's rules box (1,100 x 185) would hold
            // eleven lines and spill over its buttons.
            var guns = new List<string>();
            enemyWeapons.AddSummary(guns, plus);

            if (guns.Count > 0)
                parts.Add("with guns " + string.Join(", ", guns.ToArray()));

            return parts;
        }

        // ---- JSON --------------------------------------------------------

        public JObject ToJObject()
        {
            return new JObject
            {
                ["version"] = version,
                ["enemyHealth"] = BuffToJ(enemyHealth, true, false),
                ["enemyCount"] = BuffToJ(enemyCount, false, false),
                ["enemyDamage"] = BuffToJ(enemyDamage, false, true),
                ["enemyWeapons"] = WeaponsToJ(enemyWeapons),
                ["stationCost"] = StationToJ(stationCost),
                ["moneyDrops"] = MoneyToJ(moneyDrops),
                ["harderEarlier"] = new JObject
                {
                    ["budget"] = BuffToJ(harderEarlier.budget, false, false),
                    ["outerKinds"] = BuffToJ(harderEarlier.outerKinds, false, false)
                },
                ["enemyTable"] = TableToJ(enemyTable),
                ["firstWorldBuffs"] = firstWorldBuffs
            };
        }

        private static JObject MoneyToJ(NgMoneyRules m)
        {
            return new JObject
            {
                ["enabled"] = m.enabled,
                ["fromPlus"] = m.fromPlus,
                ["start"] = m.start,
                ["perPlus"] = m.perPlus,
                ["max"] = m.max,
                ["enemies"] = m.enemies,
                ["queens"] = m.queens,
                ["crates"] = m.crates,
                ["world"] = m.world
            };
        }

        private static JObject TableToJ(NgEnemyTable e)
        {
            var rows = new JArray();

            foreach (NgTableRow r in e.rows)
            {
                var from = new JObject();

                foreach (KeyValuePair<string, int> kv in r.from)
                    from[kv.Key] = kv.Value;

                rows.Add(new JObject { ["kind"] = r.kind, ["weight"] = r.weight, ["power"] = r.power, ["from"] = from });
            }

            return new JObject { ["enabled"] = e.enabled, ["rows"] = rows };
        }

        private static JObject StationToJ(NgStationRules s)
        {
            var list = new JArray();

            foreach (NgStationIngredient i in s.ingredients)
            {
                list.Add(new JObject
                {
                    ["id"] = i.id,
                    ["amount"] = i.amount,
                    ["perStation"] = i.perStation,
                    ["perPlus"] = i.perPlus,
                    ["max"] = i.max
                });
            }

            return new JObject
            {
                ["enabled"] = s.enabled,
                ["fromPlus"] = s.fromPlus,
                ["first"] = s.first,
                ["rise"] = s.rise,
                ["risePerPlus"] = s.risePerPlus,
                ["safetyNet"] = s.safetyNet,
                ["ingredients"] = list
            };
        }

        private static JObject WeaponsToJ(NgWeaponRules w)
        {
            var o = new JObject();

            foreach (string n in NgWeaponRules.PercentNames)
                o[n] = BuffToJ(w.Percent(n), true, false);

            foreach (string n in NgWeaponRules.StepNames)
                o[n] = StepToJ(w.Step(n));

            o["volleys"] = w.volleys;
            return o;
        }

        private static JObject StepToJ(NgStepBuff s)
        {
            return new JObject
            {
                ["enabled"] = s.enabled,
                ["fromPlus"] = s.fromPlus,
                ["add"] = s.add,
                ["every"] = s.every,
                ["max"] = s.max,
                ["coverageStart"] = s.coverageStart,
                ["coveragePerPlus"] = s.coveragePerPlus
            };
        }

        public string ToJson(bool indented)
        {
            return ToJObject().ToString(indented ? Formatting.Indented : Formatting.None);
        }

        private static JObject BuffToJ(NgBuff b, bool coverage, bool environment)
        {
            var o = new JObject
            {
                ["enabled"] = b.enabled,
                ["fromPlus"] = b.fromPlus,
                ["start"] = b.start,
                ["perPlus"] = b.perPlus,
                ["max"] = b.max
            };

            if (coverage)
            {
                o["coverageStart"] = b.coverageStart;
                o["coveragePerPlus"] = b.coveragePerPlus;
            }

            if (environment)
                o["environment"] = b.environment;

            return o;
        }

        // Parse the rules FILE. Unknown keys are reported, missing ones take
        // the default for that key (a five-line file is a valid file, the
        // Weapon Forge convention), and a value of the wrong kind is reported
        // and skipped. Returns null only when the text is not a JSON object at
        // all.
        public static NgRules Parse(string text, List<string> problems)
        {
            return Parse(text, problems, false);
        }

        // `locked`: the text is a world's LOCKED rules, out of a save's record
        // - not the file. ★ Then a buff the text does not name is OFF, never
        // its default: a world locked before a buff existed (a stage-1 save,
        // locked before enemy damage) keeps playing with the rules it was
        // locked with. Only the file takes a new buff's default.
        public static NgRules Parse(string text, List<string> problems, bool locked)
        {
            JObject o;

            try
            {
                o = JObject.Parse(text ?? "");
            }
            catch (Exception ex)
            {
                problems.Add("the file is not valid JSON (" + ex.Message + ")");
                return null;
            }

            NgRules r = locked ? None() : Defaults();

            // ★ A world locked before the volley reach existed keeps playing
            // without it (the house rule: what the record does not name is off).
            if (locked)
                r.enemyWeapons.volleys = false;

            foreach (JProperty p in o.Properties())
            {
                switch (p.Name)
                {
                    case "version":
                        int v;
                        if (TryInt(p.Value, out v))
                            r.version = v;
                        break;
                    case "enemyHealth":
                        ReadBuff(p.Value as JObject, r.enemyHealth, "enemyHealth", true, problems);
                        break;
                    case "enemyCount":
                        ReadBuff(p.Value as JObject, r.enemyCount, "enemyCount", false, problems);
                        break;
                    case "enemyDamage":
                        ReadBuff(p.Value as JObject, r.enemyDamage, "enemyDamage", false, problems);
                        break;
                    case "enemyWeapons":
                        ReadWeapons(p.Value as JObject, r.enemyWeapons, problems);
                        break;
                    case "stationCost":
                        ReadStation(p.Value as JObject, r.stationCost, problems);
                        break;
                    case "moneyDrops":
                        ReadMoney(p.Value as JObject, r.moneyDrops, problems);
                        break;
                    case "harderEarlier":
                        ReadHarder(p.Value as JObject, r.harderEarlier, problems);
                        break;
                    case "enemyTable":
                        ReadTable(p.Value as JObject, r.enemyTable, problems);
                        break;
                    case "firstWorldBuffs":
                        bool fw;
                        if (TryBool(p.Value, out fw)) r.firstWorldBuffs = fw;
                        else Bad(problems, "rules", p);
                        break;
                    default:
                        // "$..." keys are notes the builder page may write.
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add("\"" + p.Name + "\" is not a New Game Plus rule - ignored");
                        }
                        break;
                }
            }

            if (r.version > CurrentVersion)
            {
                problems.Add("the file is version " + r.version + " and this build reads " +
                             CurrentVersion + " - it is read as far as this build understands it");
            }

            return r;
        }

        private static void ReadBuff(JObject o, NgBuff b, string name, bool coverage,
                                     List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"" + name + "\" should be an object - its defaults are used");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                bool bv;
                int iv;
                float fv;

                switch (p.Name)
                {
                    case "enabled":
                        if (TryBool(p.Value, out bv)) b.enabled = bv;
                        else Bad(problems, name, p);
                        break;
                    case "fromPlus":
                        if (TryInt(p.Value, out iv)) b.fromPlus = Math.Max(0, iv);
                        else Bad(problems, name, p);
                        break;
                    case "start":
                        if (TryFloat(p.Value, out fv)) b.start = fv;
                        else Bad(problems, name, p);
                        break;
                    case "perPlus":
                        if (TryFloat(p.Value, out fv)) b.perPlus = fv;
                        else Bad(problems, name, p);
                        break;
                    case "max":
                        if (TryFloat(p.Value, out fv)) b.max = fv;
                        else Bad(problems, name, p);
                        break;
                    case "environment":
                        if (name != "enemyDamage")
                        {
                            problems.Add(name + ".environment - only enemyDamage has an " +
                                         "environment switch, so this is ignored");
                            break;
                        }
                        if (TryBool(p.Value, out bv)) b.environment = bv;
                        else Bad(problems, name, p);
                        break;
                    case "coverageStart":
                    case "coveragePerPlus":
                        if (!coverage)
                        {
                            problems.Add(name + "." + p.Name + " - " +
                                         (name == "enemyDamage"
                                             ? "enemy damage is on every enemy"
                                             : "enemy count is world-wide") +
                                         " and has no coverage, so this is ignored");
                            break;
                        }
                        if (!TryFloat(p.Value, out fv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Name == "coverageStart") b.coverageStart = fv;
                        else b.coveragePerPlus = fv;
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add(name + "." + p.Name + " is not a setting - ignored");
                        }
                        break;
                }
            }
        }

        // ★ A LOCKED record names only the stats it locked, and a stat it does
        // not name stays OFF - which every weapon stat's default is anyway.
        private static void ReadWeapons(JObject o, NgWeaponRules w, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"enemyWeapons\" should be an object - every weapon stat stays off");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                if (p.Name == "volleys")
                {
                    bool vb;
                    if (TryBool(p.Value, out vb)) w.volleys = vb;
                    else Bad(problems, "enemyWeapons", p);
                    continue;
                }

                NgBuff b = w.Percent(p.Name);
                NgStepBuff s = w.Step(p.Name);

                if (b != null)
                    ReadBuff(p.Value as JObject, b, "enemyWeapons." + p.Name, true, problems);
                else if (s != null)
                    ReadStep(p.Value as JObject, s, "enemyWeapons." + p.Name, problems);
                else if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                         !p.Name.StartsWith("_", StringComparison.Ordinal))
                {
                    problems.Add("enemyWeapons." + p.Name + " is not a weapon stat - ignored (the stats are " +
                                 string.Join(", ", NgWeaponRules.PercentNames) + ", " +
                                 string.Join(", ", NgWeaponRules.StepNames) + "; damage is enemyDamage)");
                }
            }
        }

        // Money is whole: a fraction is rounded, and said.
        private static void ReadStation(JObject o, NgStationRules s, List<string> problems)
        {
            const string name = "stationCost";

            if (o == null)
            {
                problems.Add("\"stationCost\" should be an object - it stays off");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                bool bv;
                int iv;

                switch (p.Name)
                {
                    case "enabled":
                    case "safetyNet":
                        if (!TryBool(p.Value, out bv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Name == "enabled") s.enabled = bv;
                        else s.safetyNet = bv;
                        break;
                    case "fromPlus":
                    case "first":
                    case "rise":
                    case "risePerPlus":
                        if (!TryInt(p.Value, out iv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Value.Type == JTokenType.Float && Math.Abs((double)p.Value - iv) > 1e-9)
                            problems.Add(name + "." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                                         " - money is whole, so it is " + iv);
                        if (p.Name == "fromPlus") s.fromPlus = Math.Max(0, iv);
                        else if (p.Name == "first") s.first = Math.Max(0, iv);
                        else if (p.Name == "rise") s.rise = Math.Max(0, iv);
                        else s.risePerPlus = iv;
                        break;
                    case "ingredients":
                        ReadIngredients(p.Value as JArray, s, problems);
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add(name + "." + p.Name + " is not a setting - ignored");
                        }
                        break;
                }
            }
        }

        // stationCost.ingredients: a list of { id, amount, perStation, perPlus,
        // max }. Counts are whole. An entry with no id is skipped and said; an id
        // the game does not know is said when a station is priced (the game's
        // ingredients are only known then).
        private static void ReadIngredients(JArray a, NgStationRules s, List<string> problems)
        {
            s.ingredients.Clear();

            if (a == null)
            {
                problems.Add("stationCost.ingredients should be a list - no ingredient costs");
                return;
            }

            for (int n = 0; n < a.Count; n++)
            {
                var o = a[n] as JObject;
                string where = "stationCost.ingredients[" + n + "]";

                if (o == null)
                {
                    problems.Add(where + " should be an object - skipped");
                    continue;
                }

                var i = new NgStationIngredient();

                foreach (JProperty p in o.Properties())
                {
                    int iv;

                    switch (p.Name)
                    {
                        case "id":
                            if (p.Value.Type == JTokenType.String) i.id = ((string)p.Value).Trim();
                            else Bad(problems, where, p);
                            break;
                        case "amount":
                        case "perStation":
                        case "perPlus":
                        case "max":
                            if (!TryInt(p.Value, out iv))
                            {
                                Bad(problems, where, p);
                                break;
                            }
                            if (p.Value.Type == JTokenType.Float && Math.Abs((double)p.Value - iv) > 1e-9)
                                problems.Add(where + "." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                                             " - a whole number only, so it is " + iv);
                            if (p.Name == "amount") i.amount = Math.Max(0, iv);
                            else if (p.Name == "perStation") i.perStation = iv;
                            else if (p.Name == "perPlus") i.perPlus = iv;
                            else i.max = Math.Max(0, iv);
                            break;
                        default:
                            if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                                !p.Name.StartsWith("_", StringComparison.Ordinal))
                            {
                                problems.Add(where + "." + p.Name + " is not a setting - ignored");
                            }
                            break;
                    }
                }

                if (i.id.Length == 0)
                {
                    problems.Add(where + " has no id (which ingredient?) - skipped");
                    continue;
                }

                s.ingredients.Add(i);
            }
        }

        private static void ReadMoney(JObject o, NgMoneyRules m, List<string> problems)
        {
            const string name = "moneyDrops";

            if (o == null)
            {
                problems.Add("\"moneyDrops\" should be an object - it stays off");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                bool bv;
                int iv;
                float fv;

                switch (p.Name)
                {
                    case "enabled":
                    case "enemies":
                    case "queens":
                    case "crates":
                    case "world":
                        if (!TryBool(p.Value, out bv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Name == "enabled") m.enabled = bv;
                        else if (p.Name == "enemies") m.enemies = bv;
                        else if (p.Name == "queens") m.queens = bv;
                        else if (p.Name == "crates") m.crates = bv;
                        else m.world = bv;
                        break;
                    case "fromPlus":
                        if (TryInt(p.Value, out iv)) m.fromPlus = Math.Max(0, iv);
                        else Bad(problems, name, p);
                        break;
                    case "start":
                    case "perPlus":
                    case "max":
                        if (!TryFloat(p.Value, out fv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Name == "start") m.start = fv;
                        else if (p.Name == "perPlus") m.perPlus = fv;
                        else m.max = Math.Max(0f, fv);
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add(name + "." + p.Name + " is not a setting - ignored");
                        }
                        break;
                }
            }
        }

        private static void ReadHarder(JObject o, NgHarderRules h, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"harderEarlier\" should be an object - both dials stay off");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                switch (p.Name)
                {
                    case "budget":
                        ReadBuff(p.Value as JObject, h.budget, "harderEarlier.budget", false, problems);
                        break;
                    case "outerKinds":
                        ReadBuff(p.Value as JObject, h.outerKinds, "harderEarlier.outerKinds", false, problems);
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add("harderEarlier." + p.Name + " is not a dial - ignored (the dials are " +
                                         "budget and outerKinds)");
                        }
                        break;
                }
            }
        }

        // enemyTable.rows: { kind, weight, from: { biome: plus } }. Whether a
        // kind and a biome exist is only known in the game (the generator's own
        // lists), so that is checked where the table is used and said there.
        private static void ReadTable(JObject o, NgEnemyTable e, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"enemyTable\" should be an object - it stays off");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                bool bv;

                switch (p.Name)
                {
                    case "enabled":
                        if (TryBool(p.Value, out bv)) e.enabled = bv;
                        else Bad(problems, "enemyTable", p);
                        break;
                    case "rows":
                        ReadRows(p.Value as JArray, e, problems);
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add("enemyTable." + p.Name + " is not a setting - ignored");
                        }
                        break;
                }
            }
        }

        private static void ReadRows(JArray a, NgEnemyTable e, List<string> problems)
        {
            e.rows.Clear();

            if (a == null)
            {
                problems.Add("enemyTable.rows should be a list - the table is empty");
                return;
            }

            for (int n = 0; n < a.Count; n++)
            {
                var o = a[n] as JObject;
                string where = "enemyTable.rows[" + n + "]";

                if (o == null)
                {
                    problems.Add(where + " should be an object - skipped");
                    continue;
                }

                var r = new NgTableRow();

                foreach (JProperty p in o.Properties())
                {
                    float fv;

                    switch (p.Name)
                    {
                        case "kind":
                            if (p.Value.Type == JTokenType.String) r.kind = ((string)p.Value).Trim();
                            else Bad(problems, where, p);
                            break;
                        case "weight":
                            if (TryFloat(p.Value, out fv)) r.weight = Math.Max(0f, fv);
                            else Bad(problems, where, p);
                            break;
                        case "power":
                            int pw;
                            if (TryInt(p.Value, out pw)) r.power = Math.Max(0, pw);
                            else Bad(problems, where, p);
                            break;
                        case "from":
                            var from = p.Value as JObject;

                            if (from == null)
                            {
                                Bad(problems, where, p);
                                break;
                            }

                            foreach (JProperty b in from.Properties())
                            {
                                int iv;

                                if (TryInt(b.Value, out iv)) r.from[b.Name.Trim()] = iv;
                                else problems.Add(where + ".from." + b.Name + " = " + b.Value.ToString(Formatting.None) +
                                                  " is not a plus number - skipped");
                            }
                            break;
                        default:
                            if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                                !p.Name.StartsWith("_", StringComparison.Ordinal))
                            {
                                problems.Add(where + "." + p.Name + " is not a setting - ignored");
                            }
                            break;
                    }
                }

                if (r.kind.Length == 0)
                {
                    problems.Add(where + " has no kind (which enemy?) - skipped");
                    continue;
                }

                e.rows.Add(r);
            }
        }

        // Whole numbers only (his answer): a fraction is rounded, and said.
        private static void ReadStep(JObject o, NgStepBuff s, string name, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"" + name + "\" should be an object - it stays off");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                bool bv;
                int iv;
                float fv;

                switch (p.Name)
                {
                    case "enabled":
                        if (TryBool(p.Value, out bv)) s.enabled = bv;
                        else Bad(problems, name, p);
                        break;
                    case "fromPlus":
                    case "add":
                    case "every":
                    case "max":
                        if (!TryInt(p.Value, out iv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Value.Type == JTokenType.Float && Math.Abs((double)p.Value - iv) > 1e-9)
                            problems.Add(name + "." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                                         " - a whole number only, so it is " + iv);
                        if (p.Name == "fromPlus") s.fromPlus = Math.Max(0, iv);
                        else if (p.Name == "add") s.add = Math.Max(0, iv);
                        else if (p.Name == "every") s.every = Math.Max(1, iv);
                        else s.max = Math.Max(0, iv);
                        break;
                    case "coverageStart":
                        if (TryFloat(p.Value, out fv)) s.coverageStart = fv;
                        else Bad(problems, name, p);
                        break;
                    case "coveragePerPlus":
                        if (TryFloat(p.Value, out fv)) s.coveragePerPlus = fv;
                        else Bad(problems, name, p);
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add(name + "." + p.Name + " is not a setting - ignored");
                        }
                        break;
                }
            }
        }

        private static void Bad(List<string> problems, string name, JProperty p)
        {
            problems.Add(name + "." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                         " is the wrong kind of value - its default is used");
        }

        private static bool TryBool(JToken t, out bool v)
        {
            v = false;

            if (t == null)
                return false;

            if (t.Type == JTokenType.Boolean)
            {
                v = (bool)t;
                return true;
            }

            return false;
        }

        private static bool TryInt(JToken t, out int v)
        {
            v = 0;

            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                return false;

            double d = (double)t;

            if (double.IsNaN(d) || double.IsInfinity(d) || d > int.MaxValue || d < int.MinValue)
                return false;

            v = (int)Math.Round(d);
            return true;
        }

        private static bool TryFloat(JToken t, out float v)
        {
            v = 0f;

            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                return false;

            double d = (double)t;

            if (double.IsNaN(d) || double.IsInfinity(d))
                return false;

            v = (float)d;
            return true;
        }

        // ---- the file ----------------------------------------------------

        // The rules as the file says them NOW. A missing file is written with
        // the defaults first (so there is something to open and edit); a file
        // that cannot be read means NO rules this time, and the log says why.
        public static NgRules LoadFile()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                {
                    NgRules d = Defaults();
                    WriteFile(d.ToJObject());
                    NewGamePlusPlugin.Log.LogInfo(
                        "no rules file yet - wrote the defaults to BepInEx\\config\\" +
                        Path.GetFileName(path) + " (enemy health " + NgMath.Pct(d.enemyHealth.start) +
                        " a plus, enemy count " + NgMath.Pct(d.enemyCount.start) +
                        " a plus, enemy damage " + NgMath.Pct(d.enemyDamage.start) +
                        " a plus, all from NEW GAME PLUS 1). Edit it with New Game " +
                        "Plus Builder.html, or the sliders in the GAME MODE FORGE tab.");
                    return d;
                }

                string text = File.ReadAllText(path);
                var problems = new List<string>();
                NgRules r = Parse(text, problems);

                foreach (string p in problems)
                    NewGamePlusPlugin.Log.LogWarning("rules file: " + p);

                // A file written before enemy damage existed. Missing keys take
                // their defaults (the house convention), so it is ON - say so,
                // once a session, because the file itself does not show it.
                if (r != null && !_saidNoDamage && text.IndexOf("\"enemyDamage\"", StringComparison.Ordinal) < 0)
                {
                    _saidNoDamage = true;
                    NewGamePlusPlugin.Log.LogInfo(
                        "rules file: it has no enemyDamage section (it was written before enemy " +
                        "damage existed), so enemy damage takes its default - " +
                        NgMath.Pct(r.enemyDamage.start) + " a plus from " +
                        NgMath.WorldName(r.enemyDamage.fromPlus) + ", hazards too. Change it with " +
                        "the Enemy damage slider in the GAME MODE FORGE tab or New Game Plus Builder.html.");
                }

                if (r == null)
                {
                    NewGamePlusPlugin.Log.LogError(
                        "COULD NOT READ the rules file (" + Path.GetFileName(path) + ") - this " +
                        "world gets NO New Game Plus rules. Fix or delete the file (a deleted " +
                        "file is rewritten with the defaults) and the next world picks it up.");
                    return None();
                }

                return r;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT READ the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - this world gets NO New Game Plus rules.");
                return None();
            }
        }

        // Write one headline number (the in-game sliders): both `start` and
        // `perPlus` of that buff, so "+50% a plus" means +50%, +100%, +150%,
        // and `enabled` follows (0 is off). Every other key in the file -
        // including ones the builder page wrote that this build does not know -
        // is kept exactly as it was.
        public static bool SetHeadline(string buff, float percent)
        {
            try
            {
                JObject o;

                // A file that is not JSON is refused, and the slider goes back.
                if (!ReadForEdit(out o))
                    return false;

                JObject b = BuffObject(o, buff);

                b["start"] = percent;
                b["perPlus"] = percent;
                b["enabled"] = percent > 0f;

                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: " + buff + " set to " + NgMath.Pct(percent) +
                    " a plus from the settings tab - it applies from the next world " +
                    "(this one keeps the rules it started with).");

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the slider has NOT been saved.");
                return false;
            }
        }

        // The tab's "Enemy damage: hazards too" row: enemyDamage.environment.
        // Every other key in the file is kept, as for the sliders.
        public static bool SetEnvironment(bool on)
        {
            try
            {
                JObject o;

                if (!ReadForEdit(out o))
                    return false;

                BuffObject(o, "enemyDamage")["environment"] = on;
                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: enemy damage on hazards " + (on ? "ON" : "OFF") +
                    " from the settings tab - it applies from the next world.");

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the switch has NOT been saved.");
                return false;
            }
        }

        // The tab's "Station unlock cost" row: stationCost.enabled. A rule of the
        // world, so it applies from the next one. Every other key is kept.
        public static bool SetStationCost(bool on)
        {
            try
            {
                JObject o;

                if (!ReadForEdit(out o))
                    return false;

                JObject s = o["stationCost"] as JObject;

                if (s == null)
                {
                    s = StationToJ(new NgStationRules());
                    o["stationCost"] = s;
                }

                s["enabled"] = on;
                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: station unlock cost " + (on ? "ON" : "OFF") +
                    " from the settings tab - it applies from the next world.");

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the switch has NOT been saved.");
                return false;
            }
        }

        // The tab's "Money drops" row: moneyDrops.enabled.
        public static bool SetMoney(bool on)
        {
            return SetFlag("moneyDrops", "enabled", on, MoneyToJ(new NgMoneyRules()), "money drops");
        }

        // The tab's "Buffs from the first world" row: firstWorldBuffs. Read when
        // a run starts (it shifts the whole chain).
        public static bool SetFirstWorld(bool on)
        {
            return SetFlag(null, "firstWorldBuffs", on, null, "buffs from the first world");
        }

        private static bool SetFlag(string section, string key, bool on, JObject fresh, string what)
        {
            try
            {
                JObject o;

                if (!ReadForEdit(out o))
                    return false;

                JObject s = o;

                if (section != null)
                {
                    s = o[section] as JObject;

                    if (s == null)
                    {
                        s = fresh;
                        o[section] = s;
                    }
                }

                s[key] = on;
                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: " + what + " " + (on ? "ON" : "OFF") + " from the settings tab - " +
                    (section == null ? "it applies from the next new run." : "it applies from the next world."));

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the switch has NOT been saved.");
                return false;
            }
        }

        // The tab's two harder-earlier sliders: one dial's start and perPlus
        // together, and `enabled` follows (0 is off), like the headline sliders.
        public static bool SetDial(string dial, float percent)
        {
            try
            {
                JObject o;

                if (!ReadForEdit(out o))
                    return false;

                JObject h = o["harderEarlier"] as JObject;

                if (h == null)
                {
                    NgHarderRules d = NgHarderRules.Defaults();
                    h = new JObject
                    {
                        ["budget"] = BuffToJ(d.budget, false, false),
                        ["outerKinds"] = BuffToJ(d.outerKinds, false, false)
                    };
                    o["harderEarlier"] = h;
                }

                JObject b = h[dial] as JObject;

                if (b == null)
                {
                    b = BuffToJ(NgHarderRules.Defaults().budget, false, false);
                    h[dial] = b;
                }

                b["start"] = percent;
                b["perPlus"] = percent;
                b["enabled"] = percent > 0f;
                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: harderEarlier." + dial + " set to " + NgMath.Pct(percent) +
                    " a plus from the settings tab - it applies from the next world.");

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the slider has NOT been saved.");
                return false;
            }
        }

        // The file as JSON to edit, or the defaults when there is none. A file
        // that is not JSON is not ours to overwrite quietly: refuse and say so.
        private static bool ReadForEdit(out JObject o)
        {
            string path = FilePath;
            o = null;

            if (!File.Exists(path))
            {
                o = Defaults().ToJObject();
                return true;
            }

            try
            {
                o = JObject.Parse(File.ReadAllText(path));
                return true;
            }
            catch (Exception)
            {
                NewGamePlusPlugin.Log.LogError(
                    "the rules file is not valid JSON, so the settings tab did NOT write it. " +
                    "Fix it (or delete it to get the defaults back) and try again.");
                return false;
            }
        }

        // One buff's object in the file, made from the defaults when missing.
        private static JObject BuffObject(JObject o, string buff)
        {
            JObject b = o[buff] as JObject;

            if (b != null)
                return b;

            NgRules d = Defaults();

            switch (buff)
            {
                case "enemyHealth": b = BuffToJ(d.enemyHealth, true, false); break;
                case "enemyDamage": b = BuffToJ(d.enemyDamage, false, true); break;
                default: b = BuffToJ(d.enemyCount, false, false); break;
            }

            o[buff] = b;
            return b;
        }

        private static bool _saidNoDamage;

        private static void WriteFile(JObject o)
        {
            string path = FilePath;
            string dir = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, o.ToString(Formatting.Indented));
        }
    }
}
