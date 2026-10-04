using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameModeForge.NewGamePlus
{
    // One buff's schedule. Every buff has the same controls (CONCEPT.md 2.4):
    // on/off, the plus it starts at, a start value, an amount added per plus, a
    // cap - and, for a buff given to SOME enemies, the coverage ramp.
    public sealed class NgBuff
    {
        public bool enabled;
        public int fromPlus;
        public float start;
        public float perPlus;
        public float max;
        public float coverageStart;
        public float coveragePerPlus;

        // Enemy damage only: does the world's own damage (hazard blocks, the
        // white bulbs, exploding plants, steam) take the % too? (Never crates -
        // they are nobody's, his call 2026-10-02.)
        public bool environment = true;

        public NgBuff Clone()
        {
            return (NgBuff)MemberwiseClone();
        }

        public float ValueAt(int plus)
        {
            return NgMath.Value(enabled, fromPlus, start, perPlus, max, plus);
        }

        public float CoverageAt(int plus)
        {
            return NgMath.Coverage(enabled, fromPlus, coverageStart, coveragePerPlus, plus);
        }
    }

    // THE RULES FILE - `BepInEx\config\com.sugarheady.gamemodeforge.newgameplus.rules.json`,
    // made by `New Game Plus Builder.html` (DECIDED: both files live in
    // BepInEx\config, section 4). The in-game tab's two sliders write the same
    // file (NgTab), so there is one place each number lives.
    //
    // ★ READ WHEN THE RULES LOCK, NOT AT STARTUP. A world's rules are locked
    // when it begins - at a jump for a new plus, at the run's start for plus 0 -
    // and kept in the save's own NG+ file after that (2.6). So an edit made
    // with the game running applies from the NEXT jump or run, with no restart,
    // and never changes a world already being played.
    //
    // ★ A BAD FILE MEANS NO RULES, SAID LOUDLY. Not the defaults: a world
    // quietly built on numbers the player did not write is the silent-fallback
    // bug this project has paid for most. This mod only ever ADDS difficulty to
    // a game that works without it, so every doubt resolves to "add nothing".
    public sealed class NgRules
    {
        // 2: enemyDamage (2026-10-02). A version-1 file is read as it is, and
        // its missing enemyDamage takes the default - see Parse.
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public NgBuff enemyHealth;
        public NgBuff enemyCount;
        public NgBuff enemyDamage;

        // Where the file lives. Resolved through BepInEx, never hard-coded.
        public static string FilePath
        {
            get
            {
                return Path.Combine(Paths.ConfigPath,
                                    NewGamePlusPlugin.Guid + ".rules.json");
            }
        }

        // DECIDED defaults (2.4): the test numbers are +50% health and +30%
        // enemies; coverage starts at 25% and rises each plus to 100%. Every
        // buff starts at the first NG+ world, so the first world plays as the
        // game made it.
        //
        // DECIDED 2026-10-02: enemy health has NO cap by default (a cap of 0
        // is "none"), and enemy damage is +100% a plus on EVERY enemy - no
        // coverage, no cap - with the environment included. His words: "The
        // damage buff should affect every enemy right out the bat." Enemy
        // count keeps its cap (a room only holds so many).
        public static NgRules Defaults()
        {
            return new NgRules
            {
                enemyHealth = new NgBuff
                {
                    enabled = true, fromPlus = 1, start = 50f, perPlus = 50f, max = 0f,
                    coverageStart = 25f, coveragePerPlus = 25f
                },
                enemyCount = new NgBuff
                {
                    enabled = true, fromPlus = 1, start = 30f, perPlus = 30f, max = 300f,
                    coverageStart = 100f, coveragePerPlus = 0f
                },
                enemyDamage = new NgBuff
                {
                    enabled = true, fromPlus = 1, start = 100f, perPlus = 100f, max = 0f,
                    coverageStart = 100f, coveragePerPlus = 0f, environment = true
                }
            };
        }

        // No rule in force anywhere: what a save made before New Game Plus
        // was installed gets - it never locked any.
        public static NgRules None()
        {
            NgRules r = Defaults();
            r.enemyHealth.enabled = false;
            r.enemyCount.enabled = false;
            r.enemyDamage.enabled = false;
            return r;
        }

        public NgRules Clone()
        {
            return new NgRules
            {
                version = version,
                enemyHealth = enemyHealth.Clone(),
                enemyCount = enemyCount.Clone(),
                enemyDamage = enemyDamage.Clone()
            };
        }

        // ---- the numbers for one world ---------------------------------

        public float HealthPercent(int plus)
        {
            return enemyHealth.ValueAt(plus);
        }

        public float HealthCoverage(int plus)
        {
            // A buff with no strength covers nobody, whatever its ramp says.
            return HealthPercent(plus) > 0f ? enemyHealth.CoverageAt(plus) : 0f;
        }

        // Enemy count is a WORLD-WIDE multiplier on every room's spawn budget,
        // so it has no coverage (2.5).
        public float CountPercent(int plus)
        {
            return enemyCount.ValueAt(plus);
        }

        // Enemy damage is on EVERY enemy (DECIDED 2026-10-02), so like count
        // it has no coverage: one number for the whole world.
        public float DamagePercent(int plus)
        {
            return enemyDamage.ValueAt(plus);
        }

        public bool DamageEnvironment
        {
            get { return enemyDamage.environment; }
        }

        public bool AnyAt(int plus)
        {
            return HealthCoverage(plus) > 0f || CountPercent(plus) > 0f || DamagePercent(plus) > 0f;
        }

        // "enemies +100% health (on 50% of them), +60% more, +200% damage
        // (hazards too)", for the arrival alert, the victory screen and WORLD
        // INFO.
        public string Summary(int plus)
        {
            var parts = new List<string>();

            float h = HealthPercent(plus);
            float hc = HealthCoverage(plus);

            if (h > 0f && hc > 0f)
            {
                parts.Add(NgMath.Pct(h) + " health" +
                          (hc < 100f ? " (on " + Math.Round(hc).ToString(CultureInfo.InvariantCulture) +
                                       "% of them)" : ""));
            }

            float c = CountPercent(plus);

            if (c > 0f)
                parts.Add(NgMath.Pct(c) + " more");

            float d = DamagePercent(plus);

            if (d > 0f)
                parts.Add(NgMath.Pct(d) + " damage" + (DamageEnvironment ? " (hazards too)" : ""));

            return parts.Count == 0
                ? "no rules in force"
                : "enemies " + string.Join(", ", parts.ToArray());
        }

        // ---- JSON --------------------------------------------------------

        public JObject ToJObject()
        {
            return new JObject
            {
                ["version"] = version,
                ["enemyHealth"] = BuffToJ(enemyHealth, true, false),
                ["enemyCount"] = BuffToJ(enemyCount, false, false),
                ["enemyDamage"] = BuffToJ(enemyDamage, false, true)
            };
        }

        public string ToJson(bool indented)
        {
            return ToJObject().ToString(indented ? Formatting.Indented : Formatting.None);
        }

        private static JObject BuffToJ(NgBuff b, bool coverage, bool environment)
        {
            var o = new JObject
            {
                ["enabled"] = b.enabled,
                ["fromPlus"] = b.fromPlus,
                ["start"] = b.start,
                ["perPlus"] = b.perPlus,
                ["max"] = b.max
            };

            if (coverage)
            {
                o["coverageStart"] = b.coverageStart;
                o["coveragePerPlus"] = b.coveragePerPlus;
            }

            if (environment)
                o["environment"] = b.environment;

            return o;
        }

        // Parse the rules FILE. Unknown keys are reported, missing ones take
        // the default for that key (a five-line file is a valid file, the
        // Weapon Forge convention), and a value of the wrong kind is reported
        // and skipped. Returns null only when the text is not a JSON object at
        // all.
        public static NgRules Parse(string text, List<string> problems)
        {
            return Parse(text, problems, false);
        }

        // `locked`: the text is a world's LOCKED rules, out of a save's record
        // - not the file. ★ Then a buff the text does not name is OFF, never
        // its default: a world locked before a buff existed (a stage-1 save,
        // locked before enemy damage) keeps playing with the rules it was
        // locked with. Only the file takes a new buff's default.
        public static NgRules Parse(string text, List<string> problems, bool locked)
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

            NgRules r = locked ? None() : Defaults();

            foreach (JProperty p in o.Properties())
            {
                switch (p.Name)
                {
                    case "version":
                        int v;
                        if (TryInt(p.Value, out v))
                            r.version = v;
                        break;
                    case "enemyHealth":
                        ReadBuff(p.Value as JObject, r.enemyHealth, "enemyHealth", true, problems);
                        break;
                    case "enemyCount":
                        ReadBuff(p.Value as JObject, r.enemyCount, "enemyCount", false, problems);
                        break;
                    case "enemyDamage":
                        ReadBuff(p.Value as JObject, r.enemyDamage, "enemyDamage", false, problems);
                        break;
                    default:
                        // "$..." keys are notes the builder page may write.
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add("\"" + p.Name + "\" is not a New Game Plus rule - ignored");
                        }
                        break;
                }
            }

            if (r.version > CurrentVersion)
            {
                problems.Add("the file is version " + r.version + " and this build reads " +
                             CurrentVersion + " - it is read as far as this build understands it");
            }

            return r;
        }

        private static void ReadBuff(JObject o, NgBuff b, string name, bool coverage,
                                     List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"" + name + "\" should be an object - its defaults are used");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                bool bv;
                int iv;
                float fv;

                switch (p.Name)
                {
                    case "enabled":
                        if (TryBool(p.Value, out bv)) b.enabled = bv;
                        else Bad(problems, name, p);
                        break;
                    case "fromPlus":
                        if (TryInt(p.Value, out iv)) b.fromPlus = Math.Max(0, iv);
                        else Bad(problems, name, p);
                        break;
                    case "start":
                        if (TryFloat(p.Value, out fv)) b.start = fv;
                        else Bad(problems, name, p);
                        break;
                    case "perPlus":
                        if (TryFloat(p.Value, out fv)) b.perPlus = fv;
                        else Bad(problems, name, p);
                        break;
                    case "max":
                        if (TryFloat(p.Value, out fv)) b.max = fv;
                        else Bad(problems, name, p);
                        break;
                    case "environment":
                        if (name != "enemyDamage")
                        {
                            problems.Add(name + ".environment - only enemyDamage has an " +
                                         "environment switch, so this is ignored");
                            break;
                        }
                        if (TryBool(p.Value, out bv)) b.environment = bv;
                        else Bad(problems, name, p);
                        break;
                    case "coverageStart":
                    case "coveragePerPlus":
                        if (!coverage)
                        {
                            problems.Add(name + "." + p.Name + " - " +
                                         (name == "enemyDamage"
                                             ? "enemy damage is on every enemy"
                                             : "enemy count is world-wide") +
                                         " and has no coverage, so this is ignored");
                            break;
                        }
                        if (!TryFloat(p.Value, out fv))
                        {
                            Bad(problems, name, p);
                            break;
                        }
                        if (p.Name == "coverageStart") b.coverageStart = fv;
                        else b.coveragePerPlus = fv;
                        break;
                    default:
                        if (!p.Name.StartsWith("$", StringComparison.Ordinal) &&
                            !p.Name.StartsWith("_", StringComparison.Ordinal))
                        {
                            problems.Add(name + "." + p.Name + " is not a setting - ignored");
                        }
                        break;
                }
            }
        }

        private static void Bad(List<string> problems, string name, JProperty p)
        {
            problems.Add(name + "." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                         " is the wrong kind of value - its default is used");
        }

        private static bool TryBool(JToken t, out bool v)
        {
            v = false;

            if (t == null)
                return false;

            if (t.Type == JTokenType.Boolean)
            {
                v = (bool)t;
                return true;
            }

            return false;
        }

        private static bool TryInt(JToken t, out int v)
        {
            v = 0;

            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                return false;

            double d = (double)t;

            if (double.IsNaN(d) || double.IsInfinity(d) || d > int.MaxValue || d < int.MinValue)
                return false;

            v = (int)Math.Round(d);
            return true;
        }

        private static bool TryFloat(JToken t, out float v)
        {
            v = 0f;

            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                return false;

            double d = (double)t;

            if (double.IsNaN(d) || double.IsInfinity(d))
                return false;

            v = (float)d;
            return true;
        }

        // ---- the file ----------------------------------------------------

        // The rules as the file says them NOW. A missing file is written with
        // the defaults first (so there is something to open and edit); a file
        // that cannot be read means NO rules this time, and the log says why.
        public static NgRules LoadFile()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                {
                    NgRules d = Defaults();
                    WriteFile(d.ToJObject());
                    NewGamePlusPlugin.Log.LogInfo(
                        "no rules file yet - wrote the defaults to BepInEx\\config\\" +
                        Path.GetFileName(path) + " (enemy health " + NgMath.Pct(d.enemyHealth.start) +
                        " a plus, enemy count " + NgMath.Pct(d.enemyCount.start) +
                        " a plus, enemy damage " + NgMath.Pct(d.enemyDamage.start) +
                        " a plus, all from NEW GAME PLUS 1). Edit it with New Game " +
                        "Plus Builder.html, or the sliders in the GAME MODE FORGE tab.");
                    return d;
                }

                string text = File.ReadAllText(path);
                var problems = new List<string>();
                NgRules r = Parse(text, problems);

                foreach (string p in problems)
                    NewGamePlusPlugin.Log.LogWarning("rules file: " + p);

                // A file written before enemy damage existed. Missing keys take
                // their defaults (the house convention), so it is ON - say so,
                // once a session, because the file itself does not show it.
                if (r != null && !_saidNoDamage && text.IndexOf("\"enemyDamage\"", StringComparison.Ordinal) < 0)
                {
                    _saidNoDamage = true;
                    NewGamePlusPlugin.Log.LogInfo(
                        "rules file: it has no enemyDamage section (it was written before enemy " +
                        "damage existed), so enemy damage takes its default - " +
                        NgMath.Pct(r.enemyDamage.start) + " a plus from " +
                        NgMath.WorldName(r.enemyDamage.fromPlus) + ", hazards too. Change it with " +
                        "the Enemy damage slider in the GAME MODE FORGE tab or New Game Plus Builder.html.");
                }

                if (r == null)
                {
                    NewGamePlusPlugin.Log.LogError(
                        "COULD NOT READ the rules file (" + Path.GetFileName(path) + ") - this " +
                        "world gets NO New Game Plus rules. Fix or delete the file (a deleted " +
                        "file is rewritten with the defaults) and the next world picks it up.");
                    return None();
                }

                return r;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT READ the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - this world gets NO New Game Plus rules.");
                return None();
            }
        }

        // Write one headline number (the in-game sliders): both `start` and
        // `perPlus` of that buff, so "+50% a plus" means +50%, +100%, +150%,
        // and `enabled` follows (0 is off). Every other key in the file -
        // including ones the builder page wrote that this build does not know -
        // is kept exactly as it was.
        public static bool SetHeadline(string buff, float percent)
        {
            try
            {
                JObject o;

                // A file that is not JSON is refused, and the slider goes back.
                if (!ReadForEdit(out o))
                    return false;

                JObject b = BuffObject(o, buff);

                b["start"] = percent;
                b["perPlus"] = percent;
                b["enabled"] = percent > 0f;

                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: " + buff + " set to " + NgMath.Pct(percent) +
                    " a plus from the settings tab - it applies from the next world " +
                    "(this one keeps the rules it started with).");

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the slider has NOT been saved.");
                return false;
            }
        }

        // The tab's "Enemy damage: hazards too" row: enemyDamage.environment.
        // Every other key in the file is kept, as for the sliders.
        public static bool SetEnvironment(bool on)
        {
            try
            {
                JObject o;

                if (!ReadForEdit(out o))
                    return false;

                BuffObject(o, "enemyDamage")["environment"] = on;
                WriteFile(o);

                NewGamePlusPlugin.Log.LogInfo(
                    "rules file: enemy damage on hazards " + (on ? "ON" : "OFF") +
                    " from the settings tab - it applies from the next world.");

                return true;
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the switch has NOT been saved.");
                return false;
            }
        }

        // The file as JSON to edit, or the defaults when there is none. A file
        // that is not JSON is not ours to overwrite quietly: refuse and say so.
        private static bool ReadForEdit(out JObject o)
        {
            string path = FilePath;
            o = null;

            if (!File.Exists(path))
            {
                o = Defaults().ToJObject();
                return true;
            }

            try
            {
                o = JObject.Parse(File.ReadAllText(path));
                return true;
            }
            catch (Exception)
            {
                NewGamePlusPlugin.Log.LogError(
                    "the rules file is not valid JSON, so the settings tab did NOT write it. " +
                    "Fix it (or delete it to get the defaults back) and try again.");
                return false;
            }
        }

        // One buff's object in the file, made from the defaults when missing.
        private static JObject BuffObject(JObject o, string buff)
        {
            JObject b = o[buff] as JObject;

            if (b != null)
                return b;

            NgRules d = Defaults();

            switch (buff)
            {
                case "enemyHealth": b = BuffToJ(d.enemyHealth, true, false); break;
                case "enemyDamage": b = BuffToJ(d.enemyDamage, false, true); break;
                default: b = BuffToJ(d.enemyCount, false, false); break;
            }

            o[buff] = b;
            return b;
        }

        private static bool _saidNoDamage;

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
