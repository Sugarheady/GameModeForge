using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using MyBox;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // HARDER ENEMIES EARLIER AND HIS ENEMY TABLE - stage 2's world-generation
    // piece (built 2026-10-06, his go: "lets move on to build whatevers left of
    // stage 2"). DECIDED (CONCEPT.md 2.5 and section 7): both dials OFF by
    // default, 25% a plus when on; the table lists every biome and every kind,
    // other biomes' included, each "may appear here from plus N". ★ Since
    // 2026-10-06 (his ask) EVERY enemy in the game is a kind - the debug-only
    // ones and the Queen too - at weight 0 unless its row gives one.
    //
    // ★ HOW A ROOM FILLS (measured, `EnemyGenerator.PlaceBasedOnEcosystem`):
    // its budget is radius x lerp(the ecosystem's CLOSE range, its FAR range,
    // the room's distance from the centre) x the PoI multiplier, spent drawing
    // from the ecosystem's weighted list, skipping a kind that costs more than
    // is left or whose `minDistanceFromCenter` (its "outer rooms only" line)
    // is past this room. A room draws only from its biome's dominant (first)
    // ecosystem.
    //
    // ★ SO THE DATA IS CHANGED, NOT THE GAME'S CODE. For the length of ONE
    // world's `EnemyGenerator.PlaceEnemies` every ecosystem is adjusted, and a
    // Finalizer puts every one back exactly - the list, each kind's line and
    // weight, the close range:
    //   - dial A moves each ecosystem's CLOSE range toward its FAR range by the
    //     dial's share (NgMath.NearEnd): every room moves that share of the way
    //     to the outer rooms' budget, and the rooms' own distance - which the
    //     kinds' lines read - is untouched;
    //   - dial B moves each kind's line toward the centre (NgMath.OuterLine);
    //   - the table: a kind opened in its OWN biome loses its line (it may
    //     appear anywhere there); a kind opened in another biome is ADDED to
    //     that biome's list with its home power level, at its row's weight (or
    //     its home weight), anywhere in the biome.
    // The ecosystems are found through the game's own `LevelElementsCollection
    // .allBioms` - plain data, no scene search. Placement runs synchronously
    // inside the level generator's PlaceEntities step.
    //
    // Enemies a world was made with are in its save, so a Continue needs
    // nothing from here; WORLD INFO shows the world's locked rules.
    internal static class NgWorld
    {
        private sealed class Snap
        {
            public Ecosystem eco;
            public MinMaxFloat close;
            public List<Ecosystem.EnemyDistributionItem> items;
            public List<Ecosystem.Enemy> values;
            public List<float> weights;
        }

        private static List<Snap> _snaps;

        private static readonly AccessTools.FieldRef<EnemyGenerator, int> Placed =
            Ref("placedEnemies");
        private static readonly AccessTools.FieldRef<EnemyGenerator, int> Failed =
            Ref("failedenEmies");

        private static AccessTools.FieldRef<EnemyGenerator, int> Ref(string field)
        {
            try
            {
                return AccessTools.FieldRefAccess<EnemyGenerator, int>(field);
            }
            catch (Exception)
            {
                return null;   // only the report loses its numbers
            }
        }

        // What this world was made with, for the log line.
        private static string _made;

        internal static void Reset()
        {
            _snaps = null;
            _made = null;
            _saidOnce.Clear();
        }

        // EnemyGenerator.PlaceEnemies, prefix.
        internal static void Before()
        {
            _snaps = null;
            _made = null;

            if (!NgRun.Live)
                return;

            NgRules r = NgRun.Rules;
            int plus = NgRun.RulesPlus;
            float a = r.harderEarlier.BudgetAt(plus);
            float b = r.harderEarlier.OuterAt(plus);
            bool table = r.enemyTable.AnyAt(plus);

            if (a <= 0f && b <= 0f && !table)
                return;

            Dictionary<string, Ecosystem> byName;
            List<Ecosystem> all = Ecosystems(out byName);

            if (all.Count == 0)
            {
                NewGamePlusPlugin.Log.LogWarning(
                    "harder enemies earlier: the game's ecosystems could not be found (LevelElementsCollection." +
                    "allBioms) - this world is made as the game makes it.");
                return;
            }

            _snaps = new List<Snap>();

            foreach (Ecosystem e in all)
            {
                var s = new Snap
                {
                    eco = e,
                    close = e.roomDifficultyRangeClose,
                    items = new List<Ecosystem.EnemyDistributionItem>(e.enemies.Items),
                    values = new List<Ecosystem.Enemy>(),
                    weights = new List<float>()
                };

                foreach (Ecosystem.EnemyDistributionItem i in s.items)
                {
                    s.values.Add(i.Value);
                    s.weights.Add(i.Weight);
                }

                _snaps.Add(s);
            }

            var notes = new List<string>();
            int lines = 0;

            try
            {
                // ---- the table: who is opened where, this world ----------
                Dictionary<string, Home> homes = table ? Homes(all) : null;
                var opened = new HashSet<string>();                 // "kind|eco" open in its own biome
                var added = new List<Added>();

                if (table)
                {
                    foreach (NgTableRow row in r.enemyTable.rows)
                    {
                        Home home;

                        if (!homes.TryGetValue(Clean(row.kind), out home))
                        {
                            SayOnce("kind|" + row.kind,
                                    "your enemy table names '" + row.kind + "', which is not an enemy the game " +
                                    "has - that row is skipped.");
                            continue;
                        }

                        foreach (KeyValuePair<string, int> f in row.from)
                        {
                            if (!NgMath.TableOpen(f.Value, plus))
                                continue;

                            Ecosystem eco;

                            if (!byName.TryGetValue(NgMath.BiomeName(f.Key), out eco))
                            {
                                SayOnce("biome|" + f.Key,
                                        "your enemy table names the biome '" + f.Key + "', which the game " +
                                        "does not have - that place is skipped.");
                                continue;
                            }

                            if (home.natives.Contains(eco))
                            {
                                opened.Add(home.name + "|" + eco.name);
                                notes.Add(home.name + " anywhere in " + NgMath.BiomeName(eco.name));
                            }
                            else
                            {
                                // A kind with no home biome (the Queen, the debug-only
                                // ones) has weight 0 unless its row gives one - his
                                // "so they dont spawn naturally".
                                float w = row.weight > 0f ? row.weight : Math.Max(0f, home.weight);
                                int power = row.power > 0 ? row.power
                                    : (home.power > 0 ? home.power : NgMath.DefaultTablePower);

                                if (w <= 0f)
                                {
                                    SayOnce("w0|" + home.name + "|" + eco.name,
                                            "your enemy table opens " + home.name + " in " + NgMath.BiomeName(eco.name) +
                                            " at weight 0, so it is never drawn there - give its row a weight to spawn it.");
                                    continue;
                                }

                                added.Add(new Added { eco = eco, entity = home.entity, power = power, weight = w });
                                notes.Add(home.name + " into " + NgMath.BiomeName(eco.name) + " (weight " +
                                          w.ToString("0.##", CultureInfo.InvariantCulture) + ", cost " + power + ")");

                                if (NgMath.IsQueen(home.name))
                                {
                                    SayOnce("queen|" + eco.name,
                                            "your enemy table brings the Queen (" + home.name + ") into " +
                                            NgMath.BiomeName(eco.name) + " - every Queen killed counts toward the 4, " +
                                            "so one from the table can end a world sooner. (Raising that count is a later talk.)");
                                }
                            }
                        }
                    }
                }

                // ---- dial A, dial B and the natives the table opened ------
                foreach (Snap s in _snaps)
                {
                    Ecosystem e = s.eco;

                    if (a > 0f)
                    {
                        MinMaxFloat c = e.roomDifficultyRangeClose;
                        c.Min = NgMath.NearEnd(s.close.Min, e.roomDifficultyRangeFar.Min, a);
                        c.Max = NgMath.NearEnd(s.close.Max, e.roomDifficultyRangeFar.Max, a);
                        e.roomDifficultyRangeClose = c;
                    }

                    for (int n = 0; n < s.items.Count; n++)
                    {
                        Ecosystem.Enemy v = s.values[n];

                        if (v.minDistanceFromCenter <= 0f)
                            continue;

                        string kind = v.entity != null ? Clean(v.entity.name) : "";
                        float line = opened.Contains(kind + "|" + e.name)
                            ? 0f
                            : (b > 0f ? NgMath.OuterLine(v.minDistanceFromCenter, b) : v.minDistanceFromCenter);

                        if (Math.Abs(line - v.minDistanceFromCenter) > 1e-6f)
                        {
                            v.minDistanceFromCenter = line;
                            s.items[n].Value = v;
                            lines++;
                        }
                    }
                }

                // ---- the table's newcomers ---------------------------------
                foreach (Added add in added)
                {
                    var v = new Ecosystem.Enemy
                    {
                        minDistanceFromCenter = 0f,
                        powerLevel = add.power,
                        entity = add.entity
                    };

                    add.eco.enemies.Add(v, add.weight);
                }
            }
            catch (Exception ex)
            {
                After(null);
                NewGamePlusPlugin.Log.LogError(
                    "harder enemies earlier could not prepare this world (" + ex.GetType().Name + ": " +
                    ex.Message + ") - it is made as the game makes it.");
                return;
            }

            var parts = new List<string>();

            if (a > 0f)
                parts.Add("rooms near the start " + Pct(a) + " of the way to the far rooms' budget");

            if (b > 0f)
                parts.Add("far-only kinds " + Pct(b) + " closer in");

            parts.Add(lines + " far-only line(s) moved");

            if (notes.Count > 0)
                parts.Add("your table: " + string.Join(", ", notes.ToArray()));

            _made = "harder enemies earlier (" + NgMath.WorldWithRules(NgRun.Plus, NgRun.Ahead) + "): " +
                    string.Join("; ", parts.ToArray());
        }

        // EnemyGenerator.PlaceEnemies, finalizer: every ecosystem back exactly
        // as the game had it, whatever happened.
        internal static void After(EnemyGenerator generator)
        {
            List<Snap> snaps = _snaps;
            _snaps = null;

            if (snaps == null)
                return;

            foreach (Snap s in snaps)
            {
                try
                {
                    s.eco.roomDifficultyRangeClose = s.close;

                    List<Ecosystem.EnemyDistributionItem> list = s.eco.enemies.Items;
                    list.Clear();
                    list.AddRange(s.items);

                    for (int n = 0; n < s.items.Count; n++)
                    {
                        s.items[n].Value = s.values[n];
                        s.items[n].Weight = s.weights[n];
                    }
                }
                catch (Exception ex)
                {
                    NewGamePlusPlugin.Log.LogError(
                        "harder enemies earlier could not put the ecosystem " + s.eco.name + " back (" +
                        ex.Message + ") - restart the game before the next world.");
                }
            }

            if (_made != null && generator != null)
            {
                string placed = Placed != null && Failed != null
                    ? " - " + Placed(generator) + " enemies placed, " + Failed(generator) +
                      " room(s) stopped early for want of space"
                    : "";

                NewGamePlusPlugin.Log.LogInfo(_made + placed + ".");
            }
        }

        // ---- the game's ecosystems ------------------------------------------

        private static List<Ecosystem> Ecosystems(out Dictionary<string, Ecosystem> byName)
        {
            var list = new List<Ecosystem>();
            byName = new Dictionary<string, Ecosystem>(StringComparer.OrdinalIgnoreCase);

            LevelElementsCollection elements;

            if (!ServiceLocator.TryGet<LevelElementsCollection>(out elements) || elements == null ||
                elements.allBioms == null)
            {
                return list;
            }

            foreach (Biom biom in elements.allBioms)
            {
                if (biom == null || biom.ecosystems == null)
                    continue;

                foreach (Biom.BiomEcosystem be in biom.ecosystems)
                {
                    Ecosystem e = be != null ? be.ecosystem : null;

                    if (e == null || e.enemies == null || list.Contains(e))
                        continue;

                    list.Add(e);
                    byName[NgMath.BiomeName(e.name)] = e;
                }

                // A biome's own name works too ("Flesh Starter" is the start
                // area, drawing from "Flesh Easy").
                if (biom.ecosystems.Count > 0 && biom.ecosystems[0] != null && biom.ecosystems[0].ecosystem != null &&
                    !byName.ContainsKey(biom.name))
                {
                    byName[biom.name] = biom.ecosystems[0].ecosystem;
                }
            }

            return list;
        }

        private sealed class Added
        {
            public Ecosystem eco;
            public SavableEntity entity;
            public int power;
            public float weight;
        }

        // Every kind the generator places, with its home: the ecosystem where
        // it has the highest weight (ties by name) - its power level and
        // weight are the home's ("a kind brought into another biome keeps its
        // own power level"). THEN every other enemy in the game (his ask,
        // 2026-10-06 - "include all the enemies in the game in that list",
        // the debug-only ones and the Queen too): no home, weight 0.
        private sealed class Home
        {
            public string name;
            public SavableEntity entity;
            public int power;
            public float weight = -1f;
            public string homeName;
            public readonly HashSet<Ecosystem> natives = new HashSet<Ecosystem>();
        }

        private static Dictionary<string, Home> Homes(List<Ecosystem> all)
        {
            var homes = new Dictionary<string, Home>(StringComparer.OrdinalIgnoreCase);

            foreach (Ecosystem e in all)
            {
                foreach (Ecosystem.EnemyDistributionItem i in e.enemies.Items)
                {
                    if (i == null || i.Value.entity == null)
                        continue;

                    string kind = Clean(i.Value.entity.name);
                    Home h;

                    if (!homes.TryGetValue(kind, out h))
                    {
                        h = new Home { name = kind };
                        homes[kind] = h;
                    }

                    h.natives.Add(e);

                    if (i.Weight > h.weight ||
                        (Math.Abs(i.Weight - h.weight) < 1e-6f && string.CompareOrdinal(e.name, h.homeName) < 0))
                    {
                        h.weight = i.Weight;
                        h.power = i.Value.powerLevel;
                        h.entity = i.Value.entity;
                        h.homeName = e.name;
                    }
                }
            }

            AddEveryEnemy(homes);
            return homes;
        }

        // ★ EVERY ENEMY: the game's own list of everything it can save and
        // spawn (`EntityGameObjectManager.savablesCollection` - what a load and
        // the debug menu spawn from), kept to the fighting ones - `Enemy` AND an
        // `AIAgent` (a crate has Enemy and no AIAgent). Measured 66: the 25 the
        // biomes place, and 41 more - the Queen and her Child, the five
        // drones your gadgets use, the debug-only kinds, the Beacon and the
        // Dummy. A kind with no home has weight 0 and costs its row's cost.
        private static void AddEveryEnemy(Dictionary<string, Home> homes)
        {
            SavablesCollection collection;
            EntityGameObjectManager m;

            // The game's own two ways to it: `GameSaver.Load` asks the
            // ServiceLocator, the debug menu's spawner the entity manager.
            if (!ServiceLocator.TryGet<SavablesCollection>(out collection) || collection == null)
                collection = ServiceLocator.TryGet<EntityGameObjectManager>(out m) && m != null
                    ? m.savablesCollection
                    : null;

            if (collection == null || collection.savableObjectInfos == null)
            {
                SayOnce("no-savables", "your enemy table: the game's list of every enemy could not be found, so " +
                                       "only the kinds the biomes place can be used this world.");
                return;
            }

            foreach (SavablesCollection.EntityPrefab p in collection.savableObjectInfos)
            {
                SavableEntity e = p.prefab;

                if (e == null || e.GetComponent<Enemy>() == null || e.GetComponentInChildren<AIAgent>(true) == null)
                    continue;

                string kind = Clean(e.name);

                if (!homes.ContainsKey(kind))
                    homes[kind] = new Home { name = kind, entity = e, power = 0, weight = 0f };
            }
        }

        private static string Clean(string name)
        {
            string n = (name ?? "").Trim();
            return n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 7) : n;
        }

        private static readonly HashSet<string> _saidOnce = new HashSet<string>();

        private static void SayOnce(string key, string text)
        {
            if (_saidOnce.Add(key))
                NewGamePlusPlugin.Log.LogWarning(text);
        }

        private static string Pct(float fraction)
        {
            return Math.Round(fraction * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        }

        // ---- WORLD INFO -------------------------------------------------------

        // The world's locked rules, so a Continue shows them too. Null when
        // none of the three is on.
        internal static string InfoLine(NgRules r, int plus)
        {
            float a = r.harderEarlier.BudgetAt(plus);
            float b = r.harderEarlier.OuterAt(plus);
            var parts = new List<string>();

            if (a > 0f)
                parts.Add("rooms near the start were made " + Pct(a) + " of the way to the far rooms' budget");

            if (b > 0f)
                parts.Add("far-only enemy kinds " + Pct(b) + " closer to the start");

            if (r.enemyTable.enabled)
            {
                var open = new List<string>();
                Dictionary<string, Home> homes = null;
                Dictionary<string, Ecosystem> byName = null;

                foreach (NgTableRow row in r.enemyTable.rows)
                    foreach (KeyValuePair<string, int> f in row.from)
                        if (NgMath.TableOpen(f.Value, plus))
                            open.Add(row.kind + " in " + NgMath.BiomeName(f.Key) +
                                     (NeverDrawn(row, f.Key, ref homes, ref byName) ? " (weight 0 - never drawn)" : ""));

                if (open.Count > 0)
                    parts.Add("your enemy table: " + string.Join(", ", open.ToArray()));
            }

            return parts.Count == 0 ? null : "Harder enemies earlier (when this world was made): " +
                                             string.Join("; ", parts.ToArray());
        }

        // The same test Before makes: new in that biome (not native there),
        // and weight 0 from its row and its home - the Queen and the 40 other
        // kinds no biome places, until their row gives a weight. The lists are
        // read once per WORLD INFO, only when a row has weight 0. Never throws.
        private static bool NeverDrawn(NgTableRow row, string biome, ref Dictionary<string, Home> homes,
                                       ref Dictionary<string, Ecosystem> byName)
        {
            if (row.weight > 0f)
                return false;

            try
            {
                if (homes == null)
                    homes = Homes(Ecosystems(out byName));

                Home home;
                Ecosystem eco;

                if (!homes.TryGetValue(Clean(row.kind), out home))
                    return false;   // a kind the game does not have: Before says that

                if (byName.TryGetValue(NgMath.BiomeName(biome), out eco) && home.natives.Contains(eco))
                    return false;   // its own biome: the entry drops its line, whatever the weight

                return home.weight <= 0f;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(EnemyGenerator), "PlaceEnemies")]
    internal static class NgWorldPatch
    {
        static void Prefix()
        {
            try
            {
                NgWorld.Before();
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("harder enemies earlier failed before placing enemies: " + ex);
            }
        }

        static void Finalizer(EnemyGenerator __instance)
        {
            try
            {
                NgWorld.After(__instance);
            }
            catch (Exception)
            {
                // Never let our clean-up stand in for the game's own error.
            }
        }
    }
}
