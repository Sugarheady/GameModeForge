using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GameModeForge.SalvageShop
{
    // THE MODULE CARD SHOWS EVERY SALVAGE UPGRADE (DECIDED, round 3: "yes") -
    // a line per stat, e.g. "SALVAGE +20% DAMAGE", on top of what the card
    // already shows by itself:
    //
    //   - a LEVEL as the game's own "+N" badge (HoveredModuleInfo draws it
    //     whenever Level > 1), and every effect line reads the level;
    //   - an equipped gun's live numbers (the card lists the built gun's own
    //     stats), and a vault gun's (SalvageApply.VaultCardGun rebuilds them);
    //   - an added capacity or regen as the game's own effect line.
    //
    // A postfix on the private CollectProperties, so the lines go into the
    // same list the game is about to print, in the same pass.
    internal static class SalvageCard
    {
        private static readonly AccessTools.FieldRef<HoveredModuleInfo, List<DisplayableProperty>> Lines =
            SafeLines();

        private static AccessTools.FieldRef<HoveredModuleInfo, List<DisplayableProperty>> SafeLines()
        {
            try
            {
                return AccessTools.FieldRefAccess<HoveredModuleInfo, List<DisplayableProperty>>("moduleProperties");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private const string Colour = "#E47D1E";

        [HarmonyPatch(typeof(HoveredModuleInfo), "CollectProperties")]
        public class OnCard
        {
            static void Postfix(HoveredModuleInfo __instance, Module module)
            {
                if (!SalvageShopPlugin.Active || Lines == null || module == null)
                    return;

                try
                {
                    SalvageUpgrade u = SalvageRecords.Get(module);

                    if (u == null || !u.Any)
                        return;

                    List<DisplayableProperty> list = Lines(__instance);

                    if (list == null)
                        return;

                    foreach (string line in Describe(module, u, SalvageRules.Current))
                        list.Add(new DisplayableProperty("<color=" + Colour + ">" + line + "</color>"));
                }
                catch (Exception)
                {
                    // A card that cannot say its upgrades still shows the rest.
                }
            }
        }

        // "SALVAGE +20% DAMAGE", one per stat, in the picker's order; the
        // step count first, because it is what prices the next step.
        internal static List<string> Describe(Module m, SalvageUpgrade u, SalvageRules r)
        {
            var lines = new List<string>();

            lines.Add("SALVAGE: " + u.steps + (u.steps == 1 ? " STEP" : " STEPS"));

            var order = new List<string>(SalvageRules.WeaponStats) { "level" };

            foreach (string res in SalvageRules.Resources)
                order.Add("cap." + res);

            foreach (string res in SalvageRules.Resources)
                order.Add("regen." + res);

            foreach (string id in order)
            {
                int n = u.Count(id);

                if (n > 0)
                    lines.Add("SALVAGE " + SalvageCatalog.Gives(id, n, r, m) + " " + SalvageCatalog.StatName(id, m));
            }

            // Anything recorded under a name this build does not list (a
            // record from a newer build) is still said, not dropped.
            foreach (KeyValuePair<string, int> kv in u.counts)
                if (!order.Contains(kv.Key) && kv.Value > 0)
                    lines.Add("SALVAGE " + kv.Key.ToUpperInvariant() + " x" + kv.Value);

            return lines;
        }
    }
}
