using BepInEx.Configuration;

namespace GameModeForge.Leaderboard
{
    // THE LEADERBOARD'S ONE SETTING - its own BepInEx cfg,
    // `BepInEx\config\com.sugarheady.gamemodeforge.leaderboard.cfg`.
    //
    // ★ BOUND BEFORE THE GATE (New Game Plus's rule): with the Leaderboard
    // switched off it still exists, so Game Mode Forge's tab can show it.
    internal static class LbSettings
    {
        public static ConfigEntry<bool> RecordRuns;

        // Fully qualified: the game declares its own global `ConfigFile`.
        public static void Bind(BepInEx.Configuration.ConfigFile cfg)
        {
            // DECIDED 2026-10-12 (CONCEPT.md 5.1, answer 4): "Tab switch to
            // pause recording" - ON by default, off while testing. Read when a
            // run ENDS, so switching it off mid-run leaves that run off.
            RecordRuns = cfg.Bind("Leaderboard", "RecordRuns", true,
                "On: every run that ends in a death, or with END RUN on New Game Plus's victory screen, " +
                "goes on the board (the main menu's LEADERBOARD button). Off: nothing is added - for test " +
                "runs (the debug menu's teleports). Read when a run ends; the runs already on the board stay. " +
                "Game Mode Forge's settings tab writes this for you.");
        }

        internal static bool Recording
        {
            get { return RecordRuns == null || RecordRuns.Value; }
        }
    }
}
