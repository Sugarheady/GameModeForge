using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameModeForge.SalvageShop
{
    // EVERY NUMBER THE SALVAGE SHOP DECIDES, in one file with no Unity and no
    // game types in it on purpose: Harness/salvagemathprobe.py compiles this
    // file alone and runs it. Change a rule here and nowhere else.
    //
    // The design is GameModeForge/Docs/SALVAGE SHOP - CONCEPT.md (section 19 is
    // the build in one place). Each rule below names the section it comes from.

    // One cost ladder (3.5). His default: n Ex at step n, and Bond joins AFTER
    // step 5 - 6-9: 1, 10-14: 2, 15: 3 ... A stat may carry its own (3.4).
    internal struct SalvageLadder
    {
        public int exStart;      // Ex at step 1
        public int exPerStep;    // Ex added each step after that
        public int bondAfter;    // Bond is 0 up to and including this step
        public int bondEvery;    // after it, Bond = step / bondEvery (never less than 1) ...
        public int bondAmount;   // ... times this

        public static SalvageLadder Default
        {
            get
            {
                return new SalvageLadder
                {
                    exStart = 1, exPerStep = 1, bondAfter = 5, bondEvery = 5, bondAmount = 1
                };
            }
        }
    }

    internal static class SalvageMath
    {
        // ---- prices (3.5) -------------------------------------------------

        // Ex for the item's `step`-th step (step 1 is its first). Never below 0.
        public static int ExAt(SalvageLadder l, int step)
        {
            if (step < 1)
                return 0;

            long ex = (long)l.exStart + (long)l.exPerStep * (step - 1);
            return (int)Math.Max(0L, Math.Min(int.MaxValue, ex));
        }

        // Bond for the item's `step`-th step: 0 up to the threshold, then
        // step / every rounded down, never less than 1, times the amount.
        // His ladder: 6-9 -> 1, 10-14 -> 2, 15 -> 3, 20 -> 4.
        public static int BondAt(SalvageLadder l, int step)
        {
            if (step < 1 || step <= l.bondAfter || l.bondAmount <= 0)
                return 0;

            int every = Math.Max(1, l.bondEvery);
            long bond = (long)Math.Max(1, step / every) * l.bondAmount;
            return (int)Math.Min(int.MaxValue, bond);
        }

        // ---- scrap (3.3, 3.6) ---------------------------------------------

        // What scrapping hands back of what was spent on the item's upgrades.
        // Rounded DOWN, so a partial refund can never be gamed into a profit.
        public static int Refund(int spent, float percent)
        {
            if (spent <= 0 || percent <= 0f)
                return 0;

            double p = Math.Min(100.0, percent) / 100.0;
            return (int)Math.Floor(spent * p + 1e-6);
        }

        // `roll` is a uniform number in [0, 1). A chance of 50 means half.
        public static bool BondRoll(float chancePercent, double roll)
        {
            if (chancePercent <= 0f)
                return false;

            if (chancePercent >= 100f)
                return true;

            return roll * 100.0 < chancePercent;
        }

        // ---- the stats (3.4, 13-18) ---------------------------------------

        // "Steps of one stat ADD UP, of the gun's own number": three +10%
        // damage steps are +30% of the gun's own hit, not 1.1 cubed.
        public static float AddPercentOf(float current, float own, float percentPerStep, int steps)
        {
            if (steps <= 0)
                return current;

            return current + own * (percentPerStep / 100f) * steps;
        }

        // Spread: -10% of the gun's own spread a step, never below 0.
        public static float CutPercentOf(float current, float own, float percentPerStep, int steps)
        {
            if (steps <= 0)
                return current;

            return Math.Max(0f, current - own * (percentPerStep / 100f) * steps);
        }

        // REDUCE COST (16.4, 18.3): -10% a step, adding up, never below 0, so
        // ten steps make it free to fire. Of the cost as built - the gun's own,
        // and an add-on's.
        public static float CostAfter(float cost, float percentPerStep, int steps)
        {
            if (steps <= 0)
                return cost;

            return Math.Max(0f, cost * (1f - (percentPerStep / 100f) * steps));
        }

        // ★ A WEAPON GADGET'S ACTIVATION COST STOPS AT A FLOOR, NEVER 0 (the
        // gadget cost trap, found in the audit). WeaponBasedActiveModule
        // .Activate checks GetResource(res) < cost, FIRES, and only then does
        // GetTank(res).Value -= cost, with no HasTank. A ship with no tank of
        // that resource passes the check at a cost of 0, and the subtraction
        // throws after the shot and before ModuleActivator stamps the cooldown
        // - so a held button fires every frame. At the floor the game's own
        // check says "not enough" instead, as it does for any gadget whose
        // tank you lack, and with the tank it is free in all but name.
        public const float GadgetCostFloor = 0.001f;

        public static float ActivationCostAfter(float cost, float percentPerStep, int steps)
        {
            if (steps <= 0 || cost <= 0f)
                return cost;

            return Math.Max(GadgetCostFloor, CostAfter(cost, percentPerStep, steps));
        }

        // The spark add-on's cost is an int, so a cut is rounded - halves UP
        // (18.2): a cost of 1 stays 1 until the cut passes 50%.
        public static int IntCostAfter(int cost, float percentPerStep, int steps)
        {
            if (steps <= 0)
                return cost;

            return (int)Math.Round(CostAfter(cost, percentPerStep, steps), MidpointRounding.AwayFromZero);
        }

        // A weapon GADGET's "fire rate" is its cooldown (12.1): +10% rate a
        // step, adding up, so the cooldown is the gadget's own over 1 + 0.1n.
        public static float CooldownAfter(float cooldown, float percentPerStep, int steps)
        {
            if (steps <= 0 || cooldown <= 0f)
                return cooldown;

            return cooldown / (1f + (percentPerStep / 100f) * steps);
        }

        // Does a level series move with level at all? `method` is the game's
        // FloatSeries.IncreaseMethod: 0 = Add (base + change * i), 1 =
        // Multiply (base * change ^ i). A flat series ignores level, so a
        // level step would change nothing (3.4: Burst, ExtraProjectile).
        public static bool Scales(int method, float baseValue, float change)
        {
            if (method == 0)
                return change != 0f;

            return baseValue != 0f && change != 1f;
        }

        // ---- the record (2.4) ---------------------------------------------

        // "damage:2,cap.health:1" <-> counts. Bad pieces are skipped, never
        // thrown: a hand-edited record must not stop a save from loading.
        public static string EncodeCounts(IDictionary<string, int> counts)
        {
            var sb = new StringBuilder();

            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key))
                    continue;

                if (sb.Length > 0)
                    sb.Append(',');

                sb.Append(kv.Key.Replace(',', '_').Replace(':', '_').Replace('\t', '_'))
                  .Append(':')
                  .Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }

            return sb.Length == 0 ? "-" : sb.ToString();
        }

        public static Dictionary<string, int> DecodeCounts(string text)
        {
            var d = new Dictionary<string, int>();

            if (string.IsNullOrEmpty(text) || text == "-")
                return d;

            foreach (string part in text.Split(','))
            {
                int at = part.LastIndexOf(':');

                if (at <= 0)
                    continue;

                string key = part.Substring(0, at).Trim();
                int n;

                if (key.Length == 0 ||
                    !int.TryParse(part.Substring(at + 1).Trim(), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out n) ||
                    n <= 0)
                {
                    continue;
                }

                int had;
                d.TryGetValue(key, out had);
                d[key] = had + n;
            }

            return d;
        }

        // ---- text ---------------------------------------------------------

        public static string Num(float v)
        {
            if (Math.Abs(v - Math.Round(v)) < 0.0001)
                return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // "3 EX", "6 EX + 1 BOND", "FREE".
        public static string Price(int ex, int bond, string exName, string bondName)
        {
            if (ex <= 0 && bond <= 0)
                return "FREE";

            string s = ex > 0 ? ex.ToString(CultureInfo.InvariantCulture) + " " + exName : "";

            if (bond > 0)
                s += (s.Length > 0 ? " + " : "") + bond.ToString(CultureInfo.InvariantCulture) + " " + bondName;

            return s;
        }
    }
}
