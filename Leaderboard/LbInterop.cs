using System;
using System.Reflection;

namespace GameModeForge.Leaderboard
{
    // EVERY OTHER MOD, FOUND BY TYPE NAME - never a reference, never a
    // [BepInDependency] (the house rule). The Leaderboard stands alone: with
    // New Game Plus absent every run is world 1 and the board ranks by score.
    internal static class LbInterop
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
    }
}
