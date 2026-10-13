using System;

namespace GameModeForge.Leaderboard
{
    // THE ROW THE LEADERBOARD PUTS IN GAME MODE FORGE'S SETTINGS TAB (DECIDED
    // 2026-10-12, CONCEPT.md 5.1 answer 4: a "Record runs" Off/On, ON by
    // default, off while testing).
    //
    // ★ GAME MODE FORGE DRAWS IT, THE LEADERBOARD OWNS IT - New Game Plus's
    // NgTab contract, found by TYPE NAME ("GameModeForge.Leaderboard.LbTab"):
    //
    //   string[] Rows()                    "id|toggle|caption|offText|onText"
    //   int      Get(string id)            toggle 0 / 1
    //   bool     Set(string id, int value) false = not saved (the row goes back)
    //   string   Label(string id, int v)   the caption for that value
    //
    // The cfg, applied at once: the next run to end reads it.
    public static class LbTab
    {
        public const int ContractVersion = 1;

        public static string[] Rows()
        {
            return new[]
            {
                "lb.record|toggle|Record runs|Off|On",
            };
        }

        public static int Get(string id)
        {
            try
            {
                if (id == "lb.record")
                    return LbSettings.Recording ? 1 : 0;
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
                if (id != "lb.record" || LbSettings.RecordRuns == null)
                    return false;

                bool on = value != 0;

                if (LbSettings.RecordRuns.Value != on)
                {
                    // BepInEx saves the cfg on every set.
                    LbSettings.RecordRuns.Value = on;
                    LeaderboardPlugin.Log.LogInfo(
                        "settings tab: Record runs -> " + (on ? "ON" : "OFF - runs that end from now on are not kept"));
                }

                return true;
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the settings tab could not save " + id + ": " + ex);
            }

            return false;
        }

        public static string Label(string id, int value)
        {
            if (id == "lb.record")
                return value == 0 ? "Record runs: OFF (testing)" : "Record runs: on";

            return id;
        }
    }
}
