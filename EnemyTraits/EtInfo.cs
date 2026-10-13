using System;
using System.Collections.Generic;

namespace GameModeForge.EnemyTraits
{
    // ★ WHAT NEW GAME PLUS MAY ASK (DECIDED 3.6: "WORLD INFO lists each trait
    // with its numbers and share of enemies"). New Game Plus finds this class
    // by TYPE NAME ("GameModeForge.EnemyTraits.EtInfo") for its WORLD INFO
    // screen and its victory screen; nothing here knows New Game Plus exists.
    // Strings only, so either side can be rebuilt alone. Keep these names:
    // they are a contract, pinned in both DLLs.
    public static class EtInfo
    {
        public const int ContractVersion = 1;

        // WORLD INFO: this world's traits, one line each, with what has
        // happened so far. Empty when there are none.
        public static string[] WorldLines()
        {
            try
            {
                if (!EtRun.Live)
                    return new string[0];

                EtNow now = EtRun.Now;
                List<string> parts = EtRun.Rules.Parts(now.plus);

                if (parts.Count == 0)
                    return new string[0];

                var lines = new List<string>();

                foreach (string p in parts)
                    lines.Add("Enemy " + p);

                var each = new List<string>();

                for (int t = 0; t < EtMath.TraitCount; t++)
                    if (EtDecide.CountOf(t) > 0)
                        each.Add(EtMath.Names[t] + " " + EtDecide.CountOf(t));

                lines.Add("Enemy traits so far: " + EtDecide.WithTraits + " enemies with traits" +
                          (each.Count > 0 ? " (" + string.Join(", ", each.ToArray()) + ")" : "") +
                          (EtShots.Grown > 0 ? ", " + EtShots.Grown + " shots grown" : "") +
                          (EtShots.Bounced > 0 ? ", " + EtShots.Bounced + " bounces" : "") +
                          "; " + EtMath.VolleyText(now.volleys) + ", bosses included");

                return lines.ToArray();
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        // The victory screen: the traits a world at `rulesPlus` would get,
        // from the rules FILE as it is now (they lock at the jump). "" when
        // none.
        public static string NextLine(int rulesPlus)
        {
            try
            {
                return EtRules.LoadFile().Short(rulesPlus);
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
