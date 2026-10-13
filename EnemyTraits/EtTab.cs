using System;

namespace GameModeForge.EnemyTraits
{
    // THE ROWS ENEMY TRAITS PUTS IN GAME MODE FORGE'S SETTINGS TAB (DECIDED
    // 3.6: "Enemy Traits' own on/off row and one on/off per trait; the numbers
    // live in the builder page").
    //
    // ★ GAME MODE FORGE DRAWS THEM, ENEMY TRAITS OWNS THEM - New Game Plus's
    // NgTab contract, found by TYPE NAME ("GameModeForge.EnemyTraits.EtTab"):
    //
    //   string[] Rows()                    "id|toggle|caption|offText|onText"
    //                                      "id|slider|caption|steps|maxShown"
    //   int      Get(string id)            toggle 0 / 1; slider a step
    //   bool     Set(string id, int value) false = not saved (the row goes back)
    //   string   Label(string id, int v)   the caption for that value
    //
    // The traits, the volleys and the limit are rules of a WORLD: they are
    // written to the rules file and apply from the next world. The marker and
    // the gauge are display settings in the cfg and apply at once.
    public static class EtTab
    {
        public const int ContractVersion = 1;

        public static string[] Rows()
        {
            return new[]
            {
                "et.windup|toggle|Trait: wind-up|Off|On",
                "et.grow|toggle|Trait: grow|Off|On",
                "et.overload|toggle|Trait: overload|Off|On",
                "et.ricochet|toggle|Trait: ricochet|Off|On",
                // Off / count / speed (DECIDED: speed is the default).
                "et.volleys|slider|Volleys|2|2",
                "et.limit|toggle|Traits an enemy: limit|Off|On",
                "et.marker|toggle|Marker on enemies with traits|Off|On",
                "et.gauge|toggle|Overload gauge on enemies|Off|On",
            };
        }

        private static int TraitOf(string id)
        {
            switch (id)
            {
                case "et.windup": return EtMath.Windup;
                case "et.grow": return EtMath.Grow;
                case "et.overload": return EtMath.Overload;
                case "et.ricochet": return EtMath.Ricochet;
            }

            return -1;
        }

        public static int Get(string id)
        {
            try
            {
                int t = TraitOf(id);

                if (t >= 0)
                    return EtRules.LoadFile().Trait(t).enabled ? 1 : 0;

                switch (id)
                {
                    case "et.volleys": return EtRules.LoadFile().VolleyMode;
                    case "et.limit": return EtRules.LoadFile().limit.enabled ? 1 : 0;
                    case "et.marker": return EtSettings.EnemyMarker != null && EtSettings.EnemyMarker.Value ? 1 : 0;
                    case "et.gauge": return EtSettings.OverloadGauge != null && EtSettings.OverloadGauge.Value ? 1 : 0;
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
                int t = TraitOf(id);

                if (t >= 0)
                    return EtRules.SetTrait(t, value != 0);

                switch (id)
                {
                    case "et.volleys": return EtRules.SetVolleys(value);
                    case "et.limit": return EtRules.SetLimit(value != 0);
                    case "et.marker": return SetCfg(EtSettings.EnemyMarker, value != 0);
                    case "et.gauge": return SetCfg(EtSettings.OverloadGauge, value != 0);
                }
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("the settings tab could not save " + id + ": " + ex);
            }

            return false;
        }

        private static bool SetCfg(BepInEx.Configuration.ConfigEntry<bool> e, bool on)
        {
            if (e == null)
                return false;

            if (e.Value != on)
            {
                // BepInEx saves the cfg on every set.
                e.Value = on;
                EnemyTraitsPlugin.Log.LogInfo(
                    "settings tab: " + e.Definition.Section + " / " + e.Definition.Key + " -> " + (on ? "ON" : "OFF"));
            }

            return true;
        }

        public static string Label(string id, int value)
        {
            try
            {
                int t = TraitOf(id);

                if (t >= 0)
                {
                    EtTraitRules r = EtRules.LoadFile().Trait(t);
                    string name = char.ToUpperInvariant(EtMath.Names[t][0]) + EtMath.Names[t].Substring(1);

                    if (value == 0)
                        return "Trait: " + name + " - off";

                    // ★ SAID, NOT SILENT (3.7): without New Game Plus every
                    // world is plus 0, so a trait from NG+1 never comes.
                    if (r.fromPlus > 0 && !EtInterop.NewGamePlusPresent)
                        return "Trait: " + name + " - needs New Game Plus (from NG+" + r.fromPlus + ")";

                    return "Trait: " + name + (r.fromPlus > 0 ? " from NG+" + r.fromPlus : " from world 1");
                }

                switch (id)
                {
                    case "et.volleys":
                        switch (value)
                        {
                            case EtMath.VolleysOff: return "Volleys: do not count";
                            case EtMath.VolleysCount: return "Volleys: count, game's pace";
                            default: return "Volleys: count and speed up";
                        }
                    case "et.limit":
                        return value == 0 ? "Traits an enemy: no limit" : "Traits an enemy: limited";
                }
            }
            catch (Exception)
            {
            }

            foreach (string row in Rows())
            {
                string[] bits = row.Split('|');

                if (bits.Length > 2 && bits[0] == id)
                    return bits[2];
            }

            return id;
        }
    }
}
