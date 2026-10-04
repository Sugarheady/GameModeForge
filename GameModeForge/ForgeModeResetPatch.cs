using HarmonyLib;

namespace GameModeForge
{
    // The two places a run BEGINS, and the only seam this mod has for "a new
    // run is starting". Copied in shape from Module Forge's BurnResetPatch
    // and Weapon Forge's ForgeBurnResetPatch, which reached the same pair by
    // a different road:
    //
    //   new run  -> RunData.Initialize
    //   continue -> GameSaver.Load
    //
    // ONE LIST, TWO ENTRY POINTS. Both twins held this list twice, byte for
    // byte, one copy per patch - and anything added to a duplicated list is
    // one paste from being half-wired, which then fails only on the entry
    // point nobody tested. Both were deduplicated in 2026-09. Starting with
    // one `ResetAll` is free; discovering the need again is not.
    //
    // PREFIX on both, deliberately. Per-run state has to be cleared BEFORE
    // the run's own objects start registering into it. (The mirror rule, from
    // Weapon Forge's wingman sidecar: anything that READS a saved record has
    // to be a POSTFIX on GameSaver.Load, because this prefix is what clears
    // the state it would be writing into.)
    public static class ForgeModeResetPatch
    {
        private static void ResetAll()
        {
            // Order matters: clear the run's state, then latch the new run's
            // rules from the player's switches.
            ForgeModeRegistry.Reset();
            ForgeModeRegistry.BeginRun();

            // The once-only log gates. Without this the second run of a
            // session reports none of its own warnings - so the run where you
            // go looking for a log line is the run that cannot produce it.
            ForgeDiagnosticGates.Reset();

            // Only a FAILED cross-mod lookup, so it is asked again rather
            // than answered once and forever from whenever it first happened
            // to be asked.
            ForgeInterop.ResetLookups();
        }

        [HarmonyPatch(typeof(RunData), "Initialize")]
        public class OnNewRun
        {
            static void Prefix()
            {
                ResetAll();
            }
        }

        [HarmonyPatch(typeof(Punk.SaveLoad.GameSaver), "Load")]
        public class OnContinue
        {
            static void Prefix()
            {
                ResetAll();
            }
        }
    }
}
