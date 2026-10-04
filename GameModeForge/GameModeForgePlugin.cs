using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GameModeForge
{
    [BepInPlugin(
        "com.sugarheady.gamemodeforge",
        "Game Mode Forge",
        "0.1.0")]
    public class GameModeForgePlugin : BaseUnityPlugin
    {
        // Shared log source, so classes with no MonoBehaviour of their own do
        // not each create one. Writes to BepInEx\GameModeForge.log - see
        // ForgeLog. Every mode this mod registers logs through here too.
        public static readonly ManualLogSource Log =
            ForgeLog.Source("GameModeForge");

        private void Awake()
        {
            // ---- THE LOG FILE, before the first line ----------------------
            //
            // LogOutput.log keeps only THIS plugin's registered `Logger` -
            // loaded, BUILD, patches applied - plus a copy of every error.
            // Everything on `Log` goes to the file. First, so the load lines
            // are copied into the file too.
            ConfigEntry<bool> ownLogFile = Config.Bind(
                "Logging",
                "OwnLogFile",
                true,
                "true: Game Mode Forge writes its log to " +
                "BepInEx\\GameModeForge.log (the launch before is kept as " +
                "GameModeForge.prev.log), so LogOutput.log and the console " +
                "stay readable for BepInEx and other mods. Only errors are " +
                "copied to LogOutput.log.\n" +
                "false: everything goes to LogOutput.log and the console, the " +
                "way it did before 2026-09-25.");

            ForgeLog.Start(Logger, ownLogFile.Value);

            Logger.LogInfo("Game Mode Forge loaded" + ForgeLog.Where);

            // Build stamp. A deliberate copy of the other two mods', and the
            // duplicate-by-design rule is why it is a copy: a stamp is not a
            // decoder, and this mod has to answer for itself with the others
            // absent.
            //
            // Module Forge went the whole of R18 without one while Weapon
            // Forge had it, which is worse than neither having it: the log
            // carried ONE authoritative BUILD line, so a reader checking "am
            // I testing the DLL I just built" got a confident answer about
            // the other mod.
            //
            // The timestamp is the DLL's own last-write time, so it changes
            // on every rebuild whether or not anyone remembers to bump a
            // version number.
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

            // ---- modes -------------------------------------------------
            //
            // NOTHING IS REGISTERED YET, and that is the current state of the
            // project rather than an omission: the scope of this mod is still
            // being designed. `RegisterModes` is the one door, so when the
            // first rule is agreed there is exactly one place it goes and no
            // question about where the config binding lives.
            //
            // The order below is load-bearing. Modes must be REGISTERED
            // before their switches are BOUND, because a binding writes into
            // a mode that has to already exist.
            RegisterModes();
            BindSwitches();

            // The sibling mods' on/off state, for the settings tab. Probed
            // here purely so the log records what this launch actually ran
            // with - the tab re-reads the files every time it opens, because
            // a config can be hand-edited between two openings and a switch
            // showing a value the game is not in is worse than no switch.
            //
            // ⚠ At Awake this is a partial answer by design: Module Forge
            // loads BEFORE this mod and Weapon Forge AFTER, so a type probe
            // here can miss whichever has not loaded yet. It falls back to
            // "does its config file exist", which does not care about order.
            ForgeModControl.Refresh();

            var harmony = new Harmony("com.sugarheady.gamemodeforge");
            harmony.PatchAll();

            Logger.LogInfo(
                "Game Mode Forge patches applied - " +
                ForgeModeRegistry.Count + " mode(s) registered");
        }

        // Let go of GameModeForge.log as this game closes. The restart row
        // (ForgeRelaunch) starts the next game BEFORE this one quits, and
        // that one opens the same file.
        private void OnApplicationQuit()
        {
            ForgeLog.Close();
        }

        // Declare every world rule this mod owns.
        //
        // ★ DECLARING A MODE IS NOT SWITCHING IT ON. Registration says the
        //   rule exists; ForgeModeRegistry decides, per run, whether it acts.
        //   That split is the whole reason this mod is shaped the way it is -
        //   read the header of ForgeModeRegistry before adding anything here.
        //
        // Candidates discussed and gated on this mod existing, none decided:
        //
        //   newgameplus   - after the 4 final bosses, reroll the world and
        //                   keep your build. The game has NO win condition
        //                   wired up at all (GameController.GameWon is
        //                   declared, subscribed to twice and never fired),
        //                   so GameWonScreen is built and unreachable.
        //   perstationshop - each station prints its own list at its own
        //                   rising prices. The shop inventory is ONE global
        //                   RunData.GeneralShopItemList, so the mechanics are
        //                   easy and the ownership is the problem.
        //   runghosts     - Noita-style ghosts of past runs. Needs a hostile
        //                   player-ship unit, which is EnemieForge.
        private static void RegisterModes()
        {
        }

        // Bind one config entry per registered mode.
        //
        // Deliberately derived from the registry rather than hand-listed: a
        // hand-written second list is what drifts, and a mode with no switch
        // is a rule nobody can turn on while the log cheerfully reports it
        // registered. `EffectBuilder.KnownEffects()` in Module Forge is the
        // cautionary twin - a derived list is only right if it is derived
        // from the same source the decision is made from, which here is
        // ForgeModeRegistry.All and nothing else.
        private void BindSwitches()
        {
            foreach (ForgeMode mode in ForgeModeRegistry.All)
            {
                ConfigEntry<bool> entry = Config.Bind(
                    "Modes",
                    mode.Id,
                    false,                       // OFF by default, always
                    mode.Description +
                    (mode.Scope == ForgeModeScope.RunEntry
                        ? "  (applies from the next run)"
                        : "  (applies immediately)"));

                ForgeModeRegistry.SetEnabled(mode.Id, entry.Value);

                // The config file is the fallback switch and, until a menu
                // exists, the only one. Edits made while the game is running
                // are picked up by BepInEx, so follow the entry rather than
                // reading it once - otherwise a player who edits the file
                // between runs sees nothing change and reasonably concludes
                // the setting does not work.
                ForgeMode captured = mode;
                entry.SettingChanged += (s, e) =>
                    ForgeModeRegistry.SetEnabled(captured.Id, entry.Value);
            }
        }
    }
}
