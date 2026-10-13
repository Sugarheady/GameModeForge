using System;
using System.Collections.Generic;

namespace GameModeForge.NewGamePlus
{
    // ★ THE ONE PIECE OF NEW GAME PLUS OTHER MODS MAY SEE (CONCEPT.md 1, 2.7) -
    // the first time a Game Mode Forge piece has to be visible to the other
    // mods. Weapon Forge finds this class by TYPE NAME
    // ("GameModeForge.NewGamePlus.NgCarry") and subscribes to it; nothing here
    // knows Weapon Forge exists. With New Game Plus absent, Weapon Forge finds
    // nothing and changes nothing.
    //
    // Why a mod needs it at all: a jump IS a new run, and every mod clears its
    // per-run state at a new run - Weapon Forge's rank progress with the rest.
    // So a mod that wants something to survive the jump takes it when the old
    // world is LEAVING and gives it back when the new world has ARRIVED.
    //
    //   Leaving   the old world is still loaded: every ship, grid and vault
    //             module is the one you were flying with. Raised once, just
    //             before the new world's scene starts loading.
    //   Arrived   the new world's ships exist as data with their carried grids
    //             installed, and the vault is restored - but no ship GameObject
    //             exists yet, so a gun has not been built. Exactly the moment a
    //             Continue restores a save's records (GameSaver.Load's postfix).
    //   RunEnding (contract 3) END RUN on the victory screen: raised just
    //             before the save is deleted, with the run still loaded - the
    //             Leaderboard puts the run on its board here.
    //
    // Ship ids change across a jump (an id is a random number from the world's
    // seed), so `NewShipId` maps each old ship to the one it became. Ships are
    // paired by ORDER, never by id (2.2): the game puts player 1 on the ship
    // with the LOWER id, so the first old ship (by id) goes onto the first new
    // ship (by id).
    //
    // Keep these names: they are a contract with another repo, pinned in both.
    public static class NgCarry
    {
        // 2 (2026-10-12, stage 3): RulesPlus, for Enemy Traits.
        // 3 (2026-10-12, stage 4): RunEnding, for the Leaderboard.
        public const int ContractVersion = 3;

        // True from Leaving until Arrived. A mod's run-entry reset that sees
        // this true is running for the new world of a jump.
        public static bool Carrying { get; internal set; }

        // The world's plus: 0 for the first world, 1 after the first jump.
        public static int Plus
        {
            get { return NgRun.Plus; }
        }

        // ★ SWITCHED ON THIS LAUNCH (contract 2, stage 3). A DLL that is switched
        // off in its cfg is still LOADED - this class is still found by type
        // name - so "New Game Plus is installed" and "New Game Plus is running"
        // are two questions, and Enemy Traits asks this one.
        public static bool Enabled
        {
            get { return NewGamePlusPlugin.Patched; }
        }

        // ★ THE PLUS THIS WORLD'S RULES ARE READ AT (contract 2, stage 3) - one
        // step on from Plus with the first-world switch on. Enemy Traits reads
        // its traits at this, so the switch moves them too (CONCEPT.md 3.6).
        // 0 between runs.
        public static int RulesPlus
        {
            get { return NgRun.Live ? NgRun.RulesPlus : 0; }
        }

        public static event Action Leaving;
        public static event Action Arrived;

        // ★ CONTRACT 3 (stage 4): END RUN, raised before the save is deleted.
        // A death is the game's own GameController.GameOver; END RUN is New Game
        // Plus's, so New Game Plus says so (CONCEPT.md 5.1).
        public static event Action RunEnding;

        private static readonly Dictionary<int, int> _map = new Dictionary<int, int>();

        // The new id of a ship that had `oldShipId` before the jump, or 0 when
        // that ship is not known. Valid from Arrived for the rest of that world.
        public static int NewShipId(int oldShipId)
        {
            int id;
            return _map.TryGetValue(oldShipId, out id) ? id : 0;
        }

        internal static void RaiseLeaving()
        {
            _map.Clear();
            Carrying = true;
            Raise(Leaving, "Leaving");
        }

        internal static void RaiseArrived(Dictionary<int, int> map)
        {
            _map.Clear();

            if (map != null)
                foreach (var kv in map)
                    _map[kv.Key] = kv.Value;

            try
            {
                Raise(Arrived, "Arrived");
            }
            finally
            {
                Carrying = false;
            }
        }

        // A jump that could not finish: drop the flag so nobody waits on it.
        internal static void Abandon()
        {
            Carrying = false;
            _map.Clear();
        }

        // END RUN, confirmed: the run is still loaded and its save not yet
        // deleted (NgVictory.EndRunConfirmed).
        internal static void RaiseRunEnding()
        {
            Raise(RunEnding, "RunEnding");
        }

        // ★ ONE SUBSCRIBER THAT THROWS MUST NOT STOP THE OTHERS - or the jump, or
        // END RUN.
        // Each is called on its own and a failure is logged with its name.
        private static void Raise(Action evt, string name)
        {
            if (evt == null)
                return;

            foreach (Delegate d in evt.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception ex)
                {
                    NewGamePlusPlugin.Log.LogError(
                        "a mod listening for New Game Plus's " + name + " threw (" +
                        (d.Method.DeclaringType != null ? d.Method.DeclaringType.FullName : "?") +
                        "." + d.Method.Name + ") - New Game Plus carries on without it. " + ex);
                }
            }
        }
    }
}
