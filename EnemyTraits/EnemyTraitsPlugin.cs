using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GameModeForge.EnemyTraits
{
    // ENEMY TRAITS - features given to ENEMIES, a few at a time: batch 1 is
    // wind-up, grow, overload and ricochet (New Game Plus stage 3).
    //
    // Sugarheady's design, talked through on 2026-10-09 and re-checked on
    // 2026-10-12 (GameModeForge/Docs/CONCEPT.md 3.1-3.7). The decisions:
    //
    //   - its OWN DLL in this repo, switchable in the GAME MODE FORGE tab;
    //   - PORTED, not borrowed: it works with neither Weapon Forge nor Module
    //     Forge installed (from Module Forge's windup and grow, Weapon Forge's
    //     surge and bounce - the FIXED versions);
    //   - it works WITHOUT New Game Plus as a flat "harder enemies" mode:
    //     there the plus is always 0, so only traits set to start at plus 0
    //     are in force (the builder's FLAT preset);
    //   - all four traits on from NEW GAME PLUS 1 by default, on every enemy,
    //     the Queens included; every number with a per-plus box;
    //   - a trait belongs to the enemy UNIT, never the weapon asset; a shot
    //     trait belongs to the SHOT once fired (a knocked-back bullet keeps it);
    //   - never a HUD or ship-log line for an enemy; no tell on screen but the
    //     opt-in marker and gauge.
    //
    // The house rules for a Game Mode Forge add-on: no reference to any
    // sibling, no [BepInDependency]; its own `[General] Enabled` in its own
    // cfg, above every patch; its in-game lines in GameModeForge.log when
    // Game Mode Forge is installed (EtLog); AffectsSaves false - it registers
    // no content, so a save loads with it off (and the enemies are plain).
    [BepInPlugin(Guid, "Enemy Traits", "0.1.0")]
    public class EnemyTraitsPlugin : BaseUnityPlugin
    {
        // Also Game Mode Forge's name for this mod's config file - keep the
        // two in step (ForgeModControl's list; the harness checks it).
        public const string Guid = "com.sugarheady.gamemodeforge.enemytraits";

        // Every in-game line. NOT used in Awake: EtLog decides where lines go
        // on the first one, and at Awake Game Mode Forge may not have loaded.
        public static readonly ManualLogSource Log = EtLog.Source("GameModeForge.EnemyTraits");

        private static bool _patched;

        private void Awake()
        {
            EtLog.Attach(Logger);

            Logger.LogInfo("Enemy Traits loaded" + EtLog.Where);

            // Build stamp, ABOVE the gate.
            try
            {
                string dll = System.Reflection.Assembly.GetExecutingAssembly().Location;

                Logger.LogInfo(
                    "BUILD " + System.IO.File.GetLastWriteTime(dll).ToString("yyyy-MM-dd HH:mm:ss") +
                    "  (if this is older than the change you are testing, the DLL did not get copied)");
            }
            catch { }

            // Display switches, bound above the gate - the tab can show them on
            // a launch that has Enemy Traits switched off.
            EtSettings.Bind(Config);

            // ---- THE GATE ------------------------------------------------
            ConfigEntry<bool> enabled = Config.Bind("General", "Enabled", true, EnabledBlurb());

            if (!enabled.Value)
            {
                Logger.LogInfo(
                    "Enemy Traits is switched OFF ([General] Enabled = false) - enemies are as the game " +
                    "made them this launch.");
                return;
            }

            new Harmony(Guid).PatchAll();
            _patched = true;

            Logger.LogInfo(
                "Enemy Traits patches applied - wind-up, grow, overload and ricochet, from the rules file " +
                "(" + Guid + ".rules.json, made by Enemy Traits Builder.html).");
        }

        private void Update()
        {
            if (_patched)
                EtRun.Tick();
        }

        private void OnApplicationQuit()
        {
            EtLog.Close();
        }

        // ★ WORD FOR WORD what Game Mode Forge's ForgeModControl.EnabledBlurb
        // writes for this mod. The harness compares them.
        private static string EnabledBlurb()
        {
            return
                "Master switch for this whole mod. Off means " +
                "Enemy Traits" + " patches nothing and builds nothing for " +
                "that launch - the game runs as if the DLL were not " +
                "installed. Takes effect on the NEXT launch, because a loaded " +
                "assembly cannot be unloaded. Game Mode Forge's settings tab " +
                "writes this for you; editing it here by hand works just as " +
                "well.";
        }
    }
}
