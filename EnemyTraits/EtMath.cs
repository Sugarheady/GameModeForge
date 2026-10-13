using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameModeForge.EnemyTraits
{
    // THE ARITHMETIC OF ENEMY TRAITS, with no Unity and no game types in it,
    // so Harness/etmathprobe.py can compile exactly this file and run it.
    //
    // Every number is one of CONCEPT.md 3.6 / 3.7's decisions:
    //
    //   - a trait is ON from its own `fromPlus` (DECIDED: all four from NEW
    //     GAME PLUS 1 by default, each one's start his to change);
    //   - every number in a trait can grow a plus: start + perPlus x (plus -
    //     fromPlus), stopped at its cap - the per-plus box on every number;
    //   - coverage ramps the same way and never passes 100%;
    //   - whether ONE enemy has a trait is a roll keyed on the world's seed and
    //     the enemy's instance id, so the same enemy keeps the same traits
    //     after every Continue with nothing stored per enemy;
    //   - the optional limit (OFF by default) keeps that many of an enemy's
    //     rolled traits, picked by its own seed;
    //   - wind-up is Module Forge's ramp, overload is Weapon Forge's surge
    //     window with nothing but the window (his "the benefits, not the
    //     negatives");
    //   - a reward that changes a pace another mod may also change is applied
    //     as a RATIO of what it last applied, never from a captured base
    //     (Retarget) - see EtPace.
    public static class EtMath
    {
        // The four traits, in this order everywhere (salts, the limit, the
        // log, the page).
        public const int Windup = 0;
        public const int Grow = 1;
        public const int Overload = 2;
        public const int Ricochet = 3;
        public const int TraitCount = 4;

        public static readonly string[] Keys = { "windup", "grow", "overload", "ricochet" };
        public static readonly string[] Names = { "wind-up", "grow", "overload", "ricochet" };

        // One salt per trait, and one per trait for the limit's pick, so no two
        // rolls of one enemy ever share a number. (New Game Plus uses 1 and
        // 11-18 with its own seed; these are a different seed anyway.)
        public static int CoverSalt(int trait)
        {
            return 21 + trait;
        }

        public static int LimitSalt(int trait)
        {
            return 31 + trait;
        }

        // ---- a number that grows a plus ---------------------------------------

        // start + perPlus x (plus - fromPlus), and stopped at `max` in the
        // direction it is moving (a shrinking number - a shorter wind-up time -
        // stops at its floor). No cap unless one is set.
        public static float Stepped(float start, float perPlus, float max, bool hasMax, int fromPlus, int plus)
        {
            int k = Math.Max(0, plus - fromPlus);
            float v = start + perPlus * k;

            if (hasMax)
            {
                if (perPlus > 0f && v > max)
                    v = max;
                else if (perPlus < 0f && v < max)
                    v = max;
            }

            return v;
        }

        // Is a trait in force at `plus`?
        public static bool Active(bool enabled, int fromPlus, int plus)
        {
            return enabled && plus >= fromPlus;
        }

        // The share of enemies (0-100) a trait reaches at `plus`.
        public static float Coverage(bool enabled, int fromPlus, float start, float perPlus, int plus)
        {
            if (!Active(enabled, fromPlus, plus))
                return 0f;

            float c = start + perPlus * Math.Max(0, plus - fromPlus);

            if (c > 100f)
                c = 100f;

            return c < 0f ? 0f : c;
        }

        // ---- the roll ------------------------------------------------------------

        // A number in [0, 1), the same for the same three inputs on every
        // machine and every launch - New Game Plus's own mix (murmur3's
        // finaliser), copied, so the two mods stand alone.
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

        public static bool Covered(int seed, int id, int salt, float coveragePercent)
        {
            if (coveragePercent >= 100f)
                return true;

            if (coveragePercent <= 0f)
                return false;

            return Roll(seed, id, salt) * 100f < coveragePercent;
        }

        // ★ THE OPTIONAL LIMIT (DECIDED 2026-10-09: off by default). An enemy
        // that rolled more traits than `limit` keeps `limit` of them, picked by
        // its own seed: each rolled trait draws a number and the lowest are
        // kept. The same enemy keeps the same ones after a Continue.
        // `rolled` is changed in place; returns how many were dropped.
        public static int ApplyLimit(bool[] rolled, int limit, int seed, int id)
        {
            if (rolled == null || limit < 0)
                return 0;

            int have = 0;

            for (int i = 0; i < rolled.Length; i++)
                if (rolled[i])
                    have++;

            if (have <= limit)
                return 0;

            var order = new List<int>();

            for (int i = 0; i < rolled.Length; i++)
                if (rolled[i])
                    order.Add(i);

            order.Sort((a, b) =>
            {
                int c = Roll(seed, id, LimitSalt(a)).CompareTo(Roll(seed, id, LimitSalt(b)));
                return c != 0 ? c : a.CompareTo(b);
            });

            int dropped = 0;

            for (int n = limit; n < order.Count; n++)
            {
                rolled[order[n]] = false;
                dropped++;
            }

            return dropped;
        }

        // ---- wind-up (Module Forge's ramp) ---------------------------------------

        // One frame of the meter: up while firing, and after `grace` seconds of
        // not firing, down over `windDownTime`.
        public static float Spin(float spin, bool firing, float dt, float sinceFiring,
                                 float windUpTime, float windDownTime, float grace)
        {
            if (firing)
                spin = windUpTime > 0.0001f ? spin + dt / windUpTime : 1f;
            else if (sinceFiring >= grace)
                spin = windDownTime > 0.0001f ? spin - dt / windDownTime : 0f;

            return Clamp01(spin);
        }

        // 0..1 after the ramp shape: smooth, or in `steps` whole steps (a step
        // is only paid once fully reached).
        public static float Fraction(float spin, bool stepped, int steps)
        {
            spin = Clamp01(spin);

            if (!stepped || steps <= 1)
                return spin;

            return (float)Math.Floor(spin * steps) / steps;
        }

        // ---- overload (Weapon Forge's surge, the window only) ----------------------

        // The meter between windows: up while firing (`fillPerSecond`, 0 when
        // the meter fills some other way), and after `coolDelay` seconds of not
        // firing, down at `coolPerSecond`. While a window is open nothing fills
        // it (his "the benefits", and no refill: a net gain would be a
        // permanent surge). Returns true on the frame the meter is full - the
        // blow.
        public static bool Heat(ref float meter, bool firing, float dt, float sinceFiring,
                                float fillPerSecond, float coolDelay, float coolPerSecond)
        {
            if (firing && fillPerSecond > 0f)
                meter += fillPerSecond * dt;
            else if (!firing && sinceFiring >= coolDelay && coolPerSecond > 0f)
                meter -= coolPerSecond * dt;

            meter = Clamp01(meter);
            return meter >= 1f;
        }

        // A shot or a hit adds a share of the meter.
        public static bool AddHeat(ref float meter, float share)
        {
            meter = Clamp01(meter + (share > 0f ? share : 0f));
            return meter >= 1f;
        }

        // The reward across a window: full at the blow, fading to nothing as it
        // runs out ("fade", the default), or full for the whole window ("flat").
        public static float WindowFraction(float left, float window, bool flat)
        {
            if (left <= 0f || window <= 0f)
                return 0f;

            return flat ? 1f : Clamp01(left / window);
        }

        // ---- the rewards -------------------------------------------------------

        // A reward at ramp position f: 1 at f = 0, `atFull` at f = 1.
        public static float Lerp1(float atFull, float f)
        {
            f = Clamp01(f);
            return 1f + (atFull - 1f) * f;
        }

        // ★ A PACE ANOTHER MOD MAY ALSO HAVE CHANGED, MOVED BY A RATIO. A volley
        // pattern's interval, a beam's damage gap: New Game Plus's fire-rate
        // buff changes them ONCE (when the enemy is decided), and the traits
        // change them every frame. A captured base would lose whichever came
        // second; a ratio of what WE last applied keeps the other's change in
        // any order. `lastApplied` <= 0 is treated as 1 (nothing applied yet).
        public static float Retarget(float current, float lastApplied, float next)
        {
            if (lastApplied <= 0.000001f)
                lastApplied = 1f;

            return current / lastApplied * next;
        }

        // ---- the bounce ---------------------------------------------------------

        // A rocket or lobbed shot off terrain: the part along the surface is
        // kept as the physics left it, the part into the surface comes back out
        // at `closing x e` (Weapon Forge's bounce, simplified). Returns the
        // outgoing velocity; a contact slower than `minSpeed` is not a bounce
        // (bounced = false).
        public static void Rebound(float vx, float vy, float nx, float ny, float closing, float e,
                                   float minSpeed, out float ox, out float oy, out bool bounced)
        {
            bounced = closing >= minSpeed;

            if (!bounced)
            {
                ox = vx;
                oy = vy;
                return;
            }

            float along = vx * nx + vy * ny;
            float tx = vx - along * nx;
            float ty = vy - along * ny;

            ox = tx + nx * closing * e;
            oy = ty + ny * closing * e;
        }

        // ---- words -----------------------------------------------------------------

        public static string Num(float v)
        {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Times(float v)
        {
            return "x" + Num(v);
        }

        public static string Pct(float percent)
        {
            return Math.Round(percent).ToString(CultureInfo.InvariantCulture) + "%";
        }

        public static string WorldName(int plus)
        {
            return plus <= 0
                ? "WORLD 1"
                : "NEW GAME PLUS " + plus.ToString(CultureInfo.InvariantCulture);
        }

        // The volleys setting (DECIDED 2026-10-09, his "count them, speed them
        // up ... otherwise give me an option to disable this or to leave them
        // alone").
        public const int VolleysOff = 0;
        public const int VolleysCount = 1;
        public const int VolleysSpeed = 2;

        public static readonly string[] VolleyWords = { "off", "count", "speed" };

        public static int ParseVolleys(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "off":
                case "none":
                case "no":
                    return VolleysOff;
                case "count":
                    return VolleysCount;
                default:
                    return VolleysSpeed;
            }
        }

        public static string VolleyText(int mode)
        {
            switch (mode)
            {
                case VolleysOff: return "volleys do not count";
                case VolleysCount: return "volleys count, at the game's pace";
                default: return "volleys count and speed up";
            }
        }

        public static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
