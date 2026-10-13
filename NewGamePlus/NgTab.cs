using System;
using System.Globalization;

namespace GameModeForge.NewGamePlus
{
    // THE ROWS NEW GAME PLUS PUTS IN GAME MODE FORGE'S SETTINGS TAB
    // (CONCEPT.md 4, DECIDED: every carry switch in the tab, and the headline
    // numbers - enemy health %, enemy count %, and since 2026-10-02 enemy
    // damage % - as in-game sliders too, writing the same file the builder
    // page does. So does "Enemy damage: hazards too").
    //
    // ★ GAME MODE FORGE DRAWS THEM, NEW GAME PLUS OWNS THEM. Game Mode Forge
    // finds this class by TYPE NAME ("GameModeForge.NewGamePlus.NgTab") and
    // knows nothing about what the rows mean: it asks for the rows, reads a
    // value, writes a value. Every number and every file stays here - each DLL
    // owns its own numbers (section 1). With Game Mode Forge absent nothing
    // calls this, and the cfg / rules file are edited by hand or the builder.
    //
    // THE CONTRACT - keep it in step with ForgeOptionsMenu (pinned both sides):
    //
    //   string[] Rows()                    "id|toggle|caption|offText|onText"
    //                                      "id|slider|caption|steps|maxShown"
    //   int      Get(string id)            toggle: 0 / 1; slider: a step
    //   bool     Set(string id, int value) false = not saved (the row goes back)
    //   string   Label(string id, int v)   the row's caption for that value
    //
    // Every call is wrapped: a throw here must never break the options screen.
    public static class NgTab
    {
        public const int ContractVersion = 1;

        // Enemy health: 0 - 500% a plus in steps of 25; enemy count: 0 -
        // 200% in steps of 10; enemy damage (2026-10-02, his third slider):
        // 0 - 500% in steps of 25, like health. Twenty steps each, the
        // slider's dots.
        private const int HealthPerStep = 25;
        private const int CountPerStep = 10;
        private const int DamagePerStep = 25;
        private const int Steps = 20;

        // The two harder-earlier dials (stage 2): 0 - 100% a plus in steps of 5.
        private const int DialPerStep = 5;
        private const int DialSteps = 20;

        // A dial's step: its per-plus share, 0 when it is off.
        private static int DialStep(NgBuff dial)
        {
            return dial == null || !dial.enabled ? 0 : NgMath.SliderStep(dial.perPlus, DialPerStep, DialSteps);
        }

        public static string[] Rows()
        {
            return new[]
            {
                "ngp.health|slider|Enemy health a plus|" + Steps + "|" + (Steps * HealthPerStep),
                "ngp.count|slider|Enemy count a plus|" + Steps + "|" + (Steps * CountPerStep),
                "ngp.damage|slider|Enemy damage a plus|" + Steps + "|" + (Steps * DamagePerStep),
                "ngp.damage.env|toggle|Enemy damage: hazards too|Off|On",
                "ngp.stations|toggle|Station unlock cost|Off|On",
                // The rest of stage 2 (2026-10-06): money drops is an on/off
                // (DECIDED, answer 6); the two harder-earlier dials are sliders,
                // 0 = off (DECIDED 2026-10-05), 0 - 100% a plus in steps of 5;
                // and his first-world switch.
                "ngp.money|toggle|Money drops|Off|On",
                "ngp.harder.budget|slider|Rooms near the start a plus|" + DialSteps + "|" + (DialSteps * DialPerStep),
                "ngp.harder.kinds|slider|Far-only enemies closer a plus|" + DialSteps + "|" + (DialSteps * DialPerStep),
                "ngp.firstworld|toggle|Buffs from the first world|Off|On",
                "ngp.marker|toggle|Marker on buffed enemies|Off|On",
                "ngp.layers|toggle|Health bars in layers|Off|On",
                // A slider through the named palettes (his ask, 2026-10-05):
                // the handle shows the palette's number, the caption its name.
                "ngp.layers.colours|slider|Layer colours|" + (NgMath.PaletteNames.Length - 1) + "|" +
                    (NgMath.PaletteNames.Length - 1),
                // The xN's size (R21 part 2, his ask): 0 - 400% in steps of 25,
                // 100 the default (a quarter of its first size), 0 hides it.
                "ngp.layers.count|slider|Layer count size|" + NgMath.CountSizeSteps + "|" + NgMath.CountSizeMax,
                "ngp.carry.grid|toggle|Carry: ship grid|Off|On",
                "ngp.carry.vault|toggle|Carry: vault|Off|On",
                "ngp.carry.money|toggle|Carry: money|Off|On",
                "ngp.carry.shop|toggle|Carry: shop stock|Off|On",
                "ngp.carry.prices|toggle|Carry: shop prices|Off|On",
                "ngp.carry.stations|toggle|Carry: stations count|Off|On",
                "ngp.carry.ingredients|toggle|Carry: ingredients owned|Off|On",
                "ngp.carry.totals|toggle|Carry: run time + kills|Off|On",
                "ngp.keep|toggle|New world: health, fuel, ammo|Refill|Keep",
            };
        }

        public static int Get(string id)
        {
            try
            {
                switch (id)
                {
                    case "ngp.health":
                        return NgMath.SliderStep(NgRules.LoadFile().enemyHealth.EffectivePerPlus(), HealthPerStep, Steps);
                    case "ngp.count":
                        return NgMath.SliderStep(NgRules.LoadFile().enemyCount.EffectivePerPlus(), CountPerStep, Steps);
                    case "ngp.damage":
                        return NgMath.SliderStep(NgRules.LoadFile().enemyDamage.EffectivePerPlus(), DamagePerStep, Steps);
                    case "ngp.damage.env":
                        // In the rules file, not the cfg: it is one of the
                        // world's rules, locked at the jump like the numbers.
                        return NgRules.LoadFile().enemyDamage.environment ? 1 : 0;
                    case "ngp.stations":
                        // The same: a rule of the world (stage 2, 2026-10-05).
                        return NgRules.LoadFile().stationCost.enabled ? 1 : 0;
                    case "ngp.money":
                        return NgRules.LoadFile().moneyDrops.enabled ? 1 : 0;
                    case "ngp.harder.budget":
                        return DialStep(NgRules.LoadFile().harderEarlier.budget);
                    case "ngp.harder.kinds":
                        return DialStep(NgRules.LoadFile().harderEarlier.outerKinds);
                    case "ngp.firstworld":
                        return NgRules.LoadFile().firstWorldBuffs ? 1 : 0;
                    case "ngp.layers.colours":
                        return NgSettings.HealthBarPalette != null ? (int)NgSettings.HealthBarPalette.Value : 0;
                    case "ngp.layers.count":
                        return NgMath.SliderStep(
                            NgSettings.HealthBarCountSize != null ? NgSettings.HealthBarCountSize.Value : NgMath.CountSizeDefault,
                            NgMath.CountSizePerStep, NgMath.CountSizeSteps);
                }

                BepInEx.Configuration.ConfigEntry<bool> e = Entry(id);
                return e != null && e.Value ? 1 : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static bool Set(string id, int value)
        {
            try
            {
                switch (id)
                {
                    case "ngp.health":
                        return NgRules.SetHeadline("enemyHealth", NgMath.SliderPercent(value, HealthPerStep));
                    case "ngp.count":
                        return NgRules.SetHeadline("enemyCount", NgMath.SliderPercent(value, CountPerStep));
                    case "ngp.damage":
                        return NgRules.SetHeadline("enemyDamage", NgMath.SliderPercent(value, DamagePerStep));
                    case "ngp.damage.env":
                        return NgRules.SetEnvironment(value != 0);
                    case "ngp.stations":
                        return NgRules.SetStationCost(value != 0);
                    case "ngp.money":
                        return NgRules.SetMoney(value != 0);
                    case "ngp.harder.budget":
                        return NgRules.SetDial("budget", NgMath.SliderPercent(value, DialPerStep));
                    case "ngp.harder.kinds":
                        return NgRules.SetDial("outerKinds", NgMath.SliderPercent(value, DialPerStep));
                    case "ngp.firstworld":
                        return NgRules.SetFirstWorld(value != 0);
                    case "ngp.layers.colours":
                        return SetPalette(value);
                    case "ngp.layers.count":
                        return SetCountSize(value);
                }

                BepInEx.Configuration.ConfigEntry<bool> e = Entry(id);

                if (e == null)
                    return false;

                bool on = value != 0;

                if (e.Value != on)
                {
                    // BepInEx saves the cfg on every set (SaveOnConfigSet).
                    e.Value = on;
                    NewGamePlusPlugin.Log.LogInfo(
                        "settings tab: " + e.Definition.Section + " / " + e.Definition.Key + " -> " +
                        (on ? "ON" : "OFF") +
                        (id == "ngp.marker" || id == "ngp.layers" ? "" : " (applies at the next jump)"));
                }

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("the settings tab could not save " + id + ": " + ex);
                return false;
            }
        }

        public static string Label(string id, int value)
        {
            switch (id)
            {
                case "ngp.health":
                    int h = NgMath.SliderPercent(value, HealthPerStep);
                    return h == 0 ? "Enemy health a plus: off" : "Enemy health " + NgMath.Pct(h) + " a plus";
                case "ngp.count":
                    int c = NgMath.SliderPercent(value, CountPerStep);
                    return c == 0 ? "Enemy count a plus: off" : "Enemy count " + NgMath.Pct(c) + " a plus";
                case "ngp.damage":
                    int d = NgMath.SliderPercent(value, DamagePerStep);
                    return d == 0 ? "Enemy damage a plus: off" : "Enemy damage " + NgMath.Pct(d) + " a plus";
                case "ngp.layers.colours":
                    return "Layer colours: " + NgMath.PaletteNames[Math.Max(0, Math.Min(value, NgMath.PaletteNames.Length - 1))];
                case "ngp.layers.count":
                    return NgMath.CountSizeText(NgMath.SliderPercent(value, NgMath.CountSizePerStep));
                case "ngp.harder.budget":
                    int a = NgMath.SliderPercent(value, DialPerStep);
                    return a == 0 ? "Rooms near the start: off" : "Rooms near the start " + a + "% of the way a plus";
                case "ngp.harder.kinds":
                    int k = NgMath.SliderPercent(value, DialPerStep);
                    return k == 0 ? "Far-only enemies: off" : "Far-only enemies " + k + "% closer a plus";
            }

            foreach (string row in Rows())
            {
                string[] bits = row.Split('|');

                if (bits.Length > 2 && bits[0] == id)
                    return bits[2];
            }

            return id;
        }

        private static bool SetPalette(int value)
        {
            if (NgSettings.HealthBarPalette == null || value < 0 || value >= NgMath.PaletteNames.Length)
                return false;

            var chosen = (NgLayerPalette)value;

            if (NgSettings.HealthBarPalette.Value != chosen)
            {
                // BepInEx saves the cfg on every set; the bars repaint at once.
                NgSettings.HealthBarPalette.Value = chosen;
                NewGamePlusPlugin.Log.LogInfo(
                    "settings tab: Display / HealthBarPalette -> " + chosen +
                    (chosen == NgLayerPalette.Custom ? " (your list: HealthBarLayerColours in the cfg)" : ""));
            }

            return true;
        }

        private static bool SetCountSize(int step)
        {
            if (NgSettings.HealthBarCountSize == null || step < 0 || step > NgMath.CountSizeSteps)
                return false;

            int percent = NgMath.SliderPercent(step, NgMath.CountSizePerStep);

            if (NgSettings.HealthBarCountSize.Value != percent)
            {
                // BepInEx saves the cfg on every set; every xN resizes at once.
                NgSettings.HealthBarCountSize.Value = percent;
                NewGamePlusPlugin.Log.LogInfo(
                    "settings tab: Display / HealthBarCountSize -> " + percent +
                    (percent == 0 ? " (the xN beside a layered bar is hidden)" : "%"));
            }

            return true;
        }

        private static BepInEx.Configuration.ConfigEntry<bool> Entry(string id)
        {
            switch (id)
            {
                case "ngp.marker": return NgSettings.EnemyMarker;
                case "ngp.layers": return NgSettings.HealthBarLayers;
                case "ngp.carry.grid": return NgSettings.CarryGrid;
                case "ngp.carry.vault": return NgSettings.CarryVault;
                case "ngp.carry.money": return NgSettings.CarryMoney;
                case "ngp.carry.shop": return NgSettings.CarryShop;
                case "ngp.carry.prices": return NgSettings.CarryPrices;
                case "ngp.carry.stations": return NgSettings.CarryStations;
                case "ngp.carry.ingredients": return NgSettings.CarryIngredients;
                case "ngp.carry.totals": return NgSettings.CarryTotals;
                case "ngp.keep": return NgSettings.KeepResources;
            }

            return null;
        }
    }

    internal static class NgBuffExtensions
    {
        // What the slider shows for a buff: its per-plus step, or 0 when the
        // buff is off. (A file with start != perPlus shows perPlus; moving the
        // slider sets both.)
        internal static float EffectivePerPlus(this NgBuff b)
        {
            return b != null && b.enabled ? b.perPlus : 0f;
        }
    }
}
