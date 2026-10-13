using System;
using System.Reflection;

namespace GameModeForge.EnemyTraits
{
    // EVERY OTHER MOD, FOUND BY TYPE NAME - never a reference, never a
    // [BepInDependency] (the house rule). Enemy Traits stands alone: with New
    // Game Plus absent the plus is always 0 (CONCEPT.md 3.1), and the traits
    // set to start at plus 0 are the flat "harder enemies" mode.
    internal static class EtInterop
    {
        // ★ QUIETLY. AccessTools.TypeByName logs a warning when a type is
        // absent, and "the other mod is not installed" is a normal state.
        internal static Type FindTypeQuietly(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;

                try
                {
                    t = asm.GetType(fullName, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (t != null)
                    return t;
            }

            return null;
        }

        // ---- New Game Plus's carry contract (NgCarry, version 2) ---------------
        //
        // ★ THE RULES PLUS, NOT THE TITLE PLUS (3.6): New Game Plus's first-world
        // switch plays the whole chain one step on, and the traits move with
        // it. `NgCarry.RulesPlus` was added for this (contract version 2).
        //
        // ★ LATCHED ON SUCCESS ONLY. Plugin load order is not guaranteed, so a
        // miss is retried once per run (Reset) rather than cached for the
        // session.
        private const string CarryType = "GameModeForge.NewGamePlus.NgCarry";

        private static PropertyInfo _rulesPlus;
        private static PropertyInfo _enabled;
        private static bool _looked;
        private static bool _saidOld;

        internal static void Reset()
        {
            if (_rulesPlus == null)
                _looked = false;
        }

        // ★ RUNNING, not merely installed: switched off in its cfg, New Game Plus
        // is still LOADED (its types are found), it just patches nothing - and
        // then every world is plus 0 here too (NgCarry.Enabled, contract 2).
        internal static bool NewGamePlusPresent
        {
            get
            {
                Find();

                if (_rulesPlus == null || _enabled == null)
                    return false;

                try
                {
                    return (bool)_enabled.GetValue(null, null);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // The plus this world's rules are read at; 0 with New Game Plus absent.
        internal static int RulesPlus
        {
            get
            {
                Find();

                if (_rulesPlus == null)
                    return 0;

                try
                {
                    return Math.Max(0, (int)_rulesPlus.GetValue(null, null));
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        private static void Find()
        {
            if (_looked)
                return;

            _looked = true;

            try
            {
                Type t = FindTypeQuietly(CarryType);

                if (t == null)
                    return;

                PropertyInfo p = t.GetProperty("RulesPlus", BindingFlags.Public | BindingFlags.Static);

                PropertyInfo e = t.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Static);

                if (p != null && p.PropertyType == typeof(int) && e != null && e.PropertyType == typeof(bool))
                {
                    _rulesPlus = p;
                    _enabled = e;
                    return;
                }

                // An older New Game Plus (contract 1) has only the TITLE plus.
                // Reading that would put the traits one world behind with the
                // first-world switch on, so it is refused and said.
                if (!_saidOld)
                {
                    _saidOld = true;
                    EnemyTraitsPlugin.Log.LogWarning(
                        "New Game Plus is installed but too old for Enemy Traits (no NgCarry.RulesPlus / Enabled) - the " +
                        "traits read every world as plus 0 until the two DLLs are updated together.");
                }
            }
            catch (Exception)
            {
                _rulesPlus = null;
            }
        }
    }
}
