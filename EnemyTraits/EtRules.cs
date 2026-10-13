using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameModeForge.EnemyTraits
{
    // ONE NUMBER OF A TRAIT, with its per-plus box (DECIDED 3.6: "every number
    // in a trait can take a per plus box"). In the file it is either a plain
    // number, or {"start": 1.5, "perPlus": 0.25, "max": 3}. It grows from the
    // TRAIT's own fromPlus.
    public sealed class EtNum
    {
        public float start;
        public float perPlus;
        public float max;
        public bool hasMax;

        public EtNum()
        {
        }

        public EtNum(float start)
        {
            this.start = start;
        }

        public EtNum(float start, float perPlus, float max)
        {
            this.start = start;
            this.perPlus = perPlus;
            this.max = max;
            hasMax = true;
        }

        public EtNum Clone()
        {
            return (EtNum)MemberwiseClone();
        }

        public float At(int fromPlus, int plus)
        {
            return EtMath.Stepped(start, perPlus, max, hasMax, fromPlus, plus);
        }

        public JToken ToJ()
        {
            if (perPlus == 0f && !hasMax)
                return start;

            var o = new JObject { ["start"] = start, ["perPlus"] = perPlus };

            if (hasMax)
                o["max"] = max;

            return o;
        }

        // A number, or the three-key object. Anything else is reported and
        // the default kept.
        public static EtNum Read(JToken t, EtNum fallback, string where, List<string> problems)
        {
            if (t == null)
                return fallback.Clone();

            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                return new EtNum((float)t);

            var o = t as JObject;

            if (o == null)
            {
                problems.Add(where + " should be a number or {start, perPlus, max} - kept " + EtMath.Num(fallback.start));
                return fallback.Clone();
            }

            var n = new EtNum(Float(o, "start", fallback.start, where, problems));
            n.perPlus = Float(o, "perPlus", 0f, where, problems);

            if (o["max"] != null && o["max"].Type != JTokenType.Null)
            {
                n.max = Float(o, "max", 0f, where, problems);
                n.hasMax = true;
            }

            foreach (JProperty p in o.Properties())
            {
                if (p.Name != "start" && p.Name != "perPlus" && p.Name != "max")
                    problems.Add(where + ": unknown key '" + p.Name + "' (a number takes start, perPlus and max)");
            }

            return n;
        }

        internal static float Float(JObject o, string key, float fallback, string where, List<string> problems)
        {
            JToken t = o[key];

            if (t == null || t.Type == JTokenType.Null)
                return fallback;

            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                return (float)t;

            problems.Add(where + "." + key + " should be a number - kept " + EtMath.Num(fallback));
            return fallback;
        }
    }

    // THE REWARD SET - every one of Module Forge's windup rewards (DECIDED
    // 3.6: "every Module Forge windup setting ... all of the rewards in the
    // file and the page"), each `...AtFull` a multiplier at a full meter.
    // Fire rate and accuracy default ON, the rest wait to be named.
    public sealed class EtRewardRules
    {
        public static readonly string[] FlagNames =
        {
            "rate", "accuracy", "damage", "pellets", "cheaper", "speed", "range", "size",
            "burst", "burn", "explosion", "push", "warmup", "lifetime",
        };

        public static readonly string[] NumberNames =
        {
            "fireRateAtFull", "accuracyAtFull", "spreadAtFull", "damageAtFull", "pelletsAtFull",
            "costAtFull", "speedAtFull", "rangeAtFull", "sizeAtFull", "burstAtFull",
            "burstDelayAtFull", "burnAtFull", "explosionAtFull", "pushAtFull", "knockbackAtFull",
            "warmupAtFull", "lifetimeAtFull",
        };

        public readonly Dictionary<string, bool> flags = new Dictionary<string, bool>();
        public readonly Dictionary<string, EtNum> numbers = new Dictionary<string, EtNum>();

        // Module Forge's own defaults, with the fire rate given per trait.
        public static EtRewardRules Defaults(EtNum fireRate)
        {
            var r = new EtRewardRules();

            foreach (string f in FlagNames)
                r.flags[f] = f == "rate" || f == "accuracy";

            r.numbers["fireRateAtFull"] = fireRate;
            r.numbers["accuracyAtFull"] = new EtNum(0.5f);   // half the scatter: "tighter aim"
            r.numbers["spreadAtFull"] = new EtNum(1f);       // the deliberate fan, kept
            r.numbers["damageAtFull"] = new EtNum(1.5f);
            r.numbers["pelletsAtFull"] = new EtNum(2f);
            r.numbers["costAtFull"] = new EtNum(0.5f);
            r.numbers["speedAtFull"] = new EtNum(1.5f);
            r.numbers["rangeAtFull"] = new EtNum(1.5f);
            r.numbers["sizeAtFull"] = new EtNum(1.5f);
            r.numbers["burstAtFull"] = new EtNum(2f);
            r.numbers["burstDelayAtFull"] = new EtNum(1f);
            r.numbers["burnAtFull"] = new EtNum(2f);
            r.numbers["explosionAtFull"] = new EtNum(1.5f);
            r.numbers["pushAtFull"] = new EtNum(2f);
            r.numbers["knockbackAtFull"] = new EtNum(1f);
            r.numbers["warmupAtFull"] = new EtNum(0f);
            r.numbers["lifetimeAtFull"] = new EtNum(1.5f);
            return r;
        }

        public EtRewardRules Clone()
        {
            var r = new EtRewardRules();

            foreach (var kv in flags)
                r.flags[kv.Key] = kv.Value;

            foreach (var kv in numbers)
                r.numbers[kv.Key] = kv.Value.Clone();

            return r;
        }

        public bool On(string flag)
        {
            bool v;
            return flags.TryGetValue(flag, out v) && v;
        }

        public float At(string number, int fromPlus, int plus)
        {
            EtNum n;
            return numbers.TryGetValue(number, out n) ? n.At(fromPlus, plus) : 1f;
        }

        public JObject ToJ()
        {
            var o = new JObject();

            foreach (string f in FlagNames)
                o[f] = On(f);

            foreach (string n in NumberNames)
                o[n] = numbers[n].ToJ();

            return o;
        }

        public static EtRewardRules Read(JToken t, EtRewardRules d, string where, List<string> problems)
        {
            var o = t as JObject;

            if (t == null)
                return d.Clone();

            if (o == null)
            {
                problems.Add(where + " should be an object - kept the defaults");
                return d.Clone();
            }

            var r = d.Clone();

            foreach (JProperty p in o.Properties())
            {
                if (Array.IndexOf(FlagNames, p.Name) >= 0)
                {
                    if (p.Value.Type == JTokenType.Boolean)
                        r.flags[p.Name] = (bool)p.Value;
                    else
                        problems.Add(where + "." + p.Name + " should be true or false");
                }
                else if (Array.IndexOf(NumberNames, p.Name) >= 0)
                {
                    r.numbers[p.Name] = EtNum.Read(p.Value, d.numbers[p.Name], where + "." + p.Name, problems);
                }
                else
                {
                    problems.Add(where + ": unknown reward '" + p.Name + "'");
                }
            }

            return r;
        }

        // "x1.5 fire rate, 50% tighter aim" - the rewards that move a number.
        public string Text(int fromPlus, int plus)
        {
            var parts = new List<string>();

            if (On("rate")) parts.Add(EtMath.Times(At("fireRateAtFull", fromPlus, plus)) + " fire rate");
            if (On("accuracy"))
            {
                float a = At("accuracyAtFull", fromPlus, plus);
                if (a > 0f) parts.Add(EtMath.Pct(a * 100f) + " tighter aim");
                float s = At("spreadAtFull", fromPlus, plus);
                if (Math.Abs(s - 1f) > 0.001f) parts.Add(EtMath.Times(s) + " spread");
            }
            if (On("damage")) parts.Add(EtMath.Times(At("damageAtFull", fromPlus, plus)) + " damage");
            if (On("pellets")) parts.Add(EtMath.Times(At("pelletsAtFull", fromPlus, plus)) + " bullets");
            if (On("cheaper")) parts.Add(EtMath.Times(At("costAtFull", fromPlus, plus)) + " ammo a shot");
            if (On("speed")) parts.Add(EtMath.Times(At("speedAtFull", fromPlus, plus)) + " shot speed");
            if (On("range")) parts.Add(EtMath.Times(At("rangeAtFull", fromPlus, plus)) + " range");
            if (On("size")) parts.Add(EtMath.Times(At("sizeAtFull", fromPlus, plus)) + " shot size");
            if (On("burst")) parts.Add(EtMath.Times(At("burstAtFull", fromPlus, plus)) + " burst");
            if (On("burn")) parts.Add(EtMath.Times(At("burnAtFull", fromPlus, plus)) + " burn");
            if (On("explosion")) parts.Add(EtMath.Times(At("explosionAtFull", fromPlus, plus)) + " blast");
            if (On("push")) parts.Add(EtMath.Times(At("pushAtFull", fromPlus, plus)) + " push");
            if (On("warmup")) parts.Add(EtMath.Times(At("warmupAtFull", fromPlus, plus)) + " warm-up");
            if (On("lifetime")) parts.Add(EtMath.Times(At("lifetimeAtFull", fromPlus, plus)) + " lifetime");

            return parts.Count == 0 ? "no reward" : string.Join(", ", parts.ToArray());
        }
    }

    // What every trait has: on/off, the plus it starts at, the share of
    // enemies it reaches.
    public abstract class EtTraitRules
    {
        public bool enabled = true;
        public int fromPlus = 1;
        public EtNum coverage = new EtNum(100f);

        public bool ActiveAt(int plus)
        {
            return EtMath.Active(enabled, fromPlus, plus);
        }

        public float CoverageAt(int plus)
        {
            return EtMath.Coverage(enabled, fromPlus, coverage.start, coverage.perPlus, plus);
        }

        public float At(EtNum n, int plus)
        {
            return n.At(fromPlus, plus);
        }

        internal void BaseToJ(JObject o)
        {
            o["enabled"] = enabled;
            o["fromPlus"] = fromPlus;
            o["coverage"] = coverage.ToJ();
        }

        // The three common keys; true when the key was one of them.
        internal bool ReadBase(JProperty p, string where, List<string> problems)
        {
            switch (p.Name)
            {
                case "enabled":
                    if (p.Value.Type == JTokenType.Boolean)
                        enabled = (bool)p.Value;
                    else
                        problems.Add(where + ".enabled should be true or false");
                    return true;
                case "fromPlus":
                    if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                        fromPlus = Math.Max(0, (int)Math.Round((double)p.Value));
                    else
                        problems.Add(where + ".fromPlus should be a whole number");
                    return true;
                case "coverage":
                    coverage = EtNum.Read(p.Value, coverage, where + ".coverage", problems);
                    return true;
            }

            return false;
        }

        internal static bool Bool(JProperty p, bool fallback, string where, List<string> problems)
        {
            if (p.Value.Type == JTokenType.Boolean)
                return (bool)p.Value;

            problems.Add(where + "." + p.Name + " should be true or false");
            return fallback;
        }

        internal static string Word(JProperty p, string fallback, string[] allowed, string where, List<string> problems)
        {
            string s = p.Value.Type == JTokenType.String ? ((string)p.Value ?? "").Trim().ToLowerInvariant() : null;

            if (s != null && Array.IndexOf(allowed, s) >= 0)
                return s;

            problems.Add(where + "." + p.Name + " should be one of " + string.Join(", ", allowed) +
                         " - kept " + fallback);
            return fallback;
        }
    }

    // WIND-UP - the enemy's guns fire faster the longer it fires (Module
    // Forge's windup; every setting). DEFAULTS (3.6): full after 2 s of firing,
    // x1.5 fire rate and half the scatter at full, the fire rate +0.25 a plus
    // to x3; winds down over 1 s, starting 0.25 s after the last shot.
    public sealed class EtWindupRules : EtTraitRules
    {
        public EtNum windUpTime = new EtNum(2f);
        public EtNum windDownTime = new EtNum(1f);
        public EtNum grace = new EtNum(0.25f);
        public string ramp = "smooth";
        public int steps = 3;
        public EtRewardRules rewards = EtRewardRules.Defaults(new EtNum(1.5f, 0.25f, 3f));

        public static readonly string[] Ramps = { "smooth", "stepped" };

        public EtWindupRules Clone()
        {
            var c = (EtWindupRules)MemberwiseClone();
            c.coverage = coverage.Clone();
            c.windUpTime = windUpTime.Clone();
            c.windDownTime = windDownTime.Clone();
            c.grace = grace.Clone();
            c.rewards = rewards.Clone();
            return c;
        }

        public JObject ToJ()
        {
            var o = new JObject();
            BaseToJ(o);
            o["windUpTime"] = windUpTime.ToJ();
            o["windDownTime"] = windDownTime.ToJ();
            o["grace"] = grace.ToJ();
            o["ramp"] = ramp;
            o["steps"] = steps;
            o["rewards"] = rewards.ToJ();
            return o;
        }

        public static EtWindupRules Read(JObject o, EtWindupRules d, List<string> problems)
        {
            var r = d.Clone();
            const string W = "windup";

            foreach (JProperty p in o.Properties())
            {
                if (r.ReadBase(p, W, problems))
                    continue;

                switch (p.Name)
                {
                    case "windUpTime": r.windUpTime = EtNum.Read(p.Value, d.windUpTime, W + ".windUpTime", problems); break;
                    case "windDownTime": r.windDownTime = EtNum.Read(p.Value, d.windDownTime, W + ".windDownTime", problems); break;
                    case "grace": r.grace = EtNum.Read(p.Value, d.grace, W + ".grace", problems); break;
                    case "ramp": r.ramp = Word(p, d.ramp, Ramps, W, problems); break;
                    case "steps":
                        if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                            r.steps = Math.Max(1, (int)Math.Round((double)p.Value));
                        else
                            problems.Add(W + ".steps should be a whole number");
                        break;
                    case "rewards": r.rewards = EtRewardRules.Read(p.Value, d.rewards, W + ".rewards", problems); break;
                    default: problems.Add(W + ": unknown key '" + p.Name + "'"); break;
                }
            }

            return r;
        }

        public string Text(int plus)
        {
            return "wind-up: " + rewards.Text(fromPlus, plus) + " after " +
                   EtMath.Num(At(windUpTime, plus)) + " s of firing";
        }
    }

    // OVERLOAD - Weapon Forge's surge, the window only (DECIDED 3.2: "They get
    // the benefits, not the negatives" - no lockout, no vent). An invisible
    // meter fills as the enemy fires; when full it blows and the guns get
    // better for the window. DEFAULTS (3.6): fills over 4 s of firing, the
    // window lasts 3 s fading, x2 fire rate and half the scatter at the blow,
    // the fire rate +0.25 a plus to x4. Between windows the meter cools after
    // 1 s of not firing, from full to empty in 8 s.
    public sealed class EtOverloadRules : EtTraitRules
    {
        public string fill = "fire";
        public EtNum fillTime = new EtNum(4f);
        public EtNum shots = new EtNum(20f);
        public EtNum hits = new EtNum(3f);
        public EtNum window = new EtNum(3f);
        public string ramp = "fade";
        public EtNum coolDelay = new EtNum(1f);
        public EtNum coolTime = new EtNum(8f);
        public EtRewardRules rewards = EtRewardRules.Defaults(new EtNum(2f, 0.25f, 4f));

        public static readonly string[] Fills = { "fire", "shots", "hits" };
        public static readonly string[] Ramps = { "fade", "flat" };

        public EtOverloadRules Clone()
        {
            var c = (EtOverloadRules)MemberwiseClone();
            c.coverage = coverage.Clone();
            c.fillTime = fillTime.Clone();
            c.shots = shots.Clone();
            c.hits = hits.Clone();
            c.window = window.Clone();
            c.coolDelay = coolDelay.Clone();
            c.coolTime = coolTime.Clone();
            c.rewards = rewards.Clone();
            return c;
        }

        public JObject ToJ()
        {
            var o = new JObject();
            BaseToJ(o);
            o["fill"] = fill;
            o["fillTime"] = fillTime.ToJ();
            o["shots"] = shots.ToJ();
            o["hits"] = hits.ToJ();
            o["window"] = window.ToJ();
            o["ramp"] = ramp;
            o["coolDelay"] = coolDelay.ToJ();
            o["coolTime"] = coolTime.ToJ();
            o["rewards"] = rewards.ToJ();
            return o;
        }

        public static EtOverloadRules Read(JObject o, EtOverloadRules d, List<string> problems)
        {
            var r = d.Clone();
            const string W = "overload";

            foreach (JProperty p in o.Properties())
            {
                if (r.ReadBase(p, W, problems))
                    continue;

                switch (p.Name)
                {
                    case "fill": r.fill = Word(p, d.fill, Fills, W, problems); break;
                    case "fillTime": r.fillTime = EtNum.Read(p.Value, d.fillTime, W + ".fillTime", problems); break;
                    case "shots": r.shots = EtNum.Read(p.Value, d.shots, W + ".shots", problems); break;
                    case "hits": r.hits = EtNum.Read(p.Value, d.hits, W + ".hits", problems); break;
                    case "window": r.window = EtNum.Read(p.Value, d.window, W + ".window", problems); break;
                    case "ramp": r.ramp = Word(p, d.ramp, Ramps, W, problems); break;
                    case "coolDelay": r.coolDelay = EtNum.Read(p.Value, d.coolDelay, W + ".coolDelay", problems); break;
                    case "coolTime": r.coolTime = EtNum.Read(p.Value, d.coolTime, W + ".coolTime", problems); break;
                    case "rewards": r.rewards = EtRewardRules.Read(p.Value, d.rewards, W + ".rewards", problems); break;
                    default: problems.Add(W + ": unknown key '" + p.Name + "'"); break;
                }
            }

            return r;
        }

        public string FillText(int plus)
        {
            switch (fill)
            {
                case "shots": return EtMath.Num(At(shots, plus)) + " shots";
                case "hits": return EtMath.Num(At(hits, plus)) + " hits on you";
                default: return EtMath.Num(At(fillTime, plus)) + " s of firing";
            }
        }

        public string Text(int plus)
        {
            return "overload: " + rewards.Text(fromPlus, plus) + " for " + EtMath.Num(At(window, plus)) +
                   " s after " + FillText(plus);
        }
    }

    // GROW - enemy shots swell over their range, hitbox included (Module
    // Forge's grow). Size only: damage stays New Game Plus's enemy damage %.
    // DEFAULTS (3.6): x1 to x2 over the shot's range, the end size +0.5 a plus
    // to x4.
    public sealed class EtGrowRules : EtTraitRules
    {
        public EtNum from = new EtNum(1f);
        public EtNum to = new EtNum(2f, 0.5f, 4f);
        public bool overTime;
        public EtNum span = new EtNum(0f);
        public bool hitbox = true;
        public EtNum curve = new EtNum(1f);
        public bool clamp = true;
        public bool rockets = true;

        public EtGrowRules Clone()
        {
            var c = (EtGrowRules)MemberwiseClone();
            c.coverage = coverage.Clone();
            c.from = from.Clone();
            c.to = to.Clone();
            c.span = span.Clone();
            c.curve = curve.Clone();
            return c;
        }

        public JObject ToJ()
        {
            var o = new JObject();
            BaseToJ(o);
            o["from"] = from.ToJ();
            o["to"] = to.ToJ();
            o["overTime"] = overTime;
            o["span"] = span.ToJ();
            o["hitbox"] = hitbox;
            o["curve"] = curve.ToJ();
            o["clamp"] = clamp;
            o["rockets"] = rockets;
            return o;
        }

        public static EtGrowRules Read(JObject o, EtGrowRules d, List<string> problems)
        {
            var r = d.Clone();
            const string W = "grow";

            foreach (JProperty p in o.Properties())
            {
                if (r.ReadBase(p, W, problems))
                    continue;

                switch (p.Name)
                {
                    case "from": r.from = EtNum.Read(p.Value, d.from, W + ".from", problems); break;
                    case "to": r.to = EtNum.Read(p.Value, d.to, W + ".to", problems); break;
                    case "overTime": r.overTime = Bool(p, d.overTime, W, problems); break;
                    case "span": r.span = EtNum.Read(p.Value, d.span, W + ".span", problems); break;
                    case "hitbox": r.hitbox = Bool(p, d.hitbox, W, problems); break;
                    case "curve": r.curve = EtNum.Read(p.Value, d.curve, W + ".curve", problems); break;
                    case "clamp": r.clamp = Bool(p, d.clamp, W, problems); break;
                    case "rockets": r.rockets = Bool(p, d.rockets, W, problems); break;
                    default: problems.Add(W + ": unknown key '" + p.Name + "'"); break;
                }
            }

            return r;
        }

        public string Text(int plus)
        {
            return "grow: shots " + EtMath.Times(At(from, plus)) + " to " + EtMath.Times(At(to, plus)) +
                   (overTime ? " over their life" : " over their range") +
                   (rockets ? "" : " (bullets only)");
        }
    }

    // RICOCHET - enemy shots bounce off terrain. DECIDED: bullets AND rockets
    // / bombs (3.6); only terrain is a bounce - touching you, your drones,
    // minions or wingmen ends the shot; a bounce drops a rocket's lock (3.7)
    // and "toward you" (off by default) re-aims it at you. DEFAULTS: 1 bounce
    // in NEW GAME PLUS 1, +1 a plus, to 5. A lobbed shot comes off the floor
    // at `bounciness` of the speed it landed with.
    public sealed class EtRicochetRules : EtTraitRules
    {
        public EtNum bounces = new EtNum(1f, 1f, 5f);
        public bool towardYou;
        public bool rockets = true;
        public EtNum bounciness = new EtNum(0.6f);

        public EtRicochetRules Clone()
        {
            var c = (EtRicochetRules)MemberwiseClone();
            c.coverage = coverage.Clone();
            c.bounces = bounces.Clone();
            c.bounciness = bounciness.Clone();
            return c;
        }

        public JObject ToJ()
        {
            var o = new JObject();
            BaseToJ(o);
            o["bounces"] = bounces.ToJ();
            o["towardYou"] = towardYou;
            o["rockets"] = rockets;
            o["bounciness"] = bounciness.ToJ();
            return o;
        }

        public static EtRicochetRules Read(JObject o, EtRicochetRules d, List<string> problems)
        {
            var r = d.Clone();
            const string W = "ricochet";

            foreach (JProperty p in o.Properties())
            {
                if (r.ReadBase(p, W, problems))
                    continue;

                switch (p.Name)
                {
                    case "bounces": r.bounces = EtNum.Read(p.Value, d.bounces, W + ".bounces", problems); break;
                    case "towardYou": r.towardYou = Bool(p, d.towardYou, W, problems); break;
                    case "rockets": r.rockets = Bool(p, d.rockets, W, problems); break;
                    case "bounciness": r.bounciness = EtNum.Read(p.Value, d.bounciness, W + ".bounciness", problems); break;
                    default: problems.Add(W + ": unknown key '" + p.Name + "'"); break;
                }
            }

            return r;
        }

        public int BouncesAt(int plus)
        {
            return Math.Max(0, (int)Math.Floor(At(bounces, plus) + 0.0001f));
        }

        public string Text(int plus)
        {
            int n = BouncesAt(plus);
            return "ricochet: " + n + (n == 1 ? " bounce" : " bounces") + " off walls" +
                   (rockets ? ", rockets too" : ", bullets only") +
                   (towardYou ? ", toward you" : "");
        }
    }

    // THE OPTIONAL LIMIT (DECIDED 3.6: off by default) - at most this many
    // traits on one enemy, 1 in NEW GAME PLUS 1 and +1 a plus.
    public sealed class EtLimitRules
    {
        public bool enabled;
        public int fromPlus = 1;
        public EtNum count = new EtNum(1f, 1f, 4f);

        public EtLimitRules Clone()
        {
            var c = (EtLimitRules)MemberwiseClone();
            c.count = count.Clone();
            return c;
        }

        // -1: no limit at this plus.
        public int At(int plus)
        {
            if (!enabled || plus < fromPlus)
                return -1;

            return Math.Max(0, (int)Math.Floor(count.At(fromPlus, plus) + 0.0001f));
        }

        public JObject ToJ()
        {
            return new JObject { ["enabled"] = enabled, ["fromPlus"] = fromPlus, ["count"] = count.ToJ() };
        }

        public static EtLimitRules Read(JObject o, EtLimitRules d, List<string> problems)
        {
            var r = d.Clone();

            foreach (JProperty p in o.Properties())
            {
                switch (p.Name)
                {
                    case "enabled": r.enabled = EtTraitRules.Bool(p, d.enabled, "limit", problems); break;
                    case "fromPlus":
                        if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                            r.fromPlus = Math.Max(0, (int)Math.Round((double)p.Value));
                        else
                            problems.Add("limit.fromPlus should be a whole number");
                        break;
                    case "count": r.count = EtNum.Read(p.Value, d.count, "limit.count", problems); break;
                    default: problems.Add("limit: unknown key '" + p.Name + "'"); break;
                }
            }

            return r;
        }
    }

    // THE RULES FILE - BepInEx\config\com.sugarheady.gamemodeforge.enemytraits.rules.json,
    // made by Enemy Traits Builder.html. LOCKED PER WORLD (3.6): read when a
    // world begins (the start of a run, every New Game Plus jump), kept in the
    // save's record, so a Continue gives the same traits.
    public sealed class EtRules
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string volleys = "speed";
        public EtLimitRules limit = new EtLimitRules();
        public EtWindupRules windup = new EtWindupRules();
        public EtGrowRules grow = new EtGrowRules();
        public EtOverloadRules overload = new EtOverloadRules();
        public EtRicochetRules ricochet = new EtRicochetRules();

        public static readonly string[] VolleyWords = EtMath.VolleyWords;

        public static string FilePath
        {
            get { return Path.Combine(Paths.ConfigPath, EnemyTraitsPlugin.Guid + ".rules.json"); }
        }

        // DECIDED (3.6): ALL FOUR ON FROM NEW GAME PLUS 1, every enemy.
        public static EtRules Defaults()
        {
            return new EtRules();
        }

        // Nothing in force: a save made before Enemy Traits was installed, or
        // a file that cannot be read.
        public static EtRules None()
        {
            EtRules r = Defaults();
            r.windup.enabled = false;
            r.grow.enabled = false;
            r.overload.enabled = false;
            r.ricochet.enabled = false;
            return r;
        }

        public EtRules Clone()
        {
            return new EtRules
            {
                version = version,
                volleys = volleys,
                limit = limit.Clone(),
                windup = windup.Clone(),
                grow = grow.Clone(),
                overload = overload.Clone(),
                ricochet = ricochet.Clone()
            };
        }

        public EtTraitRules Trait(int t)
        {
            switch (t)
            {
                case EtMath.Windup: return windup;
                case EtMath.Grow: return grow;
                case EtMath.Overload: return overload;
                case EtMath.Ricochet: return ricochet;
            }

            return null;
        }

        public int VolleyMode
        {
            get { return EtMath.ParseVolleys(volleys); }
        }

        public bool AnyAt(int plus)
        {
            for (int t = 0; t < EtMath.TraitCount; t++)
                if (Trait(t).CoverageAt(plus) > 0f)
                    return true;

            return false;
        }

        // The traits in force at `plus`, one part each ("wind-up: x1.5 fire
        // rate ... (on 50% of them)").
        public List<string> Parts(int plus)
        {
            var parts = new List<string>();

            for (int t = 0; t < EtMath.TraitCount; t++)
            {
                float c = Trait(t).CoverageAt(plus);

                if (c <= 0f)
                    continue;

                string text;

                switch (t)
                {
                    case EtMath.Windup: text = windup.Text(plus); break;
                    case EtMath.Grow: text = grow.Text(plus); break;
                    case EtMath.Overload: text = overload.Text(plus); break;
                    default: text = ricochet.Text(plus); break;
                }

                parts.Add(text + (c < 100f ? " (on " + EtMath.Pct(c) + " of them)" : ""));
            }

            int cap = limit.At(plus);

            if (cap >= 0 && parts.Count > cap)
                parts.Add("at most " + cap + (cap == 1 ? " trait" : " traits") + " an enemy");

            return parts;
        }

        // The short line: "enemy traits: wind-up, grow, overload, ricochet".
        public string Short(int plus)
        {
            var names = new List<string>();

            for (int t = 0; t < EtMath.TraitCount; t++)
            {
                float c = Trait(t).CoverageAt(plus);

                if (c > 0f)
                    names.Add(EtMath.Names[t] + (c < 100f ? " (" + EtMath.Pct(c) + ")" : ""));
            }

            if (names.Count == 0)
                return "";

            int cap = limit.At(plus);

            return "enemy traits: " + string.Join(", ", names.ToArray()) +
                   (cap >= 0 && names.Count > cap ? " - at most " + cap + " an enemy" : "");
        }

        // ---- JSON ------------------------------------------------------------

        public JObject ToJObject()
        {
            return new JObject
            {
                ["version"] = version,
                ["volleys"] = volleys,
                ["limit"] = limit.ToJ(),
                ["windup"] = windup.ToJ(),
                ["grow"] = grow.ToJ(),
                ["overload"] = overload.ToJ(),
                ["ricochet"] = ricochet.ToJ()
            };
        }

        public string ToJson(bool indented)
        {
            return ToJObject().ToString(indented ? Formatting.Indented : Formatting.None);
        }

        public static EtRules Parse(string text, List<string> problems)
        {
            return Parse(text, problems, false);
        }

        // `locked`: a world's LOCKED rules out of a save's record. A trait the
        // text does not name is OFF, never its default (the New Game Plus
        // rule): a world locked before a trait existed keeps playing without
        // it. Only the FILE takes a new trait's default.
        public static EtRules Parse(string text, List<string> problems, bool locked)
        {
            JObject o;

            try
            {
                o = JObject.Parse(text ?? "");
            }
            catch (Exception ex)
            {
                problems.Add("the file is not valid JSON (" + ex.Message + ")");
                return null;
            }

            EtRules r = Defaults();
            EtRules d = Defaults();

            if (locked)
            {
                foreach (string k in EtMath.Keys)
                    if (o[k] == null)
                        r.Trait(Array.IndexOf(EtMath.Keys, k)).enabled = false;
            }

            foreach (JProperty p in o.Properties())
            {
                switch (p.Name)
                {
                    case "version":
                        if (p.Value.Type == JTokenType.Integer)
                        {
                            r.version = (int)p.Value;

                            if (r.version > CurrentVersion)
                                problems.Add("the file is version " + r.version + " and this build reads " +
                                             CurrentVersion + " - reading what it knows");
                        }
                        break;

                    case "volleys":
                        string v = p.Value.Type == JTokenType.String ? ((string)p.Value ?? "").Trim().ToLowerInvariant() : null;

                        if (v != null && Array.IndexOf(EtMath.VolleyWords, v) >= 0)
                            r.volleys = v;
                        else
                            problems.Add("volleys should be speed, count or off - kept " + d.volleys);
                        break;

                    case "limit":
                        if (p.Value is JObject)
                            r.limit = EtLimitRules.Read((JObject)p.Value, d.limit, problems);
                        else
                            problems.Add("limit should be an object");
                        break;

                    case "windup":
                        if (p.Value is JObject)
                            r.windup = EtWindupRules.Read((JObject)p.Value, d.windup, problems);
                        else
                            problems.Add("windup should be an object");
                        break;

                    case "grow":
                        if (p.Value is JObject)
                            r.grow = EtGrowRules.Read((JObject)p.Value, d.grow, problems);
                        else
                            problems.Add("grow should be an object");
                        break;

                    case "overload":
                        if (p.Value is JObject)
                            r.overload = EtOverloadRules.Read((JObject)p.Value, d.overload, problems);
                        else
                            problems.Add("overload should be an object");
                        break;

                    case "ricochet":
                        if (p.Value is JObject)
                            r.ricochet = EtRicochetRules.Read((JObject)p.Value, d.ricochet, problems);
                        else
                            problems.Add("ricochet should be an object");
                        break;

                    default:
                        problems.Add("unknown key '" + p.Name + "' (left alone)");
                        break;
                }
            }

            return r;
        }

        // ---- the file ------------------------------------------------------------

        // The rules as the file says them NOW. A missing file is written with
        // the defaults first; a file that cannot be read means NO traits this
        // world, said loudly (this mod only ever adds, so a doubt adds nothing).
        public static EtRules LoadFile()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                {
                    EtRules d = Defaults();
                    WriteFile(d.ToJObject());
                    EnemyTraitsPlugin.Log.LogInfo(
                        "no rules file yet - wrote the defaults to BepInEx\\config\\" + Path.GetFileName(path) +
                        " (all four traits from NEW GAME PLUS 1, on every enemy). Edit it with Enemy " +
                        "Traits Builder.html, or switch traits in the GAME MODE FORGE tab.");
                    return d;
                }

                var problems = new List<string>();
                EtRules r = Parse(File.ReadAllText(path), problems);

                foreach (string p in problems)
                    EnemyTraitsPlugin.Log.LogWarning("rules file: " + p);

                if (r == null)
                {
                    EnemyTraitsPlugin.Log.LogError(
                        "COULD NOT READ the rules file (" + Path.GetFileName(path) + ") - this world gets NO " +
                        "enemy traits. Fix or delete the file (a deleted file is rewritten with the " +
                        "defaults) and the next world picks it up.");
                    return None();
                }

                return r;
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError(
                    "COULD NOT READ the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - this world gets NO enemy traits.");
                return None();
            }
        }

        // The tab's per-trait rows: `<trait>.enabled`. Every other key in the
        // file - including ones this build does not know - is kept.
        public static bool SetTrait(int trait, bool on)
        {
            if (trait < 0 || trait >= EtMath.TraitCount)
                return false;

            return Edit(o =>
            {
                string key = EtMath.Keys[trait];
                JObject t = o[key] as JObject;

                if (t == null)
                {
                    t = (JObject)Defaults().ToJObject()[key];
                    o[key] = t;
                }

                t["enabled"] = on;
            }, EtMath.Names[trait] + " " + (on ? "ON" : "OFF"));
        }

        // The tab's volleys row.
        public static bool SetVolleys(int mode)
        {
            if (mode < 0 || mode >= EtMath.VolleyWords.Length)
                return false;

            return Edit(o => o["volleys"] = EtMath.VolleyWords[mode], "volleys: " + EtMath.VolleyWords[mode]);
        }

        // The tab's limit row: limit.enabled.
        public static bool SetLimit(bool on)
        {
            return Edit(o =>
            {
                JObject l = o["limit"] as JObject;

                if (l == null)
                {
                    l = Defaults().limit.ToJ();
                    o["limit"] = l;
                }

                l["enabled"] = on;
            }, "the traits-an-enemy limit " + (on ? "ON" : "OFF"));
        }

        private static bool Edit(Action<JObject> change, string what)
        {
            try
            {
                JObject o;
                string path = FilePath;

                if (!File.Exists(path))
                {
                    o = Defaults().ToJObject();
                }
                else
                {
                    try
                    {
                        o = JObject.Parse(File.ReadAllText(path));
                    }
                    catch (Exception)
                    {
                        EnemyTraitsPlugin.Log.LogError(
                            "the rules file is not valid JSON, so the settings tab did NOT write it. Fix it " +
                            "(or delete it to get the defaults back) and try again.");
                        return false;
                    }
                }

                change(o);
                WriteFile(o);

                EnemyTraitsPlugin.Log.LogInfo(
                    "rules file: " + what + " from the settings tab - it applies from the next world " +
                    "(this one keeps the rules it started with).");
                return true;
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the switch has NOT been saved.");
                return false;
            }
        }

        private static void WriteFile(JObject o)
        {
            string path = FilePath;
            string dir = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, o.ToString(Formatting.Indented));
        }
    }
}
