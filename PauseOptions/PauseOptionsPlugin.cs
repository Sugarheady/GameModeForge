using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GameModeForge.PauseOptions
{
    // PAUSE OPTIONS - the Options screen from the pause menu, mid-run.
    //
    // The ask, 2026-09-28: "could i have you create a separate .dll that
    // connects to GameModeForge that enables the option menu from pause screen
    // mid run instead of having to save and quit the run to goto the menu?"
    //
    // ★ THE GAME ALREADY HAS THE BUTTON. Game.unity carries a complete
    // `OptionsMenu` (the same screen the main menu uses) and a pause-menu
    // `OptionsButton` whose ButtonBody calls `OptionsScreen.Open` as a scene
    // UnityEvent - and that button ships `m_IsActive: 0`. This DLL switches it
    // on, and fixes the two things the stock wiring gets wrong when it is on
    // (PauseMenuOptions has both).
    //
    // ★ "CONNECTS TO GAME MODE FORGE" MEANS ONE THING: Game Mode Forge's
    // settings tab lists it as one more Off/On row, beside Weapon Forge and
    // Module Forge. Everything else is the house rule for every Forge mod:
    //
    //   - it stands alone. No reference to GameModeForge, no
    //     [BepInDependency]; with Game Mode Forge absent it still works.
    //   - it owns its own `[General] Enabled` switch, in its OWN config file
    //     (com.sugarheady.gamemodeforge.pauseoptions.cfg), and the gate
    //     returns above every patch.
    //   - its in-game lines go to GameModeForge.log when Game Mode Forge is
    //     installed ("the mods that fall under the gamemodeforge logs should
    //     fall under the logs for that mod"), and to PauseOptions.log when it
    //     is not. See PauseLog.
    [BepInPlugin(Guid, "Pause Options", "0.1.0")]
    public class PauseOptionsPlugin : BaseUnityPlugin
    {
        // Also Game Mode Forge's name for this mod's config file - keep the
        // two in step (ForgeModControl's list, and the harness checks it).
        public const string Guid = "com.sugarheady.gamemodeforge.pauseoptions";

        // Every in-game line. NOT used in Awake: PauseLog decides where lines
        // go on the first one, and at Awake Game Mode Forge may not have
        // loaded yet - load order is not guaranteed.
        public static readonly ManualLogSource Log =
            PauseLog.Source("GameModeForge.PauseOptions");

        private void Awake()
        {
            // The registered Logger is what LogOutput.log keeps: loaded,
            // BUILD, and the gate's answer. Three lines, whichever way it goes.
            PauseLog.Attach(Logger);

            Logger.LogInfo("Pause Options loaded" + PauseLog.Where);

            // Build stamp, ABOVE the gate: a switched-off mod must still say
            // which DLL is installed. A deliberate copy of the other mods'.
            try
            {
                string dll = System.Reflection.Assembly
                    .GetExecutingAssembly().Location;

                Logger.LogInfo(
                    "BUILD " +
                    System.IO.File.GetLastWriteTime(dll)
                        .ToString("yyyy-MM-dd HH:mm:ss") +
                    "  (if this is older than the change you are testing, " +
                    "the DLL did not get copied)");
            }
            catch { }

            // ---- THE GATE ------------------------------------------------
            //
            // ★ ABOVE EVERY PATCH. Off means the pause menu is the game's own
            // for this launch. A .dll cannot be unloaded, so the switch - from
            // Game Mode Forge's tab or by hand in this file - takes effect at
            // the next launch.
            ConfigEntry<bool> enabled = Config.Bind(
                "General", "Enabled", true, EnabledBlurb());

            if (!enabled.Value)
            {
                Logger.LogInfo(
                    "Pause Options is switched OFF ([General] Enabled = false) " +
                    "- the pause menu is the game's own this launch.");
                return;
            }

            new Harmony(Guid).PatchAll();

            Logger.LogInfo(
                "Pause Options patches applied - the pause menu gets its " +
                "Options button the first time you pause.");
        }

        // Let go of PauseOptions.log (if this launch opened one). Game Mode
        // Forge's restart row starts the next game BEFORE this one quits.
        private void OnApplicationQuit()
        {
            PauseLog.Close();
        }

        // ★ WORD FOR WORD what Game Mode Forge's ForgeModControl.EnabledBlurb
        // writes for this mod, so whichever of the two writes the file second
        // does not churn the description. The harness compares them.
        private static string EnabledBlurb()
        {
            return
                "Master switch for this whole mod. Off means " +
                "Pause Options" + " patches nothing and builds nothing for " +
                "that launch - the game runs as if the DLL were not " +
                "installed. Takes effect on the NEXT launch, because a loaded " +
                "assembly cannot be unloaded. Game Mode Forge's settings tab " +
                "writes this for you; editing it here by hand works just as " +
                "well.";
        }
    }
}
