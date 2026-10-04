using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GameModeForge.NewGamePlus
{
    // NEW GAME PLUS - after the four Queens, a new world: new map, new
    // enemies, new bosses. You keep everything you own, and the enemies get
    // tougher each time.
    //
    // Sugarheady's idea (2026-09-21), designed over five rounds and a final
    // check (2026-09-28/29) and built from GameModeForge/CONCEPT.md, which
    // holds every decision and the measured facts they stand on. Stage 1 of
    // its build order: the victory screen, the pause buttons, the jump and
    // every carry switch, the plus counter and arrival alert, WORLD INFO, Save
    // and Continue, the Weapon Forge rank carry (in Weapon Forge), and enemy
    // health + enemy count with the coverage ramp.
    //
    // The house rules for a Game Mode Forge add-on, like Pause Options:
    //
    //   - it stands alone. No reference to any sibling, no [BepInDependency];
    //     with every other mod absent it still works.
    //   - its own `[General] Enabled`, in its OWN cfg
    //     (com.sugarheady.gamemodeforge.newgameplus.cfg), and the gate returns
    //     above every patch.
    //   - it is one more row in Game Mode Forge's settings tab (and its carry
    //     switches and two sliders are rows there too - NgTab).
    //   - its in-game lines go to GameModeForge.log when Game Mode Forge is
    //     installed, and to NewGamePlus.log when it is not (NgLog).
    //   - AffectsSaves is false: it registers no content, so a save loads with
    //     it switched off - and continues as a normal-strength run - and with
    //     it back on it reads its record and resumes (2.6).
    [BepInPlugin(Guid, "New Game Plus", "0.1.0")]
    public class NewGamePlusPlugin : BaseUnityPlugin
    {
        // Also Game Mode Forge's name for this mod's config file - keep the
        // two in step (ForgeModControl's list, and the harness checks it).
        public const string Guid = "com.sugarheady.gamemodeforge.newgameplus";

        // Every in-game line. NOT used in Awake: NgLog decides where lines go
        // on the first one, and at Awake Game Mode Forge may not have loaded.
        public static readonly ManualLogSource Log =
            NgLog.Source("GameModeForge.NewGamePlus");

        private static bool _patched;

        private void Awake()
        {
            NgLog.Attach(Logger);

            Logger.LogInfo("New Game Plus loaded" + NgLog.Where);

            // Build stamp, ABOVE the gate: a switched-off mod must still say
            // which DLL is installed.
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;

                Logger.LogInfo(
                    "BUILD " +
                    System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss") +
                    "  (if this is older than the change you are testing, " +
                    "the DLL did not get copied)");
            }
            catch { }

            // The carry switches and the marker. Bound above the gate - it
            // patches nothing - so Game Mode Forge's tab can still show and
            // change them on a launch that has New Game Plus switched off.
            NgSettings.Bind(Config);

            // ---- THE GATE ------------------------------------------------
            //
            // ★ ABOVE EVERY PATCH. Off means the game is its own for this
            // launch: no victory screen, no buttons, no buffs. A .dll cannot be
            // unloaded, so the switch - from Game Mode Forge's tab or by hand
            // in this file - takes effect at the next launch.
            ConfigEntry<bool> enabled = Config.Bind(
                "General", "Enabled", true, EnabledBlurb());

            if (!enabled.Value)
            {
                Logger.LogInfo(
                    "New Game Plus is switched OFF ([General] Enabled = false) " +
                    "- the game is its own this launch; a saved New Game Plus run continues at normal strength.");
                return;
            }

            new Harmony(Guid).PatchAll();
            _patched = true;

            Logger.LogInfo(
                "New Game Plus patches applied - kill all four Queens of a world for its " +
                "victory screen; WORLD INFO is in the pause menu.");
        }

        // The per-frame work: the deferred enemy buffs, the victory delay, the
        // arrival alert and save. Only when patched - off means off.
        private void Update()
        {
            if (_patched)
                NgRun.Tick();
        }

        // Let go of NewGamePlus.log (if this launch opened one). Game Mode
        // Forge's restart row starts the next game BEFORE this one quits.
        private void OnApplicationQuit()
        {
            NgLog.Close();
        }

        // ★ WORD FOR WORD what Game Mode Forge's ForgeModControl.EnabledBlurb
        // writes for this mod, so whichever of the two writes the file second
        // does not churn the description. The harness compares them.
        private static string EnabledBlurb()
        {
            return
                "Master switch for this whole mod. Off means " +
                "New Game Plus" + " patches nothing and builds nothing for " +
                "that launch - the game runs as if the DLL were not " +
                "installed. Takes effect on the NEXT launch, because a loaded " +
                "assembly cannot be unloaded. Game Mode Forge's settings tab " +
                "writes this for you; editing it here by hand works just as " +
                "well.";
        }
    }
}
