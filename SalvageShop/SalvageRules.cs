using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameModeForge.SalvageShop
{
    // One stat's settings (3.4, rounds 2-3): how much one step gives, its own
    // cost ladder (any part left out takes the shared ladder's), and whether it
    // is in the pool at all.
    internal sealed class SalvageStatRule
    {
        public bool enabled = true;
        public float amount;      // damage %, rate %, +projectiles, levels, capacity, regen/s ...
        public float amount2;     // explosion: radius a step; spark: chain a step

        // A stat's own ladder, part by part (null = the shared ladder's).
        public int? exStart, exPerStep, bondAfter, bondEvery, bondAmount;

        public SalvageLadder LadderFrom(SalvageLadder shared)
        {
            return new SalvageLadder
            {
                exStart = exStart ?? shared.exStart,
                exPerStep = exPerStep ?? shared.exPerStep,
                bondAfter = bondAfter ?? shared.bondAfter,
                bondEvery = bondEvery ?? shared.bondEvery,
                bondAmount = bondAmount ?? shared.bondAmount
            };
        }

        public bool HasOwnLadder
        {
            get
            {
                return exStart.HasValue || exPerStep.HasValue || bondAfter.HasValue ||
                       bondEvery.HasValue || bondAmount.HasValue;
            }
        }

        public SalvageStatRule Clone()
        {
            return (SalvageStatRule)MemberwiseClone();
        }
    }

    // THE RULES FILE - `BepInEx\config\com.sugarheady.gamemodeforge.salvageshop.rules.json`,
    // made by `Salvage Builder.html` (DECIDED, round 2: "a rules file in
    // BepInEx\config, made by a Salvage Builder.html page, as New Game Plus
    // does it, holding every number"). The tab's rows write the same file.
    //
    // ★ EVERY NUMBER IS A SETTING (3.5): the scrap payout, the refund %, the
    // shared ladder, and every stat's own amount and ladder.
    //
    // ★ READ LIVE, NOT AT STARTUP. The file is re-read when it changes on disk
    // (checked at most once a second), so an edit applies the next time a gun
    // is built or the SALVAGE screen opens, with no restart. An upgrade is
    // stored as a COUNT of steps, so a changed amount changes what every
    // upgrade already bought is worth - which is what "adjustable" means here.
    //
    // ★ A BAD FILE KEEPS THE LAST GOOD RULES, SAID LOUDLY. Unlike New Game Plus
    // (which adds nothing when in doubt), salvage has upgrades already bought
    // that must keep working, so a broken edit falls back to the last rules
    // that read - the defaults on a session's first read - and the SALVAGE
    // screen says the file is broken until it is fixed.
    internal sealed class SalvageRules
    {
        public const int CurrentVersion = 1;

        // Every stat id, in the order the picker lists them.
        public static readonly string[] WeaponStats =
        {
            "damage", "explosion", "spark", "fireRate", "range", "speed", "spread", "projectiles", "cost"
        };

        public static readonly string[] Resources =
        {
            "health", "fuel", "gel", "caps", "stamina", "tech", "electron"
        };

        public int version = CurrentVersion;

        // scrap (3.3, 3.6, round 2)
        public string common = "Ex";
        public string uncommon = "Bond";
        public int exPerScrap = 2;
        public int bondPerScrap = 1;
        public float bondChance = 50f;
        public float refundPercent = 100f;

        // the shared ladder (3.5)
        public SalvageLadder ladder = SalvageLadder.Default;

        // the add-ons (16.4, 18.1)
        public float explosionCost;
        public string explosionResource = "caps";
        public int sparkCost;
        public string sparkResource = "electron";
        public bool explodeAtRange;

        public readonly Dictionary<string, SalvageStatRule> stats =
            new Dictionary<string, SalvageStatRule>();

        public SalvageStatRule Stat(string id)
        {
            SalvageStatRule r;
            return id != null && stats.TryGetValue(id, out r) ? r : null;
        }

        public bool Enabled(string id)
        {
            SalvageStatRule r = Stat(id);
            return r != null && r.enabled;
        }

        public float Amount(string id)
        {
            SalvageStatRule r = Stat(id);
            return r != null ? r.amount : 0f;
        }

        public SalvageLadder LadderFor(string id)
        {
            SalvageStatRule r = Stat(id);
            return r != null ? r.LadderFrom(ladder) : ladder;
        }

        // The price of the item's `step`-th step, paid in the chosen stat's
        // own ladder (3.5: "a step's price = the chosen stat's own ladder, read
        // at that ITEM's next step number").
        public void PriceFor(string id, int step, out int ex, out int bond)
        {
            SalvageLadder l = LadderFor(id);
            ex = SalvageMath.ExAt(l, step);
            bond = SalvageMath.BondAt(l, step);
        }

        // ---- defaults (every DECIDED number, with its section) -------------

        public static SalvageRules Defaults()
        {
            var r = new SalvageRules();

            // Weapons and weapon gadgets (3.4, 13.2, 14.3, 14.4, 16.4).
            r.stats["damage"] = new SalvageStatRule { amount = 10f };
            r.stats["explosion"] = new SalvageStatRule { amount = 1f, amount2 = 0.5f };
            r.stats["spark"] = new SalvageStatRule { amount = 1f, amount2 = 1f };
            r.stats["fireRate"] = new SalvageStatRule { amount = 10f };
            r.stats["range"] = new SalvageStatRule { amount = 10f };
            r.stats["speed"] = new SalvageStatRule { amount = 10f };
            r.stats["spread"] = new SalvageStatRule { amount = 10f };
            r.stats["projectiles"] = new SalvageStatRule { amount = 1f };
            r.stats["cost"] = new SalvageStatRule { amount = 10f };

            // Levels (3.4): shields, the PowerCore, the scaling augmentations
            // and the drone gadgets.
            r.stats["level"] = new SalvageStatRule { amount = 1f };

            // Ship resources (13.4, 16.5): the game's own step a level, and
            // Fuel / Electron regen, which the game has no module for.
            float[] cap = { 1f, 1f, 1f, 1f, 1f, 1f, 2f };
            float[] regen = { 0.05f, 0.05f, 0.05f, 0.05f, 2f, 0.01f, 0.1f };

            for (int i = 0; i < Resources.Length; i++)
            {
                r.stats["cap." + Resources[i]] = new SalvageStatRule { amount = cap[i] };
                r.stats["regen." + Resources[i]] = new SalvageStatRule { amount = regen[i] };
            }

            return r;
        }

        // ---- where the file lives -----------------------------------------

        public static string FilePath
        {
            get
            {
                return Path.Combine(Paths.ConfigPath, SalvageShopPlugin.Guid + ".rules.json");
            }
        }

        // ---- the live rules -------------------------------------------------

        private static SalvageRules _current;
        private static DateTime _stamp = DateTime.MinValue;
        private static float _lastCheck = -10f;
        private static bool _broken;

        // True while the file on disk could not be read (the last good rules
        // are in force). The SALVAGE screen says so.
        public static bool Broken
        {
            get { return _broken; }
        }

        public static SalvageRules Current
        {
            get
            {
                float now = UnityEngine.Time.realtimeSinceStartup;

                if (_current == null || now - _lastCheck > 1f || now < _lastCheck)
                {
                    _lastCheck = now;
                    Refresh();
                }

                return _current ?? (_current = Defaults());
            }
        }

        // Force the next read to look at the file (the tab rows wrote it).
        public static void Touch()
        {
            _lastCheck = -10f;
            _stamp = DateTime.MinValue;
        }

        private static void Refresh()
        {
            string path = FilePath;

            try
            {
                if (!File.Exists(path))
                {
                    SalvageRules d = Defaults();
                    WriteFile(d.ToJObject());
                    _stamp = File.GetLastWriteTimeUtc(path);
                    _current = d;
                    _broken = false;

                    SalvageShopPlugin.Log.LogInfo(
                        "no rules file yet - wrote the defaults to BepInEx\\config\\" +
                        Path.GetFileName(path) + " (a scrap pays 2 Ex and a 50% chance of 1 Bond; a " +
                        "step costs n Ex, with Bond after step 5). Edit it with Salvage Builder.html, " +
                        "or the rows in the GAME MODE FORGE tab.");
                    return;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(path);

                if (_current != null && stamp == _stamp)
                    return;

                _stamp = stamp;

                var problems = new List<string>();
                SalvageRules r = Parse(File.ReadAllText(path), problems);

                foreach (string p in problems)
                    SalvageShopPlugin.Log.LogWarning("rules file: " + p);

                if (r == null)
                {
                    _broken = true;
                    SalvageShopPlugin.Log.LogError(
                        "COULD NOT READ the rules file (" + Path.GetFileName(path) + ") - " +
                        (_current != null ? "the last rules that read are kept" : "the defaults are used") +
                        " until it is fixed. Fix it, or delete it to have the defaults written back.");

                    if (_current == null)
                        _current = Defaults();

                    return;
                }

                _broken = false;

                bool first = _current == null;
                _current = r;

                SalvageShopPlugin.Log.LogInfo(
                    "rules file " + (first ? "read" : "re-read (it changed)") + ": a scrap pays " +
                    r.exPerScrap + " " + r.common + " and a " + SalvageMath.Num(r.bondChance) + "% chance of " +
                    r.bondPerScrap + " " + r.uncommon + "; an upgraded item refunds " +
                    SalvageMath.Num(r.refundPercent) + "% of what it cost.");
            }
            catch (Exception ex)
            {
                _broken = true;

                SalvageShopPlugin.Log.LogError(
                    "COULD NOT READ the rules file (" + ex.GetType().Name + ": " + ex.Message + ") - " +
                    (_current != null ? "the last rules that read are kept." : "the defaults are used."));

                if (_current == null)
                    _current = Defaults();
            }
        }

        // ---- JSON -------------------------------------------------------------

        public JObject ToJObject()
        {
            var stat = new JObject();

            foreach (KeyValuePair<string, SalvageStatRule> kv in stats)
            {
                var o = new JObject { ["enabled"] = kv.Value.enabled };

                if (kv.Key == "explosion")
                {
                    o["damage"] = kv.Value.amount;
                    o["radius"] = kv.Value.amount2;
                }
                else if (kv.Key == "spark")
                {
                    o["damage"] = kv.Value.amount;
                    o["chain"] = kv.Value.amount2;
                }
                else
                {
                    o["amount"] = kv.Value.amount;
                }

                if (kv.Value.HasOwnLadder)
                {
                    var l = new JObject();
                    if (kv.Value.exStart.HasValue) l["exStart"] = kv.Value.exStart.Value;
                    if (kv.Value.exPerStep.HasValue) l["exPerStep"] = kv.Value.exPerStep.Value;
                    if (kv.Value.bondAfter.HasValue) l["bondAfter"] = kv.Value.bondAfter.Value;
                    if (kv.Value.bondEvery.HasValue) l["bondEvery"] = kv.Value.bondEvery.Value;
                    if (kv.Value.bondAmount.HasValue) l["bondAmount"] = kv.Value.bondAmount.Value;
                    o["ladder"] = l;
                }

                stat[kv.Key] = o;
            }

            return new JObject
            {
                ["version"] = version,
                ["scrap"] = new JObject
                {
                    ["common"] = common,
                    ["uncommon"] = uncommon,
                    ["exPerScrap"] = exPerScrap,
                    ["bondPerScrap"] = bondPerScrap,
                    ["bondChance"] = bondChance,
                    ["refundPercent"] = refundPercent
                },
                ["ladder"] = new JObject
                {
                    ["exStart"] = ladder.exStart,
                    ["exPerStep"] = ladder.exPerStep,
                    ["bondAfter"] = ladder.bondAfter,
                    ["bondEvery"] = ladder.bondEvery,
                    ["bondAmount"] = ladder.bondAmount
                },
                ["addOns"] = new JObject
                {
                    ["explosionCost"] = explosionCost,
                    ["explosionResource"] = explosionResource,
                    ["sparkCost"] = sparkCost,
                    ["sparkResource"] = sparkResource,
                    ["explodeAtRange"] = explodeAtRange
                },
                ["stats"] = stat
            };
        }

        // Unknown keys are reported, missing ones take the default for that key
        // (a five-line file is a valid file, the house convention), and a value
        // of the wrong kind is reported and skipped. Returns null only when the
        // text is not a JSON object at all.
        public static SalvageRules Parse(string text, List<string> problems)
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

            SalvageRules r = Defaults();

            foreach (JProperty p in o.Properties())
            {
                switch (p.Name)
                {
                    case "version":
                        int v;
                        if (TryInt(p.Value, out v)) r.version = v;
                        break;
                    case "scrap":
                        ReadScrap(p.Value as JObject, r, problems);
                        break;
                    case "ladder":
                        r.ladder = ReadLadder(p.Value as JObject, r.ladder, "ladder", problems);
                        break;
                    case "addOns":
                        ReadAddOns(p.Value as JObject, r, problems);
                        break;
                    case "stats":
                        ReadStats(p.Value as JObject, r, problems);
                        break;
                    default:
                        if (!Note(p.Name))
                            problems.Add("\"" + p.Name + "\" is not a Salvage Shop setting - ignored");
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

        // "$..." and "_..." keys are notes the builder page may write.
        private static bool Note(string key)
        {
            return key.StartsWith("$", StringComparison.Ordinal) ||
                   key.StartsWith("_", StringComparison.Ordinal);
        }

        private static void ReadScrap(JObject o, SalvageRules r, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"scrap\" should be an object - its defaults are used");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                int iv;
                float fv;

                switch (p.Name)
                {
                    case "common":
                    case "uncommon":
                        string s = p.Value.Type == JTokenType.String ? ((string)p.Value ?? "").Trim() : null;

                        if (string.IsNullOrEmpty(s))
                        {
                            Bad(problems, "scrap", p);
                            break;
                        }

                        if (s.Equals("Face", StringComparison.OrdinalIgnoreCase))
                        {
                            // 3.3: "Never use Face." He wants it left for the game.
                            problems.Add("scrap." + p.Name + " = Face - Face is left for the game " +
                                         "(the concept's rule), so the default is kept");
                            break;
                        }

                        if (p.Name == "common") r.common = s;
                        else r.uncommon = s;
                        break;
                    case "exPerScrap":
                        if (TryInt(p.Value, out iv)) r.exPerScrap = Math.Max(0, iv);
                        else Bad(problems, "scrap", p);
                        break;
                    case "bondPerScrap":
                        if (TryInt(p.Value, out iv)) r.bondPerScrap = Math.Max(0, iv);
                        else Bad(problems, "scrap", p);
                        break;
                    case "bondChance":
                        if (TryFloat(p.Value, out fv)) r.bondChance = Clamp(fv, 0f, 100f);
                        else Bad(problems, "scrap", p);
                        break;
                    case "refundPercent":
                        if (TryFloat(p.Value, out fv)) r.refundPercent = Clamp(fv, 0f, 100f);
                        else Bad(problems, "scrap", p);
                        break;
                    default:
                        if (!Note(p.Name))
                            problems.Add("scrap." + p.Name + " is not a setting - ignored");
                        break;
                }
            }

            if (string.Equals(r.common, r.uncommon, StringComparison.OrdinalIgnoreCase))
                problems.Add("scrap.common and scrap.uncommon are the same ingredient (" + r.common +
                             ") - every scrap and every price is then paid in one");
        }

        private static SalvageLadder ReadLadder(JObject o, SalvageLadder l, string where,
                                                List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"" + where + "\" should be an object - its defaults are used");
                return l;
            }

            foreach (JProperty p in o.Properties())
            {
                int iv;

                if (!TryInt(p.Value, out iv))
                {
                    if (!Note(p.Name))
                        Bad(problems, where, p);
                    continue;
                }

                switch (p.Name)
                {
                    case "exStart": l.exStart = Math.Max(0, iv); break;
                    case "exPerStep": l.exPerStep = Math.Max(0, iv); break;
                    case "bondAfter": l.bondAfter = Math.Max(0, iv); break;
                    case "bondEvery": l.bondEvery = Math.Max(1, iv); break;
                    case "bondAmount": l.bondAmount = Math.Max(0, iv); break;
                    default:
                        problems.Add(where + "." + p.Name + " is not a setting - ignored");
                        break;
                }
            }

            return l;
        }

        private static void ReadAddOns(JObject o, SalvageRules r, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"addOns\" should be an object - its defaults are used");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                int iv;
                float fv;
                bool bv;

                switch (p.Name)
                {
                    case "explosionCost":
                        if (TryFloat(p.Value, out fv)) r.explosionCost = Math.Max(0f, fv);
                        else Bad(problems, "addOns", p);
                        break;
                    case "sparkCost":
                        if (TryInt(p.Value, out iv)) r.sparkCost = Math.Max(0, iv);
                        else Bad(problems, "addOns", p);
                        break;
                    case "explosionResource":
                    case "sparkResource":
                        string s = p.Value.Type == JTokenType.String ? ((string)p.Value ?? "").Trim() : null;

                        if (string.IsNullOrEmpty(s) || SalvageCatalog.ResourceId(s) == null)
                        {
                            problems.Add("addOns." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                                         " is not one of health, fuel, gel, caps, stamina, tech, electron - " +
                                         "its default is used");
                            break;
                        }

                        if (p.Name == "explosionResource") r.explosionResource = s.ToLowerInvariant();
                        else r.sparkResource = s.ToLowerInvariant();
                        break;
                    case "explodeAtRange":
                        if (TryBool(p.Value, out bv)) r.explodeAtRange = bv;
                        else Bad(problems, "addOns", p);
                        break;
                    default:
                        if (!Note(p.Name))
                            problems.Add("addOns." + p.Name + " is not a setting - ignored");
                        break;
                }
            }
        }

        private static void ReadStats(JObject o, SalvageRules r, List<string> problems)
        {
            if (o == null)
            {
                problems.Add("\"stats\" should be an object - its defaults are used");
                return;
            }

            foreach (JProperty p in o.Properties())
            {
                if (Note(p.Name))
                    continue;

                SalvageStatRule s = r.Stat(p.Name);

                if (s == null)
                {
                    problems.Add("stats." + p.Name + " is not a stat - ignored (the stats are " +
                                 string.Join(", ", new List<string>(r.stats.Keys).ToArray()) + ")");
                    continue;
                }

                float fv;
                bool bv;

                // A bare number is the stat's amount.
                if (TryFloat(p.Value, out fv))
                {
                    s.amount = Math.Max(0f, fv);
                    continue;
                }

                var so = p.Value as JObject;

                if (so == null)
                {
                    Bad(problems, "stats", p);
                    continue;
                }

                string where = "stats." + p.Name;

                foreach (JProperty q in so.Properties())
                {
                    switch (q.Name)
                    {
                        case "enabled":
                            if (TryBool(q.Value, out bv)) s.enabled = bv;
                            else Bad(problems, where, q);
                            break;
                        case "amount":
                        case "damage":
                            if ((q.Name == "damage") != (p.Name == "explosion" || p.Name == "spark"))
                            {
                                problems.Add(where + "." + q.Name + " - " +
                                             (q.Name == "damage"
                                                 ? "only explosion and spark take \"damage\"; this stat takes \"amount\""
                                                 : "explosion and spark take \"damage\", not \"amount\"") +
                                             " - ignored");
                                break;
                            }
                            if (TryFloat(q.Value, out fv)) s.amount = Math.Max(0f, fv);
                            else Bad(problems, where, q);
                            break;
                        case "radius":
                        case "chain":
                            if ((q.Name == "radius" && p.Name != "explosion") ||
                                (q.Name == "chain" && p.Name != "spark"))
                            {
                                problems.Add(where + "." + q.Name + " belongs to " +
                                             (q.Name == "radius" ? "explosion" : "spark") + " - ignored");
                                break;
                            }
                            if (TryFloat(q.Value, out fv)) s.amount2 = Math.Max(0f, fv);
                            else Bad(problems, where, q);
                            break;
                        case "ladder":
                            var lo = q.Value as JObject;

                            if (lo == null)
                            {
                                Bad(problems, where, q);
                                break;
                            }

                            foreach (JProperty lp in lo.Properties())
                            {
                                int iv;

                                if (Note(lp.Name))
                                    continue;

                                if (!TryInt(lp.Value, out iv))
                                {
                                    Bad(problems, where + ".ladder", lp);
                                    continue;
                                }

                                switch (lp.Name)
                                {
                                    case "exStart": s.exStart = Math.Max(0, iv); break;
                                    case "exPerStep": s.exPerStep = Math.Max(0, iv); break;
                                    case "bondAfter": s.bondAfter = Math.Max(0, iv); break;
                                    case "bondEvery": s.bondEvery = Math.Max(1, iv); break;
                                    case "bondAmount": s.bondAmount = Math.Max(0, iv); break;
                                    default:
                                        problems.Add(where + ".ladder." + lp.Name + " is not a setting - ignored");
                                        break;
                                }
                            }
                            break;
                        default:
                            if (!Note(q.Name))
                                problems.Add(where + "." + q.Name + " is not a setting - ignored");
                            break;
                    }
                }
            }
        }

        private static void Bad(List<string> problems, string where, JProperty p)
        {
            problems.Add(where + "." + p.Name + " = " + p.Value.ToString(Formatting.None) +
                         " is the wrong kind of value - its default is used");
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static bool TryBool(JToken t, out bool v)
        {
            v = false;

            if (t == null || t.Type != JTokenType.Boolean)
                return false;

            v = (bool)t;
            return true;
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

        // ---- the tab rows' writes ------------------------------------------

        // One number under "scrap" (the tab's sliders). Every other key in the
        // file - including ones the builder page wrote that this build does
        // not know - is kept exactly as it was.
        public static bool SetScrap(string key, double value)
        {
            try
            {
                JObject o;

                if (!ReadForEdit(out o))
                    return false;

                JObject scrap = o["scrap"] as JObject;

                if (scrap == null)
                {
                    scrap = (JObject)Defaults().ToJObject()["scrap"];
                    o["scrap"] = scrap;
                }

                if (key == "exPerScrap" || key == "bondPerScrap")
                    scrap[key] = (int)Math.Round(value);
                else
                    scrap[key] = value;

                WriteFile(o);
                Touch();

                SalvageShopPlugin.Log.LogInfo(
                    "rules file: scrap." + key + " set to " +
                    value.ToString(CultureInfo.InvariantCulture) + " from the settings tab.");

                return true;
            }
            catch (Exception ex)
            {
                SalvageShopPlugin.Log.LogError(
                    "COULD NOT WRITE the rules file (" + ex.GetType().Name + ": " + ex.Message +
                    ") - the setting has NOT been saved.");
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
                SalvageShopPlugin.Log.LogError(
                    "the rules file is not valid JSON, so the settings tab did NOT write it. " +
                    "Fix it (or delete it to get the defaults back) and try again.");
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
