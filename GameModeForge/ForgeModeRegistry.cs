using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;

namespace GameModeForge
{
    // ★ THE ONE PIECE OF ARCHITECTURE THIS MOD CANNOT RETROFIT.
    //
    // Weapon Forge and Module Forge are per-FILE mods: a weapon or a module
    // describes itself, and nothing is true of the world unless something you
    // are carrying says so. A game mode is the opposite - a rule about the
    // run - and this project has already paid once for conflating the two.
    //
    // `contactDamage`'s world modes armed EVERY UNIT IN EVERY RUN for a whole
    // session because a file existed in the folder. `Register` ran once per
    // weapon file at startup and latched a static. The fix was to split two
    // questions that had been living in one property:
    //
    //     does this rule EXIST?        -> load time, permanent, never reset
    //     should it ACT right now?     -> per run, and it must be able to
    //                                     answer "no"
    //
    // A mod whose entire purpose is world rules needs that split from the
    // first line, not retrofitted. So it is the first line.
    //
    // The two collections below are that split, and their reset behaviour is
    // opposite. `_modes` is build-time data and clearing it would unregister
    // the mod for the rest of the session - the exact trap documented on
    // `ForgeCrit.Reset()` in Weapon Forge, which deliberately does NOT clear
    // its weapon table. `_live` is per-run state and MUST be cleared, or a
    // rule the player switched off outlives the run that had it on.
    public static class ForgeModeRegistry
    {
        private static readonly ManualLogSource Log =
            ForgeLog.Source("GameModeForge");

        // WHICH RULES EXIST. Filled at load. Never cleared.
        private static readonly Dictionary<string, ForgeMode> _modes =
            new Dictionary<string, ForgeMode>(StringComparer.OrdinalIgnoreCase);

        // WHICH RULES ARE IN FORCE FOR THE RUN THAT IS RUNNING. Rebuilt from
        // `_modes` at every run entry, cleared in between.
        private static readonly HashSet<string> _live =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // False between runs - in the menus, during loading, before the first
        // run of a session. A RunEntry mode is NOT live here, and that is the
        // point: it means "no rule is in force" is a state the mod can be in,
        // rather than the rules simply always being on because they exist.
        private static bool _inRun;

        // A gate that has said its piece once. Named `_warned*` so
        // ForgeDiagnosticGates sweeps it by convention at every run entry -
        // the warning below describes a per-RUN situation and a one-shot bool
        // is per SESSION, which is how the run where you go looking for a
        // warning becomes the run that cannot produce it.
        private static readonly HashSet<string> _warnedUnknown =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<ForgeMode> All
        {
            get { return _modes.Values.OrderBy(m => m.Id); }
        }

        public static int Count
        {
            get { return _modes.Count; }
        }

        public static bool InRun
        {
            get { return _inRun; }
        }

        // Declare a rule. Load time only.
        //
        // Returns the existing mode on a duplicate id rather than throwing or
        // replacing: a crash here would take the whole mod down at Awake, and
        // replacing would mean whichever call ran last silently won.
        public static ForgeMode Register(string id, string displayName,
                                         string description,
                                         ForgeModeScope scope = ForgeModeScope.RunEntry)
        {
            ForgeMode existing;

            if (_modes.TryGetValue(id, out existing))
            {
                Log.LogWarning(
                    "mode '" + id + "' was registered twice - keeping the " +
                    "first one (" + existing.DisplayName + "). Two rules " +
                    "sharing an id would share the player's switch.");

                return existing;
            }

            ForgeMode mode = new ForgeMode(id, displayName, description, scope);
            _modes[mode.Id] = mode;

            return mode;
        }

        public static ForgeMode Get(string id)
        {
            ForgeMode mode;
            return _modes.TryGetValue(id, out mode) ? mode : null;
        }

        // What the player asked for, whether or not it is in force yet.
        public static bool IsEnabled(string id)
        {
            ForgeMode mode = Get(id);
            return mode != null && mode.Enabled;
        }

        // ★ THE QUESTION EVERY RULE ASKS: is this rule in force RIGHT NOW?
        //
        // Unknown ids answer FALSE and say so once. This mod can only ever
        // add rules to a game that works without them, so every uncertainty
        // resolves to "do nothing" - the opposite of the uncertainty rule in
        // Module Forge's powered-module gate, which can only ever take a
        // working module AWAY and therefore resolves every doubt to "live".
        // Which way an unknown resolves is decided by what the code does with
        // the answer, not by a house style.
        public static bool IsLive(string id)
        {
            ForgeMode mode = Get(id);

            if (mode == null)
            {
                if (_warnedUnknown.Add(id ?? "<null>"))
                    Log.LogWarning(
                        "something asked whether mode '" + id + "' is live, " +
                        "and no such mode is registered - answering NO. " +
                        "Registered: " + Names());

                return false;
            }

            if (mode.Scope == ForgeModeScope.Live)
                return mode.Enabled;

            // RunEntry: what was latched when this run began, which is
            // deliberately not the same as what the switch says now.
            return _live.Contains(mode.Id);
        }

        // Set the player's switch. Safe to call at any time; a RunEntry mode
        // says out loud that it will not take effect until the next run,
        // because a toggle that silently does nothing is the complaint this
        // whole file exists to avoid.
        public static void SetEnabled(string id, bool on)
        {
            ForgeMode mode = Get(id);

            if (mode == null)
            {
                Log.LogWarning("cannot set unknown mode '" + id + "'.");
                return;
            }

            if (mode.Enabled == on)
                return;

            mode.Enabled = on;

            bool deferred = _inRun &&
                            mode.Scope == ForgeModeScope.RunEntry &&
                            _live.Contains(mode.Id) != on;

            Log.LogInfo(
                mode.DisplayName + " " + (on ? "ON" : "OFF") +
                (deferred ? "  (takes effect on the next run)" : ""));
        }

        // Called from ForgeModeResetPatch at both run-entry points. Latches
        // the RunEntry modes for the run that is about to start.
        //
        // This is the ONLY place `_inRun` becomes true, so a rule cannot be
        // in force before a run exists to be ruled.
        internal static void BeginRun()
        {
            _live.Clear();

            foreach (ForgeMode mode in _modes.Values)
                if (mode.Enabled)
                    _live.Add(mode.Id);

            _inRun = true;

            if (_live.Count == 0)
            {
                Log.LogInfo("no modes active - the game is stock.");
                return;
            }

            Log.LogInfo(
                "active this run: " +
                string.Join(", ", _live.OrderBy(s => s).ToArray()));
        }

        // Per-RUN state only. `_modes` is deliberately untouched - see the
        // header. Called from the run-entry patch before BeginRun, and there
        // is no other caller.
        internal static void Reset()
        {
            _live.Clear();
            _inRun = false;
        }

        private static string Names()
        {
            return _modes.Count == 0
                ? "(none yet)"
                : string.Join(", ", _modes.Keys.OrderBy(s => s).ToArray());
        }
    }
}
