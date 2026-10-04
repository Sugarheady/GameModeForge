using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GameModeForge.SalvageShop
{
    // ★ IS THE GAME STILL LEAVING EX AND BOND ALONE? (3.3, PROPOSED and built)
    //
    // Bond, Ex and Face are registered ingredients that nothing in the game
    // uses - no drop table gives them, no shop item costs or needs them (2.5).
    // That is what makes them free scrap: a save holding them loads with the
    // mod off, the HUD and the price widget show them, and nothing else hands
    // them out. The game is in open playtest, so an update could start using
    // one. Salvage would still work, but that scrap would also come from, and
    // go to, the game - so this looks once a session, the first time the
    // SALVAGE screen is built, and says so in the log.
    internal static class SalvageCheck
    {
        private static bool _done;

        private static readonly FieldInfo UseGroup = AccessTools.Field(typeof(DropTableItem), "useGroup");

        internal static void Once(Ingredient common, Ingredient uncommon)
        {
            if (_done || common == null)
                return;

            _done = true;

            try
            {
                var uses = new List<string>();

                foreach (DropTableWeightedGroup g in UnityEngine.Resources.FindObjectsOfTypeAll<DropTableWeightedGroup>())
                {
                    if (g == null || g.itemDistribution == null || g.itemDistribution.Items == null)
                        continue;

                    foreach (DropTableWeightedGroup.DroppabbleItemDistributionItem it in g.itemDistribution.Items)
                        if (it != null)
                            Note(uses, it.Value, "the drop group " + g.name, common, uncommon);
                }

                foreach (DropTable t in UnityEngine.Resources.FindObjectsOfTypeAll<DropTable>())
                {
                    if (t == null || t.items == null)
                        continue;

                    foreach (DropTableItem it in t.items)
                    {
                        bool grouped = UseGroup != null && (bool)UseGroup.GetValue(it);

                        if (!grouped)
                            Note(uses, it.item, "the drop table " + t.name, common, uncommon);
                    }
                }

                foreach (ShopItemsConfig cfg in UnityEngine.Resources.FindObjectsOfTypeAll<ShopItemsConfig>())
                {
                    if (cfg == null || cfg.AllItems == null)
                        continue;

                    foreach (ShopItemConfig item in cfg.AllItems)
                    {
                        if (item == null)
                            continue;

                        if (item.price != null)
                            foreach (Price p in item.price)
                                if (p.currencyType == Price.CurrencyType.Ingredient && Is(p.ingredient, common, uncommon))
                                    uses.Add("a shop price (" + p.ingredient.displayName + ")");

                        if (item.unlockRequirements != null)
                            foreach (Ingredient need in item.unlockRequirements)
                                if (Is(need, common, uncommon))
                                    uses.Add("a shop unlock (" + need.displayName + ")");
                    }
                }

                if (uses.Count == 0)
                {
                    SalvageShopPlugin.Log.LogInfo(
                        common.displayName + (uncommon != null ? " and " + uncommon.displayName : "") +
                        " are still unused by the game (no drop, no price, no unlock) - salvage is their only source.");
                    return;
                }

                var distinct = new List<string>();

                foreach (string u in uses)
                    if (!distinct.Contains(u))
                        distinct.Add(u);

                SalvageShopPlugin.Log.LogWarning(
                    "THE GAME NOW USES THE SALVAGE SCRAP: " + string.Join("; ", distinct.ToArray()) +
                    ". Salvage still works, but that scrap also comes from and goes to the game now. To keep " +
                    "salvage's own, set scrap.common / scrap.uncommon in the rules file to an ingredient " +
                    "the game does not use.");
            }
            catch (Exception ex)
            {
                SalvageShopPlugin.Log.LogInfo("the check for whether the game uses the salvage scrap could not run (" +
                                              ex.GetType().Name + ") - salvage works either way.");
            }
        }

        private static void Note(List<string> uses, DroppabbleItem item, string where, Ingredient a, Ingredient b)
        {
            if (item.droppableType == DroppabbleType.Ingedient && Is(item.ingredient, a, b))
                uses.Add(where + " (" + item.ingredient.displayName + ")");
        }

        private static bool Is(Ingredient i, Ingredient a, Ingredient b)
        {
            return i != null && (i == a || (b != null && i == b));
        }
    }
}
