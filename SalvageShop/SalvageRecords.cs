using System.Collections.Generic;

namespace GameModeForge.SalvageShop
{
    // What one item has had done to it: its own step count (the ladder runs PER
    // ITEM, 3.5), what it cost (the refund, 3.6), and how many steps went into
    // each stat.
    internal sealed class SalvageUpgrade
    {
        public int steps;
        public int spentEx;
        public int spentBond;
        public readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public int Count(string id)
        {
            int n;
            return id != null && counts.TryGetValue(id, out n) ? n : 0;
        }

        public bool Any
        {
            get { return steps > 0 || counts.Count > 0; }
        }

        public void Add(string id, int ex, int bond)
        {
            steps++;
            spentEx += ex;
            spentBond += bond;
            counts[id] = Count(id) + 1;
        }

        // Any of the stats that live on the BUILT gun (not the module).
        public bool AnyWeaponStat
        {
            get
            {
                foreach (string id in SalvageRules.WeaponStats)
                    if (Count(id) > 0)
                        return true;

                return false;
            }
        }
    }

    // ★ ONE UPGRADE PER MODULE INSTANCE. The same Module object travels the
    // whole way - grid to vault to grid (2.2) - and the game never duplicates a
    // live module (Weapon Forge measured it: every DeepCopy starts from an
    // asset), so the instance is a safe identity for as long as it exists.
    // Across a save, the record keys each one by where it is (SalvageSave).
    internal static class SalvageRecords
    {
        private static readonly Dictionary<Module, SalvageUpgrade> _all =
            new Dictionary<Module, SalvageUpgrade>();

        public static SalvageUpgrade Get(Module m)
        {
            SalvageUpgrade u;
            return m != null && _all.TryGetValue(m, out u) ? u : null;
        }

        public static SalvageUpgrade GetOrAdd(Module m)
        {
            SalvageUpgrade u = Get(m);

            if (u == null && m != null)
            {
                u = new SalvageUpgrade();
                _all[m] = u;
            }

            return u;
        }

        public static void Set(Module m, SalvageUpgrade u)
        {
            if (m == null)
                return;

            if (u == null || !u.Any)
                _all.Remove(m);
            else
                _all[m] = u;
        }

        public static bool Remove(Module m)
        {
            return m != null && _all.Remove(m);
        }

        public static IEnumerable<KeyValuePair<Module, SalvageUpgrade>> All
        {
            get { return _all; }
        }

        public static int Count
        {
            get { return _all.Count; }
        }

        // ★ BOTH RUN ENTRIES (RunData.Initialize, GameSaver.Load - prefixes).
        // A new run's modules are new instances, so the last run's upgrades
        // belong to nothing. A Continue gets them back in the Load POSTFIX,
        // and a New Game Plus jump in NgCarry.Arrived.
        public static void Reset()
        {
            _all.Clear();
            SalvageApply.Reset();
        }
    }
}
