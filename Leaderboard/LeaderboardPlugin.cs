using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GameModeForge.Leaderboard
{
    // THE LEADERBOARD - a local board of finished runs (New Game Plus stage 4).
    //
    // Sugarheady: "for now we could even create our own leaderboard locally. I
    // guess this would be a separate mod for gamemodeforge." Researched and
    // answered on 2026-10-12 (GameModeForge/Docs/CONCEPT.md 5, 5.1):
    //
    //   - a run goes on the board when it ENDS: in a death, or with END RUN on
    //     New Game Plus's victory screen - never a Restart, never a New Game
    //     over an old save;
    //   - ranked by the FURTHEST WORLD, then the SCORE (the money held when the
    //     run ended - the game's own score);
    //   - shown from a LEADERBOARD button in the main menu's bottom row, beside
    //     SETTINGS, and nowhere else;
    //   - a "Record runs" Off/On in the GAME MODE FORGE tab, ON by default, off
    //     while testing.
    //
    // ★ THE GAME'S OWN BOARD IS SWITCHED OFF IN THE PLAYTEST (measured): its
    // uploader's object is inactive in Game.unity, its screen is not in the
    // build. This mod never touches it (PlatformFeatures) - it is local only.
    //
    // The house rules for a Game Mode Forge add-on: no reference to any
    // sibling, no [BepInDependency]; its own `[General] Enabled` in its own
    // cfg, above every patch; its in-game lines in GameModeForge.log when
    // Game Mode Forge is installed (LbLog); AffectsSaves false - it registers
    // no content, so a save loads with it off (that run is simply not counted).
    [BepInPlugin(Guid, "Leaderboard", "0.1.0")]
    public class LeaderboardPlugin : BaseUnityPlugin
    {
        // Also Game Mode Forge's name for this mod's config file - keep the
        // two in step (ForgeModControl's list; the harness checks it).
        public const string Guid = "com.sugarheady.gamemodeforge.leaderboard";

        // Every in-game line. NOT used in Awake: LbLog decides where lines go
        // on the first one, and at Awake Game Mode Forge may not have loaded.
        public static readonly ManualLogSource Log = LbLog.Source("GameModeForge.Leaderboard");

        private void Awake()
        {
            LbLog.Attach(Logger);

            Logger.LogInfo("Leaderboard loaded" + LbLog.Where);

            // Build stamp, ABOVE the gate.
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;

                Logger.LogInfo(
                    "BUILD " + System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss") +
                    "  (if this is older than the change you are testing, the DLL did not get copied)");
            }
            catch { }

            // "Record runs", bound above the gate - the tab can show it on a
            // launch that has the Leaderboard switched off.
            LbSettings.Bind(Config);

            // ---- THE GATE ------------------------------------------------
            ConfigEntry<bool> enabled = Config.Bind("General", "Enabled", true, EnabledBlurb());

            if (!enabled.Value)
            {
                Logger.LogInfo(
                    "Leaderboard is switched OFF ([General] Enabled = false) - no run is recorded and the main " +
                    "menu has no LEADERBOARD button this launch.");
                return;
            }

            // Which Forge mods this launch runs: each one's own switch, as it
            // started (LbMods).
            LbMods.ReadLaunch(Paths.ConfigPath);

            new Harmony(Guid).PatchAll();

            // ★ A DEATH IS THE GAME'S OWN EVENT, heard rather than patched. The
            // handler never throws: the game-over screen listens to the same
            // multicast delegate.
            GameController.GameOver = (Action)Delegate.Combine(
                GameController.GameOver, new Action(LbRun.OnGameOverEvent));

            Logger.LogInfo(
                "Leaderboard patches applied - a run that ends in a death or with END RUN goes on the board (" +
                LbStore.FileName + "); the main menu's LEADERBOARD button shows it.");
        }

        private void OnApplicationQuit()
        {
            LbLog.Close();
        }

        // ★ WORD FOR WORD what Game Mode Forge's ForgeModControl.EnabledBlurb
        // writes for this mod. The harness compares them.
        private static string EnabledBlurb()
        {
            return
                "Master switch for this whole mod. Off means " +
                "Leaderboard" + " patches nothing and builds nothing for " +
                "that launch - the game runs as if the DLL were not " +
                "installed. Takes effect on the NEXT launch, because a loaded " +
                "assembly cannot be unloaded. Game Mode Forge's settings tab " +
                "writes this for you; editing it here by hand works just as " +
                "well.";
        }
    }
}
