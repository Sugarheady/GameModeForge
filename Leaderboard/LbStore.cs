using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameModeForge.Leaderboard
{
    // THE BOARD'S FILE - `BepInEx\config\com.sugarheady.gamemodeforge.leaderboard.runs.json`
    // (CONCEPT.md 5.1: where every Forge file lives, section 4; resolved through
    // BepInEx's Paths.ConfigPath, never a hard-coded path).
    //
    // ★ IT IS OUTSIDE THE SAVE FOLDER ON PURPOSE. Every end of a run deletes the
    // whole save folder (GameSaver.DeleteSave) - death, Restart, END RUN - so a
    // board kept there would be deleted by the very thing it records.
    //
    // ★ NOTHING HERE EVER LOSES A ROW:
    //   - written to a temp file and swapped in, so a crash mid-write leaves the
    //     old board (or, when the old one was already gone, the temp is taken
    //     back at the next read);
    //   - a file that cannot be read - not JSON, a row that will not read, or one
    //     made by a newer Leaderboard - is RENAMED ASIDE (`.bad-<when>`), said in
    //     the log, never deleted, and a new board begins with the rows that did
    //     read;
    //   - the board screen only READS; only a finished run writes.
    //
    // The path-free halves (ToJson / FromJson, and the file functions taking a
    // path) are what lbstoreprobe.py runs against the BUILT DLL.
    public static class LbStore
    {
        public const int Version = 1;

        public const string FileName = "com.sugarheady.gamemodeforge.leaderboard.runs.json";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // In game only (BepInEx's Paths is set up by the chainloader).
        public static string FilePath
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, FileName); }
        }

        // ---- the format ----------------------------------------------------------

        public static string ToJson(List<LbEntry> runs)
        {
            var arr = new JArray();

            if (runs != null)
                foreach (LbEntry e in runs)
                    if (e != null)
                        arr.Add(ToJ(e));

            var o = new JObject();
            o["version"] = Version;
            o["note"] = "Leaderboard's record of every finished run (a death, or END RUN on New Game Plus's " +
                        "victory screen). One row per run, in the order they ended; the board ranks them by " +
                        "the furthest world, then the score. Delete this file to clear the board.";
            o["runs"] = arr;

            return o.ToString(Formatting.Indented);
        }

        private static JObject ToJ(LbEntry e)
        {
            var j = new JObject();
            j["ended"] = e.ended ?? "";
            j["coop"] = e.coop;
            j["world"] = e.world;
            j["ahead"] = e.ahead;
            j["score"] = e.score;
            j["time"] = Math.Round(e.time, 1);
            j["kills"] = e.kills;
            j["stations"] = e.stations;
            j["queens"] = e.queens;
            j["how"] = e.how ?? LbMath.Died;
            j["cleared"] = e.cleared;
            j["loadout"] = e.loadout ?? "";
            j["mods"] = new JArray(e.mods != null ? e.mods.ToArray() : new string[0]);
            j["death"] = e.death;
            return j;
        }

        // The rows, or null with `problem` said when the text is not a board
        // this build can keep. Empty text is an empty board. `skipped` counts
        // rows that did not read (the caller sets the file aside for those
        // too, so they are kept).
        public static List<LbEntry> FromJson(string text, out int skipped, out string problem)
        {
            skipped = 0;
            problem = null;

            var list = new List<LbEntry>();

            if (string.IsNullOrWhiteSpace(text))
                return list;

            JObject o;

            try
            {
                o = JObject.Parse(text);
            }
            catch (Exception ex)
            {
                problem = "not JSON (" + ex.Message + ")";
                return null;
            }

            JToken v = o["version"];

            if (v != null && v.Type == JTokenType.Integer && v.Value<int>() > Version)
            {
                problem = "made by a newer Leaderboard (file version " + v.Value<int>().ToString(Inv) +
                          ", this one reads " + Version.ToString(Inv) + ")";
                return null;
            }

            var runs = o["runs"] as JArray;

            if (runs == null)
            {
                problem = "it has no \"runs\" list";
                return null;
            }

            foreach (JToken t in runs)
            {
                LbEntry e = FromJ(t as JObject);

                if (e == null)
                    skipped++;
                else
                    list.Add(e);
            }

            return list;
        }

        // A missing key is its default; a key of the wrong type makes the row
        // unreadable (counted, never guessed).
        private static LbEntry FromJ(JObject j)
        {
            if (j == null)
                return null;

            try
            {
                var e = new LbEntry();
                e.ended = Str(j, "ended", "");
                e.coop = Bool(j, "coop");
                e.world = Math.Max(0, Int(j, "world"));
                e.ahead = Math.Max(0, Math.Min(1, Int(j, "ahead")));
                e.score = Int(j, "score");
                e.time = Math.Max(0d, Num(j, "time"));
                e.kills = Math.Max(0, Int(j, "kills"));
                e.stations = Math.Max(0, Int(j, "stations"));
                e.queens = Math.Max(0, Int(j, "queens"));
                e.how = Str(j, "how", LbMath.Died) == LbMath.EndRun ? LbMath.EndRun : LbMath.Died;
                e.cleared = Bool(j, "cleared");
                e.loadout = Str(j, "loadout", "");
                e.death = Math.Max(0, Int(j, "death"));

                JToken mods = j["mods"];

                if (mods != null && mods.Type != JTokenType.Null)
                {
                    if (mods.Type != JTokenType.Array)
                        return null;

                    foreach (JToken m in mods)
                    {
                        if (m.Type != JTokenType.String)
                            return null;

                        e.mods.Add(m.Value<string>());
                    }
                }

                return e;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static JToken Key(JObject j, string k, params JTokenType[] types)
        {
            JToken t = j[k];

            if (t == null || t.Type == JTokenType.Null)
                return null;

            foreach (JTokenType ok in types)
                if (t.Type == ok)
                    return t;

            throw new FormatException(k + " is a " + t.Type);
        }

        private static int Int(JObject j, string k)
        {
            JToken t = Key(j, k, JTokenType.Integer);
            return t == null ? 0 : t.Value<int>();
        }

        private static double Num(JObject j, string k)
        {
            JToken t = Key(j, k, JTokenType.Integer, JTokenType.Float);
            return t == null ? 0d : t.Value<double>();
        }

        private static bool Bool(JObject j, string k)
        {
            JToken t = Key(j, k, JTokenType.Boolean);
            return t != null && t.Value<bool>();
        }

        private static string Str(JObject j, string k, string fallback)
        {
            JToken t = Key(j, k, JTokenType.String);
            return t == null ? fallback : t.Value<string>();
        }

        // ---- the file ---------------------------------------------------------------

        // Never throws. A missing file is an empty board; null + `problem` is a
        // file that is there and cannot be kept as it is.
        public static List<LbEntry> ReadFile(string path, out int skipped, out string problem)
        {
            skipped = 0;
            problem = null;

            try
            {
                // ★ A crash between writing the temp file and swapping it in, with
                // no board left behind: the temp IS the newest whole board.
                string tmp = path + ".tmp";

                if (!File.Exists(path) && File.Exists(tmp))
                    File.Move(tmp, path);

                if (!File.Exists(path))
                    return new List<LbEntry>();

                return FromJson(File.ReadAllText(path, Encoding.UTF8), out skipped, out problem);
            }
            catch (Exception ex)
            {
                problem = "it could not be opened (" + ex.GetType().Name + ": " + ex.Message + ")";
                return null;
            }
        }

        // ★ TEMP FILE, THEN SWAPPED IN. Throws on failure (the caller says so).
        public static void WriteFile(string path, List<LbEntry> runs)
        {
            string dir = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJson(runs), new UTF8Encoding(false));

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }

            try
            {
                File.Replace(tmp, path, null);
            }
            catch (Exception)
            {
                // A file system without an atomic replace: copy over, then drop
                // the temp. The temp outlives a crash here, and ReadFile keeps it.
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }

        // ★ ONE FINISHED RUN, ADDED. Returns the board as it now stands (null when
        // nothing could be written); `note` says anything the log must carry - a
        // file set aside. Never throws.
        public static List<LbEntry> Add(string path, LbEntry entry, out string note)
        {
            note = null;

            try
            {
                int skipped;
                string problem;
                List<LbEntry> runs = ReadFile(path, out skipped, out problem);

                if (runs == null || skipped > 0)
                {
                    string aside = SetAside(path);

                    note = "the board's file " +
                           (runs == null ? "could not be read (" + problem + ")" :
                               "had " + skipped.ToString(Inv) + " row(s) that would not read") +
                           " - it is KEPT as " + Path.GetFileName(aside) + ", untouched, and a new board begins" +
                           (runs != null && runs.Count > 0
                               ? " with the " + runs.Count.ToString(Inv) + " row(s) that did read"
                               : "") + ".";

                    if (runs == null)
                        runs = new List<LbEntry>();
                }

                runs.Add(entry);
                WriteFile(path, runs);
                return runs;
            }
            catch (Exception ex)
            {
                note = "COULD NOT WRITE the board's file at " + path + " (" + ex.GetType().Name + ": " +
                       ex.Message + ") - this run is not on the board.";
                return null;
            }
        }

        // Renamed, never deleted: `<file>.bad-yyyyMMdd-HHmmss` (a second one in
        // the same second gets -2, -3...).
        private static string SetAside(string path)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv);
            string aside = path + ".bad-" + stamp;

            for (int i = 2; File.Exists(aside); i++)
                aside = path + ".bad-" + stamp + "-" + i.ToString(Inv);

            if (File.Exists(path))
                File.Move(path, aside);

            return aside;
        }
    }
}
