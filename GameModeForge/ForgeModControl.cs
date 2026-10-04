using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;

namespace GameModeForge
{
    // The model behind the settings tab: which sibling mods are installed,
    // whether each is switched on, and what the player has asked for but not
    // yet applied.
    //
    // ★ THIS MOD NEVER REACHES INTO ANOTHER MOD. Each sibling owns an
    // `[General] Enabled` entry in its OWN BepInEx config and honours it at
    // its own startup with this mod absent. All this class does is read and
    // write those files - so the off switch works whether or not Game Mode
    // Forge is installed, and [[mods-must-stand-alone]] is untouched.
    //
    // ★ AND "OFF" ONLY EVER MEANS "NEXT LAUNCH". A .NET assembly cannot be
    // unloaded in Unity's Mono: one AppDomain, and .NET Framework 4.7.2 has
    // no collectible load contexts. So the honest mechanism is to write the
    // flag and restart the process - which is also a REAL reload of every
    // DLL, with none of the half-states an in-place unload would leave.
    public static class ForgeModControl
    {
        public sealed class Entry
        {
            // The BepInEx plugin GUID, which is also its config file's name.
            public readonly string Guid;

            public readonly string DisplayName;

            // A type that exists only in that mod, used to answer "is the DLL
            // installed". Deliberately a long-lived owner rather than a
            // feature class - a renamed feature would read as "not installed",
            // which is the wrong answer arriving silently.
            public readonly string ProbeType;

            // Is the DLL present at all? A mod that is installed but switched
            // OFF still answers true: its assembly loads either way, and only
            // its Awake returns early. That is what makes the switch
            // reversible from this menu.
            public bool Installed;

            // What the config file says today. Before 2026-09-28 this was
            // also "what this launch used", because a write was always
            // followed by a restart. The mid-run tab writes WITHOUT one, so
            // the two can now differ - see Launched.
            public bool Enabled;

            // What the player has chosen in the menu. Equal to Enabled until
            // they touch something.
            public bool Pending;

            // ★ WHAT THIS LAUNCH IS ACTUALLY RUNNING WITH, read once at Game
            // Mode Forge's own startup and never again. A switch flipped from
            // the pause menu is written straight to the file but cannot take
            // effect until the next launch (an assembly cannot be unloaded),
            // so "the file says" and "the game is doing" are different facts
            // for the rest of the session - and a row that showed only the
            // file would claim a change had happened when it had not.
            public bool Launched;

            internal bool LaunchKnown;

            // False for a mod that adds nothing to a saved run - Pause Options
            // only switches a menu button on - so switching it off never
            // earns the "your saved run may not load" warning. Over-warning is
            // the right default for the content mods (see SaveExists), not for
            // one with no content at all.
            public readonly bool AffectsSaves;

            // A public static class in that mod whose settings rows are drawn
            // under its switch (ForgeTabRows), or null for a mod with only a
            // switch. Found by type name like everything else here.
            public string RowsType;

            public bool Changed
            {
                get { return Installed && Pending != Enabled; }
            }

            // The menu's choice differs from what this launch is running:
            // the row is "dirty" and takes effect at the next launch.
            public bool DiffersFromLaunch
            {
                get { return Installed && Pending != Launched; }
            }

            internal Entry(string guid, string displayName, string probeType,
                           bool affectsSaves)
            {
                Guid = guid;
                DisplayName = displayName;
                ProbeType = probeType;
                AffectsSaves = affectsSaves;
            }

            public string ConfigPath
            {
                get { return Path.Combine(Paths.ConfigPath, Guid + ".cfg"); }
            }
        }

        // ★ GAME MODE FORGE IS DELIBERATELY NOT IN THIS LIST. A menu that can
        // switch off the mod that draws the menu leaves no way back except
        // hand-editing a config file, which is the thing this menu exists to
        // avoid.
        //
        // Pause Options is a separate DLL built from this repo (2026-09-28,
        // the ask: "a separate .dll that connects to GameModeForge"). It is
        // listed here exactly like the two content mods - its own switch in
        // its own config, honoured with this mod absent - so "connects to"
        // means "shows up in this tab", and nothing more.
        //
        // New Game Plus (2026-09-29) is the third DLL from this repo and the
        // first with settings of its own in the tab: its carry switches and
        // two sliders are drawn under its switch from `RowsType`. It adds no
        // content to a run, so it earns no save warning either (a save made in
        // New Game Plus continues at normal strength with it switched off).
        //
        // Salvage Shop (2026-10-02) is the fourth, with rows of its own too
        // (refund %, Bond chance, Ex a scrap, quick salvage). It earns no save
        // warning: its scrap is two STOCK ingredients and its upgrades are a
        // record beside the save, so a save loads with it off - un-upgraded -
        // and with it back on the upgrades return (its concept, 3.8).
        private static readonly Entry[] _mods =
        {
            new Entry("com.sugarheady.weaponforge", "Weapon Forge",
                      "WeaponForge.ForgeWeaponStats", true),
            new Entry("com.sugarheady.moduleforge", "Module Forge",
                      "ModuleForge.ModuleForgeRegistry", true),
            new Entry("com.sugarheady.gamemodeforge.pauseoptions", "Pause Options",
                      "GameModeForge.PauseOptions.PauseOptionsPlugin", false),
            new Entry("com.sugarheady.gamemodeforge.newgameplus", "New Game Plus",
                      "GameModeForge.NewGamePlus.NewGamePlusPlugin", false)
            {
                RowsType = "GameModeForge.NewGamePlus.NgTab"
            },
            new Entry("com.sugarheady.gamemodeforge.salvageshop", "Salvage Shop",
                      "GameModeForge.SalvageShop.SalvageShopPlugin", false)
            {
                RowsType = "GameModeForge.SalvageShop.SalvageTab"
            },
        };

        public static IEnumerable<Entry> All
        {
            get { return _mods; }
        }

        public static IEnumerable<Entry> Present
        {
            get { return _mods.Where(m => m.Installed); }
        }

        public static bool AnyChanged
        {
            get { return _mods.Any(m => m.Changed); }
        }

        // Probe for each mod and read its current switch. Cheap enough to call
        // every time the tab opens, which is what keeps the menu honest if the
        // file was edited by hand in between.
        public static void Refresh()
        {
            foreach (Entry mod in _mods)
            {
                mod.Installed =
                    ForgeInterop.FindTypeQuietly(mod.ProbeType) != null ||
                    File.Exists(mod.ConfigPath);

                mod.Enabled = ReadEnabled(mod);
                mod.Pending = mod.Enabled;

                // The first read of the session is Game Mode Forge's own
                // Awake, which is before any menu can have written the file -
                // so it is what every mod read for itself this launch.
                if (!mod.LaunchKnown)
                {
                    mod.Launched = mod.Enabled;
                    mod.LaunchKnown = true;
                }
            }

            GameModeForgePlugin.Log.LogInfo(
                "mods found: " +
                (Present.Any()
                    ? string.Join(", ", Present.Select(
                        m => m.DisplayName + (m.Enabled ? " (on)" : " (OFF)"))
                        .ToArray())
                    : "none"));
        }

        // Throw away anything the player chose but did not apply. Called when
        // the tab closes without a restart, so reopening it does not show
        // stale pending state as though it had taken effect.
        public static void Revert()
        {
            foreach (Entry mod in _mods)
                mod.Pending = mod.Enabled;
        }

        // Write every pending switch to its own mod's config file.
        //
        // Returns false if ANY write failed, and the caller must not restart
        // on false - restarting after a failed write would look exactly like
        // the switch not working, and the log line would be the only evidence.
        public static bool Commit()
        {
            bool allOk = true;

            foreach (Entry mod in _mods)
            {
                if (!mod.Changed)
                    continue;

                if (WriteEnabled(mod, mod.Pending))
                {
                    // The file says this now. Without it, a write that is
                    // NOT followed by a restart - the mid-run tab - would
                    // stay "changed" and be written again on every toggle.
                    mod.Enabled = mod.Pending;

                    GameModeForgePlugin.Log.LogInfo(
                        mod.DisplayName + " -> " +
                        (mod.Pending ? "ON" : "OFF") +
                        " (takes effect on the next launch)");
                }
                else
                {
                    allOk = false;
                }
            }

            return allOk;
        }

        // ---- the config files themselves --------------------------------
        //
        // Read and written through BepInEx's own ConfigFile rather than by
        // hand-editing INI. The format carries a type line and a default line
        // per entry, and a hand-rolled writer that dropped either would leave
        // a file the owning mod then rewrites - so the setting would appear to
        // revert for no visible reason.
        //
        // `saveOnInit: false` matters: constructing one of these must not
        // rewrite another mod's file just because we looked at it.
        private static bool ReadEnabled(Entry mod)
        {
            if (!File.Exists(mod.ConfigPath))
                return true;            // never launched; it defaults to on

            try
            {
                // ★ FULLY QUALIFIED ON PURPOSE. The GAME declares its own
                // `ConfigFile` in the global namespace - it is the abstract
                // base of `ConfigRegistry<TItem, TId>` - and a global-namespace
                // type beats one imported by a `using`. So a bare `ConfigFile`
                // here silently resolves to the game's, and the error you get
                // is "cannot create an instance of the abstract type", which
                // sends you looking at BepInEx rather than at PUNK.
                BepInEx.Configuration.ConfigFile file =
                    new BepInEx.Configuration.ConfigFile(mod.ConfigPath, false);

                ConfigEntry<bool> entry =
                    file.Bind("General", "Enabled", true, EnabledBlurb(mod));

                return entry.Value;
            }
            catch (Exception ex)
            {
                // Never let a config read break the menu. An unreadable file
                // reads as "on", because that is the state the game is
                // actually in - the mod either loaded or it did not, and
                // guessing OFF would show a switch that contradicts the game.
                GameModeForgePlugin.Log.LogWarning(
                    "could not read " + mod.DisplayName + "'s config (" +
                    ex.GetType().Name + ": " + ex.Message +
                    ") - assuming it is on.");

                return true;
            }
        }

        private static bool WriteEnabled(Entry mod, bool value)
        {
            try
            {
                // Fully qualified for the same reason as in ReadEnabled: the
                // game has its own global `ConfigFile` that shadows BepInEx's.
                BepInEx.Configuration.ConfigFile file =
                    new BepInEx.Configuration.ConfigFile(mod.ConfigPath, false);

                ConfigEntry<bool> entry =
                    file.Bind("General", "Enabled", true, EnabledBlurb(mod));

                entry.Value = value;
                file.Save();

                return true;
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogError(
                    "COULD NOT WRITE " + mod.DisplayName + "'s config at " +
                    mod.ConfigPath + " (" + ex.GetType().Name + ": " +
                    ex.Message + "). The switch has NOT been saved and the " +
                    "game will not be restarted.");

                return false;
            }
        }

        // Kept identical in wording to the blurb each mod binds for itself, so
        // whichever writes the file second does not churn the description.
        private static string EnabledBlurb(Entry mod)
        {
            return
                "Master switch for this whole mod. Off means " +
                mod.DisplayName + " patches nothing and builds nothing for " +
                "that launch - the game runs as if the DLL were not " +
                "installed. Takes effect on the NEXT launch, because a loaded " +
                "assembly cannot be unloaded. Game Mode Forge's settings tab " +
                "writes this for you; editing it here by hand works just as " +
                "well.";
        }

        // ---- the save warning --------------------------------------------
        //
        // ⚠ A SAVE THAT USED FORGE CONTENT WILL NOT LOAD WITH THE MOD OFF.
        // `Vault.RestoreFromMemento` does `registry.Get(id).DeepCopy()` with
        // no null check, and `ConfigRegistry.Get` returns default(T) on a
        // miss - so a Forge module in the vault means a
        // NullReferenceException inside the game's own load path.
        //
        // This deliberately reports only that a save EXISTS, not that it uses
        // Forge content, because the second question cannot be answered
        // without loading the save. Saying "may not load" about a save that
        // turns out to be clean is a mild false alarm; saying nothing about
        // one that is not is a save the player cannot open and no warning
        // anywhere. Knowing exactly would need each mod to leave a marker
        // beside the save when it contributes content - worth doing, not done.
        public static bool SaveExists()
        {
            try
            {
                Punk.SaveLoad.GameSaver saver = new Punk.SaveLoad.GameSaver();
                return saver.SavedGameExists(false) || saver.SavedGameExists(true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // True when the next launch will run a content mod OFF that this one
        // ran ON, and a save exists - the only direction that can cost the
        // player anything. Measured against Launched, not the file: a switch
        // flipped from the pause menu is already in the file, and the save it
        // endangers is the run being played right now.
        public static bool WarnAboutSave()
        {
            return _mods.Any(WarnFor) && SaveExists();
        }

        public static bool WarnFor(Entry m)
        {
            return m.Installed && m.AffectsSaves && m.Launched && !m.Pending;
        }
    }
}
