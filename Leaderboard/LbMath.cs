using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GameModeForge.Leaderboard
{
    // ONE FINISHED RUN - a row of the board. CONCEPT.md 5 decided what a row
    // keeps; 5.1 measured where each number comes from.
    //
    // Pure data, like everything in this file: no Unity, no Newtonsoft, so
    // lbmathprobe.py compiles LbMath.cs on its own and runs it.
    public sealed class LbEntry
    {
        // Local date and time the run ended, "yyyy-MM-dd HH:mm:ss" (sorts as text).
        public string ended = "";

        public bool coop;

        // The REAL world reached, as the titles count it: 0 is the first world
        // (WORLD 1), 1 is NEW GAME PLUS 1...
        public int world;

        // 1 when New Game Plus's first-world switch ran this world's rules a
        // step ahead (the row says so; the rank does not change).
        public int ahead;

        // ★ THE SCORE IS THE MONEY HELD when the run ended (CONCEPT.md 2.3 / 5:
        // GameController.Score is the money tank), so spending lowers it.
        public int score;

        // The whole chain: every world of a New Game Plus run, however its
        // "totals" carry switch is set (5.1).
        public double time;
        public int kills;
        public int stations;
        public int queens;

        // How it ended: Died, or EndRun (New Game Plus's victory screen). A pause
        // Restart or a New Game over an old save never makes a row (5.1, answer 1).
        public string how = LbMath.Died;

        // The last world's four Queens were all killed (a death after KEEP
        // PLAYING is a death like any other, and says so).
        public bool cleared;

        public string loadout = "";

        // The Forge mods this launch was RUNNING (not merely installed).
        public List<string> mods = new List<string>();

        // On a death: the game's own lifetime death count at that death - what
        // the game-over screen's "Total deaths" shows. 0 when not a death.
        public int death;
    }

    public static class LbMath
    {
        public const string Died = "died";
        public const string EndRun = "endrun";

        // Rows drawn on the board (every row is KEPT in the file). One text holds
        // them all, and a TextMeshPro mesh tops out near 16,000 characters.
        public const int Shown = 100;

        // ★ THE MAIN MENU'S FONT CANNOT DRAW THESE. Font_Minimum - the footer
        // buttons' font, which the board's text is made from - lacks
        // # % & < > @ [ ] ^ ` { } ~ (measured off its glyph table, 2026-10-12),
        // and TextMeshPro would draw a missing glyph from another font at the
        // wrong size. So no number, name or word the board writes uses one; only
        // a rich-text TAG may (a tag is never drawn).
        public const string Undrawable = "#%&<>@[]^`{}~";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- the ranking (5.1, answer 2: "Furthest world, then score") --------

        // Negative when `a` ranks ABOVE `b`: the furthest world, then the score,
        // then the shorter time, then the older run.
        public static int Compare(LbEntry a, LbEntry b)
        {
            if (a.world != b.world)
                return b.world.CompareTo(a.world);

            if (a.score != b.score)
                return b.score.CompareTo(a.score);

            if (a.time != b.time)
                return a.time.CompareTo(b.time);

            return string.CompareOrdinal(a.ended ?? "", b.ended ?? "");
        }

        // A sorted copy. OrderBy is stable, so two identical rows keep the
        // file's order.
        public static List<LbEntry> Ranked(IEnumerable<LbEntry> runs)
        {
            if (runs == null)
                return new List<LbEntry>();

            return runs.Where(r => r != null)
                       .OrderBy(r => r, Comparer<LbEntry>.Create(Compare))
                       .ToList();
        }

        // 1-based place of `entry` among `runs`, or 0 when it is not there.
        public static int PlaceOf(IEnumerable<LbEntry> runs, LbEntry entry)
        {
            List<LbEntry> ranked = Ranked(runs);
            int i = ranked.IndexOf(entry);
            return i < 0 ? 0 : i + 1;
        }

        // ---- the chain tally (5.1: "a row is the whole chain") ------------------

        // What a world ADDED to a running total that started at `start`. Never
        // negative: a total the game reset under us counts as nothing, never as
        // a debt.
        public static double Added(double now, double start)
        {
            if (double.IsNaN(now) || double.IsNaN(start))
                return 0d;

            return now > start ? now - start : 0d;
        }

        public static int Added(int now, int start)
        {
            return now > start ? now - start : 0;
        }

        // ---- the other mods' switches ---------------------------------------------

        // Does a BepInEx cfg's `[General] Enabled` say on? Read as TEXT, never
        // through a ConfigFile, so looking at another mod's file can never write
        // it. Missing file, section or key: on (every Forge mod defaults to on,
        // and the game is running it).
        public static bool EnabledInCfg(string cfgText)
        {
            if (string.IsNullOrEmpty(cfgText))
                return true;

            string section = "";

            foreach (string raw in cfgText.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    continue;

                if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (!string.Equals(section, "General", StringComparison.OrdinalIgnoreCase))
                    continue;

                int eq = line.IndexOf('=');

                if (eq <= 0)
                    continue;

                if (!string.Equals(line.Substring(0, eq).Trim(), "Enabled", StringComparison.OrdinalIgnoreCase))
                    continue;

                return !string.Equals(line.Substring(eq + 1).Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        // ---- words ------------------------------------------------------------

        // The names New Game Plus's titles use (NgMath.WorldName, copied).
        public static string WorldName(int world)
        {
            return world <= 0 ? "WORLD 1" : "NEW GAME PLUS " + world.ToString(Inv);
        }

        public static string WorldText(int world, int ahead)
        {
            return WorldName(world) + (ahead > 0 ? " (NG+" + (world + ahead).ToString(Inv) + " rules)" : "");
        }

        // Hours keep counting past a day: 26:03:09, never 1.02:03:09.
        public static string TimeText(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                seconds = 0d;

            long s = (long)Math.Floor(seconds);

            return (s / 3600).ToString(Inv) + ":" + (s / 60 % 60).ToString("00", Inv) + ":" +
                   (s % 60).ToString("00", Inv);
        }

        public static string Grouped(long n)
        {
            return n.ToString("N0", Inv);
        }

        // A name from outside (a loadout - Weapon Forge's are typed by the
        // player) made safe to draw: no character the font lacks, none that
        // opens a rich-text tag, no control characters, one line, capped.
        public static string Clean(string text, int max = 32)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var sb = new StringBuilder(text.Length);
            bool space = false;

            foreach (char c in text)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c) || Undrawable.IndexOf(c) >= 0)
                {
                    space = sb.Length > 0;
                    continue;
                }

                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }

                sb.Append(c);
            }

            string s = sb.ToString();
            return s.Length > max ? s.Substring(0, max).TrimEnd() : s;
        }

        public static string HowText(LbEntry e)
        {
            if (e.how == EndRun)
                return "END RUN";

            return "died" + (e.death > 0 ? " (death " + e.death.ToString(Inv) + ")" : "");
        }

        public static string ModsText(List<string> mods)
        {
            var clean = new List<string>();

            if (mods != null)
                foreach (string m in mods)
                {
                    string c = Clean(m, 12);

                    if (c.Length > 0)
                        clean.Add(c);
                }

            return clean.Count == 0 ? "no Forge mods" : string.Join(" ", clean.ToArray());
        }

        // "2026-10-12 21:04" - the minute is enough on a row.
        public static string EndedText(string ended)
        {
            string s = Clean(ended, 19);
            return s.Length > 16 ? s.Substring(0, 16) : s;
        }

        // One line for the log.
        public static string Summary(LbEntry e)
        {
            return WorldText(e.world, e.ahead) + ", score " + Grouped(e.score) + ", " + TimeText(e.time) + ", " +
                   Grouped(e.kills) + " kills, " + e.queens.ToString(Inv) + " Queens, " + e.stations.ToString(Inv) +
                   " stations, " + HowText(e) + (e.cleared ? ", world cleared" : "") + ", " +
                   (e.coop ? "CO-OP" : "SOLO") + ", " + ModsText(e.mods);
        }

        // ---- the board's text ----------------------------------------------------

        // Column starts, in the board text's own units (TextMeshPro's <pos>).
        // The body is 1,560 wide (LbMenu).
        public static readonly int[] Columns = { 0, 90, 640, 880, 1080, 1250, 1400 };

        // Tags: never drawn, so the # inside the colour is safe.
        private const string Second = "<size=20><color=#9A9A9A>";
        private const string SecondEnd = "</color></size>";

        private static string Col(int i)
        {
            return "<pos=" + Columns[i].ToString(Inv) + ">";
        }

        public static string Header()
        {
            return Col(1) + "WORLD" + Col(2) + "SCORE" + Col(3) + "TIME" + Col(4) + "KILLS" + Col(5) + "QUEENS" +
                   Col(6) + "PLAYERS";
        }

        // Two lines: the numbers the board ranks and compares, then when, how,
        // with what.
        public static string Row(LbEntry e, int rank)
        {
            string first =
                Col(0) + rank.ToString(Inv) + "." +
                Col(1) + WorldText(e.world, e.ahead) +
                Col(2) + Grouped(e.score) +
                Col(3) + TimeText(e.time) +
                Col(4) + Grouped(e.kills) +
                Col(5) + e.queens.ToString(Inv) +
                Col(6) + (e.coop ? "CO-OP" : "SOLO");

            string loadout = Clean(e.loadout);

            string second =
                Second + Col(1) + EndedText(e.ended) + " - " + HowText(e) +
                (e.cleared ? " - world cleared" : "") +
                " - " + e.stations.ToString(Inv) + (e.stations == 1 ? " station" : " stations") +
                (loadout.Length > 0 ? " - loadout " + loadout : "") +
                " - " + ModsText(e.mods) + SecondEnd;

            return first + "\n" + second;
        }

        // The whole board, ranked runs in; the top `Shown` drawn.
        public static string Board(IList<LbEntry> ranked)
        {
            if (ranked == null || ranked.Count == 0)
            {
                return "No finished runs yet.\n\nA run goes on the board when it ends: in a death, or with END RUN " +
                       "on New Game Plus's victory screen. A Restart or a New Game over a save does not count.";
            }

            var sb = new StringBuilder();
            int n = Math.Min(Shown, ranked.Count);

            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                    sb.Append("\n<size=12> </size>\n");

                sb.Append(Row(ranked[i], i + 1));
            }

            return sb.ToString();
        }

        public static string Subtitle(int total, bool recording)
        {
            string count =
                total > Shown ? " Showing the top " + Shown.ToString(Inv) + " of " + Grouped(total) + " runs." :
                total == 1 ? " 1 run." :
                total > 1 ? " " + Grouped(total) + " runs." : "";

            return "Furthest world first, then the score - the money held when the run ended." + count +
                   (recording ? "" : "\nRECORD RUNS IS OFF - new runs are not kept (the GAME MODE FORGE tab).");
        }

        // Everything a text shows with its rich-text tags taken out - what the
        // font is actually asked to draw (lbmathprobe checks it against
        // Undrawable).
        public static string Drawn(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var sb = new StringBuilder(text.Length);
            bool tag = false;

            foreach (char c in text)
            {
                if (c == '<')
                {
                    tag = true;
                    continue;
                }

                if (c == '>' && tag)
                {
                    tag = false;
                    continue;
                }

                if (!tag)
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
