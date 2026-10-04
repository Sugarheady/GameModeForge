using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GameModeForge.SalvageShop
{
    // SALVAGE SHOP - at a station, scrap the weapons and gadgets you do not
    // want for Ex and Bond, and spend them upgrading what you keep, one step at
    // a time, with no cap. The upgrade lives on that one item and goes wherever
    // it goes, New Game Plus included.
    //
    // Sugarheady's idea (2026-09-24), designed over seven rounds and two
    // pre-build checks (2026-10-02) and built from
    // GameModeForge/Docs/SALVAGE SHOP - CONCEPT.md - section 19 is the build in
    // one place, section 21 what the build chose.
    //
    // The house rules for a Game Mode Forge add-on, like Pause Options and New
    // Game Plus:
    //
    //   - it stands alone. No reference to any sibling, no [BepInDependency];
    //     with every other mod absent it still works.
    //   - its own `[General] Enabled`, in its OWN cfg
    //     (com.sugarheady.gamemodeforge.salvageshop.cfg), and the gate returns
    //     above every patch.
    //   - it is one more row in Game Mode Forge's settings tab (and its refund,
    //     Bond chance, Ex per scrap and quick salvage are rows there too -
    //     SalvageTab).
    //   - its in-game lines go to GameModeForge.log when Game Mode Forge is
    //     installed, and to SalvageShop.log when it is not (SalvageLog).
    //   - AffectsSaves is false (3.8): the scrap is two stock ingredients and the
    //     upgrades are a record beside the save, so a save loads with it off -
    //     the items are simply un-upgraded - and with it back on they return.
    [BepInPlugin(Guid, "Salvage Shop", "0.1.0")]
    public class SalvageShopPlugin : BaseUnityPlugin
    {
        // Also Game Mode Forge's name for this mod's config file - keep the
        // two in step (ForgeModControl's list, and the harness checks it).
        public const string Guid = "com.sugarheady.gamemodeforge.salvageshop";

        // Every in-game line. NOT used in Awake: SalvageLog decides where lines
        // go on the first one, and at Awake Game Mode Forge may not have loaded.
        public static readonly ManualLogSource Log = SalvageLog.Source("GameModeForge.SalvageShop");

        // True once the patches are in. Every patch asks this first.
        internal static bool Active;

        // The SALVAGE screen's QUICK SALVAGE tickbox (14.1: "an option or
        // tickbox in the menu to disable the confirm salvage for quick
        // salvage"), remembered here. Off - asking - is the default.
        internal static ConfigEntry<bool> QuickSalvage;

        internal static bool Quick
        {
            get { return QuickSalvage != null && QuickSalvage.Value; }
        }

        internal static bool SetQuick(bool on, string from)
        {
            if (QuickSalvage == null)
                return false;

            if (QuickSalvage.Value != on)
            {
                // BepInEx saves the cfg on every set (SaveOnConfigSet).
                QuickSalvage.Value = on;
                Log.LogInfo("quick salvage " + (on ? "ON" : "OFF") + " (from " + from + ") - " +
                            (on ? "a scrap no longer asks, except for an upgraded item or a ranked gun."
                                : "a scrap asks to confirm."));
            }

            return true;
        }

        private void Awake()
        {
            SalvageLog.Attach(Logger);

            Logger.LogInfo("Salvage Shop loaded" + SalvageLog.Where);

            // Build stamp, ABOVE the gate: a switched-off mod must still say
            // which DLL is installed.
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;

                Logger.LogInfo(
                    "BUILD " +
                    System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss") +
                    "  (if this is older than the change you are testing, the DLL did not get copied)");
            }
            catch { }

            // Bound above the gate - it patches nothing - so Game Mode Forge's
            // tab can still show and change it on a launch that has Salvage
            // Shop switched off.
            QuickSalvage = Config.Bind("Salvage", "QuickSalvage", false,
                "Off (the default): dropping a module into the SALVAGE slot asks to confirm before it is " +
                "scrapped. On: it is scrapped at once - except an upgraded item or a ranked gun, which " +
                "always asks. The tickbox on the SALVAGE screen and Game Mode Forge's tab both write this.");

            // ---- THE GATE ------------------------------------------------
            //
            // ★ ABOVE EVERY PATCH. Off means the game is its own for this
            // launch: no SALVAGE tab, no upgrades applied. A .dll cannot be
            // unloaded, so the switch takes effect at the next launch.
            ConfigEntry<bool> enabled = Config.Bind("General", "Enabled", true, EnabledBlurb());

            if (!enabled.Value)
            {
                Logger.LogInfo(
                    "Salvage Shop is switched OFF ([General] Enabled = false) - the game is its own this " +
                    "launch; saved upgrades are kept in the save and come back when it is on again.");
                return;
            }

            try
            {
                new Harmony(Guid).PatchAll();
                Active = true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Salvage Shop could NOT apply its patches, so it does nothing this launch: " + ex);
                return;
            }

            Logger.LogInfo(
                "Salvage Shop patches applied - at a station, the grid tab has a SALVAGE tab beside SHOP " +
                "and VAULT.");
        }

        // Let go of SalvageShop.log (if this launch opened one). Game Mode
        // Forge's restart row starts the next game BEFORE this one quits.
        private void OnApplicationQuit()
        {
            SalvageLog.Close();
        }

        // ★ WORD FOR WORD what Game Mode Forge's ForgeModControl.EnabledBlurb
        // writes for this mod, so whichever of the two writes the file second
        // does not churn the description. The harness compares them.
        private static string EnabledBlurb()
        {
            return
                "Master switch for this whole mod. Off means " +
                "Salvage Shop" + " patches nothing and builds nothing for " +
                "that launch - the game runs as if the DLL were not " +
                "installed. Takes effect on the NEXT launch, because a loaded " +
                "assembly cannot be unloaded. Game Mode Forge's settings tab " +
                "writes this for you; editing it here by hand works just as " +
                "well.";
        }
    }
}
