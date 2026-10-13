using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // THE STATION UNLOCK COST - stage 2's third piece (built 2026-10-05). The
    // rules are NgStationRules (NgRules.cs); this applies them.
    //
    // ★ THE GAME ALREADY HAS THE WHOLE MECHANISM AND SETS IT TO NOTHING
    // (measured 2026-09-28): a locked station's one upgrade, `FuelDispenser`,
    // costs 0 with a rise of 0 on both station prefabs. `Station.TryInstallUpgrade`
    // charges `CalculateUpgradeCost` out of the installer's tank and refuses
    // when it cannot pay, and `OnHoverStateChanged` writes the same number into
    // the unlock prompt's own price text. So the money is a postfix on
    // `CalculateUpgradeCost`: the price shown and the price charged are ours,
    // with no new UI.
    //
    // ★ THE LADDER'S STEP IS OURS, AND SAVED. The game counts purchases in
    // `RunData.purchasedStationUpgradeCounts`, which is NOT in RunData's
    // memento - every Continue would make the next station cheap again. So
    // New Game Plus counts the stations paid for this world itself (`Paid`),
    // keeps it in its own record in the save, and the ladder starts again at
    // every new world (a run entry resets it).
    //
    // ★ THE STATION YOU START BESIDE IS NEVER ON THE LADDER:
    // `StationGenerator.InitializeStations` unlocks it with `Install` directly,
    // never through `TryInstallUpgrade`, which is the only place this counts.
    //
    // ★ INGREDIENT COSTS (his ask the same day, none by default): Fiber, Ex,
    // Chip, Bond... are the game's INGREDIENTS, kept in the Vault
    // (`Vault.AmountOf` / `Vault.Remove`, the shop's own way of paying them).
    // The game's unlock takes only one resource, so the ingredients are
    // checked in a PREFIX on `TryInstallUpgrade` - if any is short, the unlock
    // is refused there, before the game takes the money - and taken in its
    // postfix once the station has unlocked. The prompt's one text shows the
    // money; the whole cost goes in the ship log as you look at a locked
    // station.
    //
    // The safety net (OFF by default, his call): a station you cannot afford
    // opens free when your fuel is critical or a co-op partner is down - a
    // locked station gives no fuel, and unlocking one is how a dead partner
    // comes back (`ShipManager.OnUpgradeInstalled`). Free is free of the
    // ingredients too. A free unlock still counts as a step, so the net cannot
    // be used to keep the price low.
    internal static class NgStations
    {
        // Stations paid for (or opened free by the net) this world.
        internal static int Paid;

        private struct Cost
        {
            internal Ingredient ingredient;
            internal int count;
        }

        private static Station _freeFor;
        private static string _freeWhy;
        private static List<Cost> _pending;           // the ingredients to take once it unlocks
        private static List<string> _short;           // what you were short of, for the refusal
        private static float _saidShortAt = -100f;
        private static Station _saidCostFor;
        private static float _saidCostAt = -100f;
        private static bool _saidFirst;
        private static readonly HashSet<string> _saidUnknown = new HashSet<string>();

        // A log id of our own, so the line never replaces a game line.
        private const int LogId = 73901;

        internal static void Reset()
        {
            Paid = 0;
            ForgetFree();
            _saidShortAt = -100f;
            _saidCostFor = null;
            _saidCostAt = -100f;
            _saidFirst = false;
        }

        internal static void ForgetFree()
        {
            _freeFor = null;
            _freeWhy = null;
            _pending = null;
            _short = null;
        }

        internal static bool Live
        {
            get { return NgRun.Live && NgRun.Rules.stationCost.ActiveAt(NgRun.RulesPlus); }
        }

        internal static int NextPrice
        {
            get { return NgRun.Rules.stationCost.PriceAt(NgRun.RulesPlus, Paid); }
        }

        // ---- the ingredients --------------------------------------------------

        // What the next station takes of each ingredient, through the game's
        // own registry (an id it does not know is said once and left out).
        private static List<Cost> IngredientCosts()
        {
            var list = new List<Cost>();
            NgStationRules r = NgRun.Rules.stationCost;

            if (r.ingredients.Count == 0)
                return list;

            IngredientRegistry reg;
            ServiceLocator.TryGet<IngredientRegistry>(out reg);

            foreach (NgStationIngredient i in r.ingredients)
            {
                int n = i.AmountAt(r.fromPlus, NgRun.RulesPlus, Paid);

                if (n <= 0)
                    continue;

                Ingredient ing = Resolve(reg, i.id);

                if (ing == null)
                {
                    if (_saidUnknown.Add(i.id))
                    {
                        NewGamePlusPlugin.Log.LogWarning(
                            "station unlock cost: '" + i.id + "' is not an ingredient the game has - it is " +
                            "left out of the cost. The ingredients are Bond, Chip, Fiber, Ex, Face, Gland, " +
                            "Powerstar, Generator and Strange Ball.");
                    }

                    continue;
                }

                list.Add(new Cost { ingredient = ing, count = n });
            }

            return list;
        }

        private static Ingredient Resolve(IngredientRegistry reg, string text)
        {
            if (reg == null)
                return null;

            try
            {
                string id = NgMath.IngredientId(text);

                if (id != null)
                {
                    Ingredient hit = reg.Get(id);

                    if (hit != null)
                        return hit;
                }

                foreach (Ingredient g in reg.AllItems)
                    if (g != null && NgMath.SameIngredient(text, g.Id, g.displayName))
                        return g;
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static Vault TheVault()
        {
            Vault v;
            return ServiceLocator.TryGet<Vault>(out v) ? v : null;
        }

        // "400 + 2 FIBER + 1 EX"
        private static string CostText(List<Cost> costs)
        {
            string s = NextPrice.ToString();

            foreach (Cost c in costs)
                s += " + " + c.count + " " + c.ingredient.displayName.ToUpperInvariant();

            return s;
        }

        // For WORLD INFO: the whole price of the next station.
        internal static string NextCostText()
        {
            try
            {
                List<Cost> costs = IngredientCosts();
                string s = NextPrice + " money";

                foreach (Cost c in costs)
                    s += ", " + c.count + " " + c.ingredient.displayName;

                return s;
            }
            catch (Exception)
            {
                return NextPrice + " money";
            }
        }

        // ---- the money: the game's own price -----------------------------------

        // Station.CalculateUpgradeCost, postfix: the price shown in the unlock
        // prompt and the price TryInstallUpgrade charges.
        internal static void Price(Station station, ref int result)
        {
            if (!Live)
                return;

            result = ReferenceEquals(station, _freeFor) ? 0 : NextPrice;
        }

        // ---- the unlock ------------------------------------------------------------

        // Station.TryInstallUpgrade, prefix. False = refused here (an
        // ingredient is short): the game's own unlock - and its money - is
        // skipped, and the postfix says what you were short of.
        internal static bool BeforeUnlock(Station station, StationUpgrade upgrade, Unit installer, ref bool result)
        {
            ForgetFree();

            if (!Live || installer == null || upgrade == null)
                return true;

            List<Cost> costs = IngredientCosts();
            Vault vault = TheVault();
            var missing = new List<string>();

            int money = Mathf.FloorToInt(installer.GetResource(upgrade.resourceUsed));

            if (money < NextPrice)
                missing.Add((NextPrice - money) + " MONEY");

            foreach (Cost c in costs)
            {
                int have = vault != null ? vault.AmountOf(c.ingredient) : 0;

                if (have < c.count)
                    missing.Add((c.count - have) + " " + c.ingredient.displayName.ToUpperInvariant());
            }

            if (missing.Count == 0)
            {
                _pending = costs;           // the game takes the money; we take these once it unlocks
                return true;
            }

            if (NgRun.Rules.stationCost.safetyNet)
            {
                string why = Stranded(installer);

                if (why != null)
                {
                    _freeFor = station;     // free of everything
                    _freeWhy = why;
                    return true;
                }
            }

            _short = missing;

            if (costs.Count == 0)
                return true;                // money only: the game refuses on its own

            result = false;
            return false;
        }

        // Why you are stranded, or null if you are not.
        private static string Stranded(Unit installer)
        {
            Ship ship = installer.GetComponent<Ship>();

            if (ship != null && ship.Fuel < ship.fuelCriticalThreshold)
                return "your fuel is critical";

            ShipManager ships;

            if (ServiceLocator.TryGet<ShipManager>(out ships) && ships != null)
            {
                foreach (Ship s in ships.Ships)
                    if (s != null && s.IsDead)
                        return "your co-op partner is down";
            }

            return null;
        }

        // Station.TryInstallUpgrade, postfix (it runs after a skipping prefix too).
        internal static void AfterUnlock(Station station, StationUpgrade upgrade, Unit installer, bool unlocked)
        {
            bool free = ReferenceEquals(station, _freeFor);
            string why = _freeWhy;
            List<Cost> pending = _pending;
            List<string> shortOf = _short;
            ForgetFree();

            if (!Live)
                return;

            if (unlocked)
            {
                int paidNow = free ? 0 : NextPrice;
                Paid++;

                // The ingredients, now that the station is open (free takes none).
                string taken = "";

                if (!free && pending != null && pending.Count > 0)
                {
                    Vault vault = TheVault();

                    foreach (Cost c in pending)
                    {
                        if (vault != null)
                            vault.Remove(c.ingredient, c.count);

                        taken += ", " + c.count + " " + c.ingredient.displayName;
                    }
                }

                if (free)
                {
                    Say(installer, "STATION OPENED FREE - " + why + ". The next costs " + NextPrice + ".");
                    NewGamePlusPlugin.Log.LogInfo(
                        "station unlock cost: a station opened FREE (the safety net: " + why + "); it counts as " +
                        "station " + Paid + " this world, so the next costs " + NextCostText() + ".");
                }
                else if (!_saidFirst)
                {
                    _saidFirst = true;
                    NewGamePlusPlugin.Log.LogInfo(
                        "station unlock cost: the first station paid for in " + NgMath.WorldName(NgRun.Plus) +
                        " cost " + paidNow + " money" + taken + "; the next costs " + NextCostText() + " (+" +
                        NgRun.Rules.stationCost.RiseAt(NgRun.RulesPlus) + " money each in this world). Said once a world.");
                }

                return;
            }

            // Could not pay: the whole price and what you were short of - not on
            // every press of the key.
            if (Time.unscaledTime - _saidShortAt < 3f || installer == null || upgrade == null)
                return;

            _saidShortAt = Time.unscaledTime;

            List<Cost> costs = IngredientCosts();
            int have = Mathf.FloorToInt(installer.GetResource(upgrade.resourceUsed));
            string line = costs.Count == 0
                ? "THIS STATION COSTS " + NextPrice + " TO UNLOCK - YOU HAVE " + have
                : "THIS STATION COSTS " + CostText(costs) + " TO UNLOCK - YOU NEED " +
                  (shortOf != null && shortOf.Count > 0 ? string.Join(", ", shortOf.ToArray()) : "MORE");

            Say(installer, line);
            NewGamePlusPlugin.Log.LogInfo(
                "station unlock cost: could not unlock a station - it costs " + NextCostText() + ", and you " +
                "have " + have + " money" + (shortOf != null && shortOf.Count > 0
                    ? " (short of " + string.Join(", ", shortOf.ToArray()).ToLowerInvariant() + ")"
                    : "") + "." + (NgRun.Rules.stationCost.safetyNet
                    ? " (The safety net opens one free only when your fuel is critical or a co-op partner is down.)"
                    : ""));
        }

        // Station.OnHoverStateChanged, postfix: looking at a locked station
        // that takes ingredients says its whole price (the prompt's one text
        // shows only the money). Once per station per few seconds.
        internal static void Looked(Station station, bool hovered)
        {
            if (!hovered || !Live || station == null || NgRun.Rules.stationCost.ingredients.Count == 0)
                return;

            if (station.ComponentData == null || station.ComponentData.IsUnlocked)
                return;

            if (ReferenceEquals(station, _saidCostFor) && Time.unscaledTime - _saidCostAt < 6f)
                return;

            List<Cost> costs = IngredientCosts();

            if (costs.Count == 0)
                return;

            _saidCostFor = station;
            _saidCostAt = Time.unscaledTime;

            ShipManager ships;

            if (!ServiceLocator.TryGet<ShipManager>(out ships) || ships == null)
                return;

            foreach (Ship s in ships.Ships)
                Say(s, "UNLOCK: " + CostText(costs), costs[0].ingredient.iconSmall);
        }

        // A line in a ship's log.
        private static void Say(Unit installer, string text)
        {
            Say(installer != null ? installer.GetComponent<Ship>() : null, text, null);
        }

        private static void Say(Ship ship, string text, Sprite icon)
        {
            try
            {
                if (ship != null && ship.LogOutput != null)
                    ship.LogOutput.Log(LogId, text, icon, 4f);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }
    }

    // ---- the patches -------------------------------------------------------------

    [HarmonyPatch(typeof(Station), "CalculateUpgradeCost")]
    internal static class NgStationPricePatch
    {
        static void Postfix(Station __instance, ref int __result)
        {
            try
            {
                NgStations.Price(__instance, ref __result);
            }
            catch (Exception)
            {
                // The game's own price.
            }
        }
    }

    [HarmonyPatch(typeof(Station), "TryInstallUpgrade")]
    internal static class NgStationUnlockPatch
    {
        static bool Prefix(Station __instance, StationUpgrade upgrade, Unit installer, ref bool __result)
        {
            try
            {
                return NgStations.BeforeUnlock(__instance, upgrade, installer, ref __result);
            }
            catch (Exception)
            {
                // The game's own unlock.
                NgStations.ForgetFree();
                return true;
            }
        }

        static void Postfix(Station __instance, StationUpgrade upgrade, Unit installer, bool __result)
        {
            try
            {
                NgStations.AfterUnlock(__instance, upgrade, installer, __result);
            }
            catch (Exception)
            {
            }
        }

        // ★ If the unlock throws, the postfix never runs - and a station marked
        // free would stay free. The Finalizer always forgets it.
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                NgStations.ForgetFree();

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Station), "OnHoverStateChanged")]
    internal static class NgStationLookPatch
    {
        static void Postfix(Station __instance, bool isHovered)
        {
            try
            {
                NgStations.Looked(__instance, isHovered);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }
    }
}
