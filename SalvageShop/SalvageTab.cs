using System;

namespace GameModeForge.SalvageShop
{
    // THE ROWS SALVAGE SHOP PUTS IN GAME MODE FORGE'S SETTINGS TAB (3.9,
    // DECIDED round 2: "rows in the GAME MODE FORGE tab for the on/off and the
    // refund %"; PROPOSED and taken: "the Bond chance and the Ex per scrap get
    // tab rows too"), plus the QUICK SALVAGE switch the screen's tickbox writes.
    //
    // ★ GAME MODE FORGE DRAWS THEM, SALVAGE SHOP OWNS THEM. Found by TYPE NAME
    // ("GameModeForge.SalvageShop.SalvageTab"); Game Mode Forge knows nothing
    // about what the rows mean. The numbers live in the rules file (the same
    // one Salvage Builder.html writes), the switch in this mod's cfg.
    //
    // THE CONTRACT - the one New Game Plus's NgTab speaks (ForgeTabRows):
    //
    //   string[] Rows()                    "id|toggle|caption|offText|onText"
    //                                      "id|slider|caption|steps|maxShown"
    //   int      Get(string id)            toggle: 0 / 1; slider: a step
    //   bool     Set(string id, int value) false = not saved (the row goes back)
    //   string   Label(string id, int v)   the row's caption for that value
    public static class SalvageTab
    {
        public const int ContractVersion = 1;

        // Refund and Bond chance: 0 - 100% in steps of 5. Ex a scrap: 0 - 20.
        private const int PercentPerStep = 5;
        private const int PercentSteps = 20;
        private const int ExSteps = 20;

        public static string[] Rows()
        {
            return new[]
            {
                "salvage.refund|slider|Scrap refund of upgrades|" + PercentSteps + "|" + (PercentSteps * PercentPerStep),
                "salvage.bond|slider|Bond chance a scrap|" + PercentSteps + "|" + (PercentSteps * PercentPerStep),
                "salvage.ex|slider|Ex a scrap|" + ExSteps + "|" + ExSteps,
                "salvage.quick|toggle|Quick salvage (no confirm)|Off|On",
            };
        }

        public static int Get(string id)
        {
            try
            {
                SalvageRules r = SalvageRules.Current;

                switch (id)
                {
                    case "salvage.refund":
                        return Step(r.refundPercent, PercentPerStep, PercentSteps);
                    case "salvage.bond":
                        return Step(r.bondChance, PercentPerStep, PercentSteps);
                    case "salvage.ex":
                        return Math.Max(0, Math.Min(ExSteps, r.exPerScrap));
                    case "salvage.quick":
                        return SalvageShopPlugin.QuickSalvage != null && SalvageShopPlugin.QuickSalvage.Value ? 1 : 0;
                }
            }
            catch (Exception)
            {
            }

            return 0;
        }

        public static bool Set(string id, int value)
        {
            try
            {
                switch (id)
                {
                    case "salvage.refund":
                        return SalvageRules.SetScrap("refundPercent", Math.Max(0, value) * PercentPerStep);
                    case "salvage.bond":
                        return SalvageRules.SetScrap("bondChance", Math.Max(0, value) * PercentPerStep);
                    case "salvage.ex":
                        return SalvageRules.SetScrap("exPerScrap", Math.Max(0, value));
                    case "salvage.quick":
                        return SalvageShopPlugin.SetQuick(value != 0, "the settings tab");
                }
            }
            catch (Exception ex)
            {
                SalvageShopPlugin.Log.LogError("the settings tab could not save " + id + ": " + ex);
            }

            return false;
        }

        public static string Label(string id, int value)
        {
            switch (id)
            {
                case "salvage.refund":
                    int f = Math.Max(0, value) * PercentPerStep;
                    return f == 0 ? "Scrap refund of upgrades: none" : "Scrap refunds " + f + "% of upgrades";
                case "salvage.bond":
                    int b = Math.Max(0, value) * PercentPerStep;
                    return b == 0 ? "Bond a scrap: never" : "Bond chance a scrap: " + b + "%";
                case "salvage.ex":
                    return "Ex a scrap: " + Math.Max(0, value);
            }

            foreach (string row in Rows())
            {
                string[] bits = row.Split('|');

                if (bits.Length > 2 && bits[0] == id)
                    return bits[2];
            }

            return id;
        }

        private static int Step(float value, int perStep, int steps)
        {
            int s = (int)Math.Round(value / perStep, MidpointRounding.AwayFromZero);
            return s < 0 ? 0 : (s > steps ? steps : s);
        }
    }
}
