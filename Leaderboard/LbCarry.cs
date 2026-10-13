using System;
using System.Reflection;

namespace GameModeForge.Leaderboard
{
    // NEW GAME PLUS'S CARRY CONTRACT (NgCarry), FOUND BY TYPE NAME - the one
    // Weapon Forge's rank, Salvage Shop and Enemy Traits read. Version 3 was
    // added for this mod (2026-10-12, CONCEPT.md 5.1):
    //
    //   Leaving     the old world of a jump, still loaded: the board takes what
    //               THAT world added to the chain's totals (LbRun.OnLeaving).
    //   Arrived     the new world, its run record already carried: what the new
    //               world STARTS from is measured (LbRun.OnArrived).
    //   RunEnding   ★ contract 3: END RUN on the victory screen, raised just
    //               before the save is deleted, the run still loaded. A run that
    //               ends this way goes on the board (5.1, answer 1).
    //   Carrying    true from Leaving until Arrived.
    //   Plus / RulesPlus / Enabled   the world, the plus its rules are read at,
    //               and whether New Game Plus is RUNNING (switched off it is
    //               still loaded).
    //
    // ★ With New Game Plus absent nothing here is found and nothing changes:
    // every run is world 1, and there is no END RUN (only deaths end a run).
    // Looked for again at every run entry until found - plugin load order is
    // not guaranteed - and latched on success.
    internal static class LbCarry
    {
        private const string CarryType = "GameModeForge.NewGamePlus.NgCarry";

        private static bool _subscribed;
        private static PropertyInfo _carrying, _plus, _rulesPlus, _enabled;

        // New Game Plus's contract was found (any version that has Enabled).
        internal static bool Found
        {
            get { return _enabled != null; }
        }

        internal static void OnRunEntry()
        {
            Subscribe();
        }

        private static void Subscribe()
        {
            if (_subscribed)
                return;

            Type t = LbInterop.FindTypeQuietly(CarryType);

            if (t == null)
                return;   // not installed, or not loaded yet - asked again next run

            _subscribed = true;

            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;

            _carrying = Prop(t, "Carrying", typeof(bool));
            _plus = Prop(t, "Plus", typeof(int));
            _rulesPlus = Prop(t, "RulesPlus", typeof(int));
            _enabled = Prop(t, "Enabled", typeof(bool));

            bool leaving = Hook(t.GetEvent("Leaving", S), Safe(LbRun.OnLeaving, "Leaving"));
            bool arrived = Hook(t.GetEvent("Arrived", S), Safe(LbRun.OnArrived, "Arrived"));
            bool ending = Hook(t.GetEvent("RunEnding", S), Safe(LbRun.OnRunEnding, "RunEnding"));

            if (!leaving || !arrived || _carrying == null || _plus == null)
            {
                LeaderboardPlugin.Log.LogWarning(
                    "New Game Plus is installed but its carry contract is not the one this build reads - a New " +
                    "Game Plus run's earlier worlds will NOT be added to its row.");
            }

            if (!ending || _enabled == null)
            {
                LeaderboardPlugin.Log.LogWarning(
                    "New Game Plus is installed but older than its contract 3 (no NgCarry.RunEnding / Enabled) - " +
                    "a run ended with END RUN will NOT go on the board until the two DLLs are updated together. " +
                    "Deaths still do.");
            }

            if (leaving && arrived && ending)
            {
                LeaderboardPlugin.Log.LogInfo(
                    "New Game Plus found - a run's row counts every world it reached, and END RUN puts it on " +
                    "the board.");
            }
        }

        private static PropertyInfo Prop(Type t, string name, Type type)
        {
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            return p != null && p.PropertyType == type ? p : null;
        }

        private static bool Hook(EventInfo e, Action handler)
        {
            if (e == null || e.EventHandlerType != typeof(Action))
                return false;

            try
            {
                e.AddEventHandler(null, handler);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ★ OUR THROW NEVER REACHES NEW GAME PLUS (which would log it as a jump
        // listener failing). Said here, in our own words.
        private static Action Safe(Action a, string what)
        {
            return () =>
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    LeaderboardPlugin.Log.LogError("the board could not follow New Game Plus's " + what + ": " + ex);
                }
            };
        }

        internal static bool Carrying
        {
            get { return Get(_carrying, false); }
        }

        // ★ RUNNING, not merely installed (contract 2).
        internal static bool Running
        {
            get { return Get(_enabled, false); }
        }

        // The world: 0 when New Game Plus is absent or not running.
        internal static int Plus
        {
            get { return Running ? Math.Max(0, Get(_plus, 0)) : 0; }
        }

        // 1 when the first-world switch reads this world's rules a step ahead.
        internal static int Ahead
        {
            get
            {
                if (!Running || _rulesPlus == null)
                    return 0;

                int d = Get(_rulesPlus, 0) - Get(_plus, 0);
                return d > 0 ? 1 : 0;
            }
        }

        private static T Get<T>(PropertyInfo p, T fallback)
        {
            if (p == null)
                return fallback;

            try
            {
                return (T)p.GetValue(null, null);
            }
            catch (Exception)
            {
                return fallback;
            }
        }
    }
}
