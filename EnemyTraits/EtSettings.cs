using BepInEx.Configuration;

namespace GameModeForge.EnemyTraits
{
    // THE DISPLAY SWITCHES - Enemy Traits' own BepInEx cfg,
    // `BepInEx\config\com.sugarheady.gamemodeforge.enemytraits.cfg`. The
    // traits themselves are rules of a world and live in the rules file; these
    // two only change what is drawn, and are read live.
    //
    // ★ BOUND BEFORE THE GATE (New Game Plus's rule): with Enemy Traits
    // switched off these still exist, so Game Mode Forge's tab can show them.
    internal static class EtSettings
    {
        public static ConfigEntry<bool> EnemyMarker;
        public static ConfigEntry<bool> OverloadGauge;

        // Fully qualified: the game declares its own global `ConfigFile`.
        public static void Bind(BepInEx.Configuration.ConfigFile cfg)
        {
            // DECIDED 3.6: ONE MARKER - the same bubble New Game Plus draws
            // (whichever DLL draws it first, the other sees it and skips).
            EnemyMarker = cfg.Bind("Display", "EnemyMarker", false,
                "The small amber ring with a plus in it (New Game Plus's marker) beside every enemy that " +
                "got a trait not every enemy gets - a trait whose coverage is under 100%, or one the " +
                "traits-an-enemy limit left out of others. Off by default. Never a colour tint: the " +
                "game's own hit flash owns an enemy's colour.");

            // DECIDED 3.2: "none by default, with a setting to show a very
            // small one on the enemy".
            OverloadGauge = cfg.Bind("Display", "OverloadGauge", false,
                "A very small bar above every enemy with the overload trait: blue as its meter fills, " +
                "gold while it is overloaded. Off by default - nothing else on screen says an enemy " +
                "is wound up or overloading (his call). Applies at once.");
        }
    }
}
