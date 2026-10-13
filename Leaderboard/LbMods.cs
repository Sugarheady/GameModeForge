using System;
using System.Collections.Generic;
using System.IO;

namespace GameModeForge.Leaderboard
{
    // WHICH FORGE MODS A RUN WAS PLAYED WITH (CONCEPT.md 5: "and which Forge
    // mods were on").
    //
    // ★ RUNNING, NOT INSTALLED (measured 2026-10-12, 5.1). A Forge DLL switched
    // off in its cfg is still LOADED - its types are found - it just patches
    // nothing. Only New Game Plus says publicly whether it is running
    // (NgCarry.Enabled); the others do not. So for each: its type is loaded
    // AND its own `[General] Enabled` said on when THIS mod woke - the same
    // idea as Game Mode Forge's `Entry.Launched`: a switch flipped mid-run is
    // written to the file at once but cannot take effect until the next
    // launch, so the file is read once, at startup, and never again.
    //
    // ★ READ AS TEXT (LbMath.EnabledInCfg), never through a BepInEx ConfigFile:
    // binding an entry on another mod's file can write it, and looking must
    // never write.
    //
    // The same probe types as Game Mode Forge's switch list (a copy - the
    // Leaderboard stands alone). Pause Options is left out: it changes no run.
    internal static class LbMods
    {
        private sealed class Mod
        {
            internal readonly string Tag;
            internal readonly string Guid;
            internal readonly string Probe;
            internal bool OnAtLaunch = true;

            internal Mod(string tag, string guid, string probe)
            {
                Tag = tag;
                Guid = guid;
                Probe = probe;
            }
        }

        private const string NewGamePlusGuid = "com.sugarheady.gamemodeforge.newgameplus";

        private static readonly Mod[] _mods =
        {
            new Mod("WF", "com.sugarheady.weaponforge", "WeaponForge.ForgeWeaponStats"),
            new Mod("MF", "com.sugarheady.moduleforge", "ModuleForge.ModuleForgeRegistry"),
            new Mod("NG+", NewGamePlusGuid, "GameModeForge.NewGamePlus.NewGamePlusPlugin"),
            new Mod("ET", "com.sugarheady.gamemodeforge.enemytraits", "GameModeForge.EnemyTraits.EnemyTraitsPlugin"),
            new Mod("SALVAGE", "com.sugarheady.gamemodeforge.salvageshop", "GameModeForge.SalvageShop.SalvageShopPlugin"),
        };

        // The plugin's Awake: every switch as this launch started.
        internal static void ReadLaunch(string configDir)
        {
            foreach (Mod m in _mods)
            {
                try
                {
                    string path = Path.Combine(configDir, m.Guid + ".cfg");
                    m.OnAtLaunch = !File.Exists(path) || LbMath.EnabledInCfg(File.ReadAllText(path));
                }
                catch (Exception)
                {
                    // Unreadable reads as on: the mod either loaded or it did not,
                    // and the type check below still has to agree.
                    m.OnAtLaunch = true;
                }
            }
        }

        // Asked when a run ends - long after every plugin has loaded, so "the
        // type is not there" means not installed.
        internal static List<string> Running()
        {
            var on = new List<string>();

            foreach (Mod m in _mods)
            {
                bool running;

                try
                {
                    running = m.Guid == NewGamePlusGuid && LbCarry.Found
                        ? LbCarry.Running
                        : LbInterop.FindTypeQuietly(m.Probe) != null && m.OnAtLaunch;
                }
                catch (Exception)
                {
                    running = false;
                }

                if (running)
                    on.Add(m.Tag);
            }

            return on;
        }
    }
}
