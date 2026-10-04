using System;
using System.Globalization;

namespace GameModeForge.NewGamePlus
{
    // THE ARITHMETIC OF NEW GAME PLUS, with no Unity and no game types in it,
    // so Harness/ngmathprobe.py can compile exactly this file and run it.
    //
    // Everything here is a number that decides what a world is like, and each
    // one was a decision rather than a guess (CONCEPT.md 2.4):
    //
    //   - a buff STACKS each plus: start + perPlus * (plus - fromPlus), up to
    //     its cap. "+50% health per plus" is +50%, +100%, +150%. No compounding.
    //   - nothing before `fromPlus`. fromPlus 0 means the first world too.
    //   - coverage (the share of enemies that get it) ramps the same way and
    //     can never pass 100%.
    //   - whether ONE enemy is covered is a roll keyed on NG+'s own seed and the
    //     enemy's instance id, so the same enemy gets the same answer after
    //     every Continue with nothing stored per enemy (2.6).
    public static class NgMath
    {
        // A buff's value (a percent) at `plus`. A cap of 0 or less is "no cap".
        public static float Value(bool enabled, int fromPlus, float start,
                                  float perPlus, float max, int plus)
        {
            if (!enabled || plus < fromPlus)
                return 0f;

            float v = start + perPlus * (plus - fromPlus);

            if (max > 0f && v > max)
                v = max;

            return v < 0f ? 0f : v;
        }

        // The share of enemies (a percent, 0-100) that get a buff at `plus`.
        public static float Coverage(bool enabled, int fromPlus, float coverageStart,
                                     float coveragePerPlus, int plus)
        {
            if (!enabled || plus < fromPlus)
                return 0f;

            float c = coverageStart + coveragePerPlus * (plus - fromPlus);

            if (c > 100f)
                c = 100f;

            return c < 0f ? 0f : c;
        }

        // A percent bonus as the multiplier it becomes: +50 -> x1.5.
        public static float Multiplier(float percent)
        {
            return 1f + (percent < 0f ? 0f : percent) / 100f;
        }

        // ---- enemy damage (2026-10-02) -------------------------------------
        //
        // WHO A HIT CAME FROM, as NgDamage sorts it. Enemy damage % is on every
        // enemy (no coverage - his call) and on the world's own hazards, to
        // everything they hit; anything of yours is never touched; and a hit
        // whose source cannot be told is left alone, because this mod only ever
        // ADDS difficulty and every doubt resolves to "add nothing".
        public const int SideUnknown = 0;   // could not tell
        public const int SidePlayer = 1;    // you, your drones, minions, wingmen, a co-op partner
        public const int SideEnemy = 2;     // an enemy, bosses included
        public const int SideWorld = 3;     // hazard blocks, white bulbs, exploding plants, steam
        // A crate or box: nobody's (DECIDED 2026-10-02 - "I wouldn't count
        // crates as hazards please"). Left alone like yours and never counted.
        // Measured the same day: none of the 13 does any damage anyway.
        public const int SideNeutral = 4;

        // The multiplier a hit from `side` takes this world.
        public static float DamageScale(int side, float percent, bool environment)
        {
            if (side == SideEnemy)
                return Multiplier(percent);

            if (side == SideWorld && environment)
                return Multiplier(percent);

            return 1f;
        }

        // A beam's or an electric spark's target mask. Aimed at the Player
        // layer and not at Entities is an enemy's (every enemy laser ships
        // 9280); aimed at Entities and not at Player is yours (9344); both or
        // neither cannot be told.
        public static int SideFromMask(int mask, int playerLayer, int entitiesLayer)
        {
            if (playerLayer < 0 || playerLayer > 31 || entitiesLayer < 0 || entitiesLayer > 31)
                return SideUnknown;

            bool player = (mask & (1 << playerLayer)) != 0;
            bool entities = (mask & (1 << entitiesLayer)) != 0;

            if (player && !entities)
                return SideEnemy;

            if (entities && !player)
                return SidePlayer;

            return SideUnknown;
        }

        // A shot with no owner (a sub-emitter fragment): its GameObject layer
        // IS its faction.
        public static int SideFromShotLayer(int layer, int enemyShots, int playerShots)
        {
            if (layer >= 0 && layer == enemyShots)
                return SideEnemy;

            if (layer >= 0 && layer == playerShots)
                return SidePlayer;

            return SideUnknown;
        }

        // A number in [0, 1), the same for the same three inputs on every
        // machine and every launch. A plain integer mix (the finaliser of
        // murmur3), so it needs no System.Random - whose sequence is not
        // promised across .NET versions - and no state.
        public static float Roll(int seed, int id, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)id * 0x85EBCA77u;
                h ^= (uint)salt * 0xC2B2AE3Du;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;

                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        // Is this enemy one of the covered share?
        public static bool Covered(int seed, int id, int salt, float coveragePercent)
        {
            if (coveragePercent >= 100f)
                return true;

            if (coveragePercent <= 0f)
                return false;

            return Roll(seed, id, salt) * 100f < coveragePercent;
        }

        // ★ THE STATIONS-UNLOCKED COUNT ACROSS A JUMP. `RunData.Initialize`
        // itself counts 1 (the starter shop tier) before any station, so a
        // fresh world's count is 1 + whatever it has unlocked by the time the
        // carry is applied (its start station). Carrying the old count
        // wholesale would lose the new world's start station; adding the two
        // would count the starter tier twice.
        public static int MergeStationCount(int carried, int fresh)
        {
            return carried + Math.Max(0, fresh - 1);
        }

        // The in-game sliders: a step index and the percent it stands for.
        public static int SliderPercent(int step, int percentPerStep)
        {
            return Math.Max(0, step) * percentPerStep;
        }

        public static int SliderStep(float percent, int percentPerStep, int steps)
        {
            if (percentPerStep <= 0)
                return 0;

            int s = (int)Math.Round(percent / percentPerStep, MidpointRounding.AwayFromZero);

            return s < 0 ? 0 : (s > steps ? steps : s);
        }

        // "+50%", "+12.5%" - never "50.0000001".
        public static string Pct(float percent)
        {
            return "+" + Math.Round(percent, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        // What a world is called: the first world is WORLD 1, the one after the
        // first jump is NEW GAME PLUS 1 (DECIDED 2026-09-29 - the victory title,
        // the arrival alert and WORLD INFO all use this one name).
        public static string WorldName(int plus)
        {
            return plus <= 0
                ? "WORLD 1"
                : "NEW GAME PLUS " + plus.ToString(CultureInfo.InvariantCulture);
        }
    }
}
