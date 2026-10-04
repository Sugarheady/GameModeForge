using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GameModeForge
{
    // Restart the game as a genuinely new session.
    //
    // ★ THIS IS THE REAL "RELOAD THE MOD" AND IT IS WHY THE OFF SWITCH WORKS
    // AT ALL. A .NET assembly cannot be unloaded in Unity's Mono - one
    // AppDomain, and .NET Framework 4.7.2 has no collectible load contexts -
    // so there is no in-place way to make a loaded mod stop existing. A new
    // process reloads every DLL, honestly and completely, with none of the
    // half-states an in-place unload would leave behind (content built but
    // patches gone; registries stripped and saves broken).
    //
    // Two routes, preferred in order:
    //
    //   1. The executable directly, from
    //      Process.GetCurrentProcess().MainModule.FileName - the real path of
    //      WHATEVER IS RUNNING, so the new session is the same copy of the
    //      game, with the same BepInEx folder and the same mods.
    //   2. steam://rungameid/<appid>, only if that launch could not even be
    //      started.
    //
    // ★ IT USED TO BE THE OTHER WAY ROUND, AND STEAM LAUNCHED THE WRONG GAME.
    // R19 test 273: "Im almost certain now that it's loading my steam copy of
    // the game. Not the same modded version." Correct - a Steam URL runs the
    // copy STEAM has installed, and he tests on a separate modded copy so his
    // base-game progress stays untouched. So the restart came back with no
    // mods at all, which also explains 272's "starts the game without the
    // mods (including the game mode forge menu)".
    //
    // Launching the exe directly is not bounced back to Steam either: the
    // game's `SteamManager` calls `SteamAPI.RestartAppIfNecessary`, which only
    // restarts through Steam when the process was not started by Steam and
    // there is no steam_appid.txt beside it. A copy that already runs when he
    // starts it himself passes that test, and the new process inherits this
    // one's environment - so it passes it the same way.
    //
    // ★ AND THEN THE RIGHT COPY CAME BACK WITH NO MODS (R20 273), because it
    // inherited this process's environment - including the mod loader's own
    // "already started" marker. See ClearLoaderMarkers.
    //
    // ⚠ The failure mode is benign - the game quits and does not come back,
    // so you start it again yourself - which is why Quit is never called
    // unless a launch was actually started without throwing.
    public static class ForgeRelaunch
    {
        // Set once a relaunch is under way, so a second press cannot start a
        // third process. Not named like a diagnostic gate on purpose: this
        // must NOT be swept at run entry, because there is no run after it.
        private static bool _going;

        public static bool InProgress
        {
            get { return _going; }
        }

        // Returns false if nothing could be launched, in which case the caller
        // must NOT quit - quitting after a failed launch is how you close
        // somebody's game for no reason.
        public static bool Restart()
        {
            if (_going)
                return true;

            if (TryExecutable() || TrySteam())
            {
                _going = true;

                GameModeForgePlugin.Log.LogInfo(
                    "relaunch requested - quitting this session.");

                Application.Quit();
                return true;
            }

            GameModeForgePlugin.Log.LogError(
                "COULD NOT RELAUNCH THE GAME. Your settings HAVE been saved, " +
                "so quit and start the game yourself and they will apply. " +
                "Nothing has been lost.");

            return false;
        }

        // Steam's own launch URL - the FALLBACK. `Application.OpenURL` hands it
        // to the OS. It launches whichever copy Steam has installed, which is
        // exactly why it is no longer tried first.
        private static bool TrySteam()
        {
            try
            {
                uint appId = SteamAppId();

                if (appId == 0)
                    return false;

                GameModeForgePlugin.Log.LogInfo(
                    "relaunching through Steam (app " + appId + ") - the " +
                    "executable could not be started, so this is STEAM'S " +
                    "installed copy, which may not be the one you were " +
                    "playing.");

                Application.OpenURL("steam://rungameid/" + appId);
                return true;
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogWarning(
                    "Steam relaunch route failed too (" + ex.GetType().Name +
                    ").");

                return false;
            }
        }

        // Read the AppID from Steamworks if it initialised, entirely by
        // reflection.
        //
        // ★ BY REFLECTION ON PURPOSE, even though Punk.Main references
        // Steamworks.NET and we could too. Referencing it would put a hard
        // compile-time dependency on a third-party assembly into a mod whose
        // whole design rule is that it must build and run with things absent -
        // and a Steam-less build of the game (or a future one that drops the
        // dependency) would then fail to load this mod rather than quietly
        // using the executable route.
        private static uint SteamAppId()
        {
            Type manager = ForgeInterop.FindTypeQuietly("SteamManager");

            if (manager == null)
                return 0;

            var initialised = manager.GetProperty(
                "Initialized",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

            if (initialised == null || !(bool)initialised.GetValue(null, null))
                return 0;

            Type utils = ForgeInterop.FindTypeQuietly("Steamworks.SteamUtils");

            if (utils == null)
                return 0;

            var getAppId = utils.GetMethod(
                "GetAppID",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static);

            if (getAppId == null)
                return 0;

            object appId = getAppId.Invoke(null, null);

            if (appId == null)
                return 0;

            // Steamworks.NET wraps it in an AppId_t struct with a public
            // `m_AppId` field. Read whatever it is rather than casting.
            var field = appId.GetType().GetField("m_AppId");

            if (field != null)
                return Convert.ToUInt32(field.GetValue(appId));

            return Convert.ToUInt32(appId.ToString());
        }

        // The plain route: start another copy of exactly what is running.
        private static bool TryExecutable()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;

                if (string.IsNullOrEmpty(exe))
                    return false;

                GameModeForgePlugin.Log.LogInfo(
                    "relaunching this same copy of the game: " + exe);

                // UseShellExecute so the new process is not a child sharing
                // our console and standard handles - a child would inherit
                // them and can be killed with the parent on some setups.
                ProcessStartInfo info = new ProcessStartInfo(exe);
                info.UseShellExecute = true;
                info.WorkingDirectory =
                    System.IO.Path.GetDirectoryName(exe) ?? "";

                // ★ THE MOD LOADER'S OWN MARKER HAS TO GO FIRST, or the new
                // game starts with no mods at all.
                Dictionary<string, string> cleared = ClearLoaderMarkers();

                try
                {
                    Process.Start(info);
                }
                catch (Exception)
                {
                    // Nothing was started, so this session goes on - and
                    // BepInEx may still want what it put there.
                    Restore(cleared);
                    throw;
                }

                return true;
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogWarning(
                    "could not launch the executable (" + ex.GetType().Name +
                    ": " + ex.Message + ")");

                return false;
            }
        }

        // ★ R20 TEST 273: "it didn't load any mods this time actually. Just the
        // base game." The exe route DID start the right copy - and that copy
        // skipped BepInEx. Doorstop (BepInEx's loader, the winhttp.dll beside
        // the exe) sets DOORSTOP_INITIALIZED in the process environment when it
        // starts, and skips any process that already has it, so that a game
        // launching its own helpers does not load the mods twice. A process
        // started from this one gets a copy of this environment - ShellExecute
        // included - so the relaunched game looked "already started" and ran
        // stock. The other DOORSTOP_ values (which dll to load, where the game
        // is) are re-made by the new process's own Doorstop, so all of them go.
        //
        // Cleared from THIS process just before the launch, which is about to
        // quit; put back if the launch throws, because then it is not.
        private static Dictionary<string, string> ClearLoaderMarkers()
        {
            var cleared = new Dictionary<string, string>();

            try
            {
                foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
                {
                    string name = e.Key as string;

                    if (name != null &&
                        name.StartsWith("DOORSTOP_", StringComparison.OrdinalIgnoreCase))
                    {
                        cleared[name] = e.Value as string;
                    }
                }

                foreach (string name in cleared.Keys)
                    Environment.SetEnvironmentVariable(name, null);

                GameModeForgePlugin.Log.LogInfo(
                    cleared.Count > 0
                        ? "cleared " + cleared.Count + " mod-loader value(s) from " +
                          "the environment the new game inherits (" +
                          string.Join(", ", new List<string>(cleared.Keys).ToArray()) +
                          ") - with DOORSTOP_INITIALIZED left in, the mod loader " +
                          "skips the new game and it starts with no mods."
                        : "no mod-loader values in the environment to clear.");
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogWarning(
                    "could not clear the mod loader's values from the " +
                    "environment (" + ex.GetType().Name + ") - the new game may " +
                    "start with no mods; if it does, start it yourself.");
            }

            return cleared;
        }

        private static void Restore(Dictionary<string, string> cleared)
        {
            try
            {
                foreach (var pair in cleared)
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
            catch (Exception)
            {
            }
        }
    }
}
