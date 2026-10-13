using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Punk.SaveLoad;
using UnityEngine;

namespace GameModeForge.Leaderboard
{
    // ONE RUN, FROM ITS START TO THE ROW IT MAKES (CONCEPT.md 5, 5.1).
    //
    // ★ WHAT ENDS A RUN (5.1, answer 1: "Death + END RUN"). Measured: every end
    // deletes the save through GameSaver.DeleteSave - death, the pause Restart,
    // END RUN, the main menu's New Game over a save - but only two are a
    // FINISHED run:
    //   - a death: the game's own GameController.GameOver (all ships down),
    //     heard as a subscriber, no patch - the run is still loaded then;
    //   - END RUN: New Game Plus's NgCarry.RunEnding (contract 3), raised just
    //     before the save goes.
    // A Restart or a New Game over a save never makes a row.
    //
    // ★ A ROW IS THE WHOLE CHAIN, WHATEVER NEW GAME PLUS'S "TOTALS" CARRY SWITCH
    // SAYS (5.1). The game counts run time, kills and stations per RunData, and
    // New Game Plus adds the old world's to the new one only while that switch
    // is on; the Queens it zeroes every world. So the board keeps its own tally:
    // at each jump (NgCarry.Leaving) it takes what THAT world added - its totals
    // now, less what it STARTED from - and a new world's start is measured once
    // New Game Plus has carried the run record (NgCarry.Arrived, or StartGame).
    // Carried totals are therefore never counted twice, and a reset one never
    // goes negative (LbMath.Added).
    //
    // ★ ITS OWN RECORD IN THE SAVE FOLDER (`leaderboard.txt`, the sidecar
    // pattern): the tally, the start and the loadout - which a Continue would
    // otherwise lose (RunArguments.startingLoadout is empty on a Continue).
    // Written in a Save(string) postfix after CreateDirectory (the game makes
    // the folder on a worker thread); read in a Load POSTFIX (the reset is the
    // prefix - nothing about enemies depends on it, unlike New Game Plus's);
    // deleted with the save.
    internal static class LbRun
    {
        internal const string FileName = "leaderboard.txt";
        private const string Version = "leaderboard 1";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly FieldInfo BossCountField = AccessTools.Field(typeof(GameController), "bossCount");

        // A run is under way (from its run entry; a save before one is not ours).
        internal static bool Live;

        // The chain's EARLIER worlds - what each added, summed.
        internal static int Worlds;
        internal static double Time;
        internal static int Kills, Stations, Queens;

        // What THIS world started from (RunData's totals once carried).
        internal static bool StartKnown;
        internal static double StartTime;
        internal static int StartKills, StartStations;

        internal static string Loadout = "";

        // One row per run, whichever end comes first.
        internal static bool Recorded;

        // A jump's old world, from Leaving until the new world's run entry.
        private sealed class Held
        {
            internal double time;
            internal int kills, stations, queens;
        }

        private static Held _held;

        private static void Reset()
        {
            Worlds = 0;
            Time = 0d;
            Kills = Stations = Queens = 0;
            StartKnown = false;
            StartTime = 0d;
            StartKills = StartStations = 0;
            Loadout = "";
            Recorded = false;
        }

        // ---- the run entries ------------------------------------------------------

        // RunData.Initialize, prefix: a new run, every jump's new world - and a
        // Continue too (every Game scene makes a RunData), which GameSaver.Load
        // owns.
        internal static void OnRunDataInitialize()
        {
            LbCarry.OnRunEntry();

            RunArguments args = GameScene.arguments;

            if (args.isContinue)
                return;

            Live = true;

            if (LbCarry.Carrying)
            {
                // The new world of a New Game Plus jump: the old one joins the tally.
                Held h = _held;
                _held = null;
                Worlds++;

                if (h != null)
                {
                    Time += h.time;
                    Kills += h.kills;
                    Stations += h.stations;
                    Queens += h.queens;
                }
                else
                {
                    LeaderboardPlugin.Log.LogWarning(
                        "a New Game Plus jump the board did not see leave - the world before it is not added " +
                        "to this run's row.");
                }

                StartKnown = false;
                Recorded = false;

                LeaderboardPlugin.Log.LogInfo(
                    "New Game Plus jump: this run's row now holds " + Worlds + " earlier world(s) - " +
                    LbMath.TimeText(Time) + ", " + Kills + " kills, " + Stations + " stations, " + Queens +
                    " Queens.");
                return;
            }

            if (_held != null)
            {
                LeaderboardPlugin.Log.LogWarning("a New Game Plus jump that never arrived - what it held is dropped.");
                _held = null;
            }

            Reset();
            Live = true;
            Loadout = LoadoutName(args.startingLoadout);

            // A new run starts from nothing.
            StartKnown = true;
        }

        // GameSaver.Load, prefix: a Continue (or a debug load) - everything
        // comes from the save's record, read in the postfix.
        internal static void OnLoadStarting()
        {
            LbCarry.OnRunEntry();
            _held = null;
            Reset();
            Live = true;
        }

        // GameSaver.Load, postfix.
        internal static void OnLoadFinished(bool loaded, GameSaver saver, string folderName)
        {
            if (!loaded)
                return;

            string path = PathFor(saver, folderName);

            if (!File.Exists(path))
            {
                // A save made before the Leaderboard was installed: count this
                // world's totals exactly as the game keeps them (with New Game
                // Plus's totals switch on, they already hold the earlier worlds).
                StartKnown = true;

                LeaderboardPlugin.Log.LogInfo(
                    "this save has no Leaderboard record (made before it was installed?) - its row will count " +
                    "the run's totals as the game keeps them.");
                return;
            }

            try
            {
                Read(File.ReadAllText(path, Encoding.UTF8));

                LeaderboardPlugin.Log.LogInfo(
                    "continued - this run's row holds " + Worlds + " earlier world(s)" +
                    (Loadout.Length > 0 ? ", loadout " + Loadout : "") + ".");
            }
            catch (Exception ex)
            {
                StartKnown = true;
                LeaderboardPlugin.Log.LogError(
                    "could not read the board's record in this save (" + ex.GetType().Name + ": " + ex.Message +
                    ") - its row will count the run's totals as the game keeps them.");
            }
        }

        // NgCarry.Arrived: the run record is carried; the new world starts here.
        internal static void OnArrived()
        {
            if (!StartKnown)
                MeasureStart();
        }

        // GameController.StartGame, postfix: the backstop for a world whose start
        // was not measured yet (an Arrived that never came).
        internal static void OnGameStarted()
        {
            if (Live && !StartKnown)
                MeasureStart();
        }

        private static void MeasureStart()
        {
            RunData run;

            if (!ServiceLocator.TryGet<RunData>(out run) || run == null)
                return;

            StartTime = run.TotalRunTime;
            StartKills = run.KilledEnemyCount;
            StartStations = run.UnlockedStationCount;
            StartKnown = true;
        }

        // NgCarry.Leaving: the old world, still loaded. Held, not added: a jump
        // that never arrives leaves the old world running, and its run counts
        // it at the end in the ordinary way.
        internal static void OnLeaving()
        {
            RunData run;

            if (!ServiceLocator.TryGet<RunData>(out run) || run == null)
            {
                _held = null;
                return;
            }

            _held = new Held
            {
                time = StartKnown ? LbMath.Added(run.TotalRunTime, StartTime) : 0d,
                kills = StartKnown ? LbMath.Added(run.KilledEnemyCount, StartKills) : 0,
                stations = StartKnown ? LbMath.Added(run.UnlockedStationCount, StartStations) : 0,
                queens = run.KilledBossCount
            };
        }

        // ---- the ends --------------------------------------------------------------

        // GameController.GameOver (subscribed by the plugin). ★ The game raises
        // it as a multicast delegate: a throw here would stop the game-over
        // screen's own subscriber, so nothing may leave this method.
        internal static void OnGameOverEvent()
        {
            try
            {
                End(LbMath.Died);
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the board could not record this death: " + ex);
            }
        }

        // NgCarry.RunEnding (contract 3): END RUN, before the save is deleted.
        internal static void OnRunEnding()
        {
            End(LbMath.EndRun);
        }

        private static void End(string how)
        {
            if (!Live || Recorded)
                return;

            Recorded = true;

            LbEntry e = Build(how);

            if (e == null)
            {
                LeaderboardPlugin.Log.LogWarning("the run's numbers could not be read - it is not on the board.");
                return;
            }

            string summary = LbMath.Summary(e);

            if (!LbSettings.Recording)
            {
                LeaderboardPlugin.Log.LogInfo("Record runs is OFF - this run is NOT on the board: " + summary + ".");
                return;
            }

            string note;
            List<LbEntry> board = LbStore.Add(LbStore.FilePath, e, out note);

            if (note != null)
            {
                if (board == null)
                    LeaderboardPlugin.Log.LogError(note);
                else
                    LeaderboardPlugin.Log.LogWarning(note);
            }

            if (board != null)
            {
                LeaderboardPlugin.Log.LogInfo(
                    "ON THE LEADERBOARD, place " + LbMath.PlaceOf(board, e) + " of " + board.Count + ": " + summary + ".");
            }
        }

        internal static LbEntry Build(string how)
        {
            RunData run;

            if (!ServiceLocator.TryGet<RunData>(out run) || run == null)
                return null;

            GameController gc;
            ServiceLocator.TryGet<GameController>(out gc);

            var e = new LbEntry();
            e.ended = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            e.how = how;
            e.coop = gc != null && gc.IsCoop;

            // ★ THE SCORE IS THE MONEY HELD (GameController.Score, the money tank).
            try
            {
                e.score = gc != null ? gc.Score : 0;
            }
            catch (Exception)
            {
                e.score = 0;
            }

            // The real world: New Game Plus's when it is running, else the jumps
            // this board saw (a save from a switched-off New Game Plus).
            e.world = Math.Max(LbCarry.Plus, Worlds);
            e.ahead = LbCarry.Ahead;

            e.time = Time + (StartKnown ? LbMath.Added(run.TotalRunTime, StartTime) : 0d);
            e.kills = Kills + (StartKnown ? LbMath.Added(run.KilledEnemyCount, StartKills) : 0);
            e.stations = Stations + (StartKnown ? LbMath.Added(run.UnlockedStationCount, StartStations) : 0);
            e.queens = Queens + run.KilledBossCount;
            e.cleared = run.KilledBossCount >= BossCount();
            e.loadout = Loadout ?? "";
            e.mods = LbMods.Running();

            if (how == LbMath.Died)
            {
                // GameOver comes after the game's own RegisterDeath, so this
                // count includes this death - the game-over screen's number.
                MetaProgressManager meta;

                if (ServiceLocator.TryGet<MetaProgressManager>(out meta) && meta != null)
                {
                    try
                    {
                        e.death = Math.Max(0, meta.GetTotalDeathCount());
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return e;
        }

        private static int BossCount()
        {
            try
            {
                GameController gc;

                if (BossCountField != null && ServiceLocator.TryGet<GameController>(out gc) && gc != null)
                {
                    int n = (int)BossCountField.GetValue(gc);

                    if (n > 0)
                        return n;
                }
            }
            catch (Exception)
            {
            }

            return 4;
        }

        private static string LoadoutName(LoadoutTemplate t)
        {
            try
            {
                if (t == null)
                    return "";

                return !string.IsNullOrEmpty(t.displayName) ? t.displayName : t.name;
            }
            catch (Exception)
            {
                return "";
            }
        }

        // ---- the record --------------------------------------------------------------

        // GameSaver.Save(string), postfix.
        internal static void OnSaved(string folder)
        {
            if (!Live || string.IsNullOrEmpty(folder))
                return;

            try
            {
                // ★ The game creates the folder on a WORKER THREAD, after Save has
                // returned - so on a run's first save it may not exist yet.
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, FileName), Write(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError(
                    "COULD NOT SAVE the board's record with this save (" + ex.GetType().Name + ": " + ex.Message +
                    ") - a Continue of it would count only what the game keeps.");
            }
        }

        private static string Write()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Version);
            sb.AppendLine("# The Leaderboard's record of this run: what the earlier worlds of a New Game Plus run " +
                          "added, what this world started from, and the loadout. Deleted with the save.");
            sb.AppendLine("worlds\t" + Worlds.ToString(Inv));
            sb.AppendLine("time\t" + Time.ToString("R", Inv));
            sb.AppendLine("kills\t" + Kills.ToString(Inv));
            sb.AppendLine("stations\t" + Stations.ToString(Inv));
            sb.AppendLine("queens\t" + Queens.ToString(Inv));
            sb.AppendLine("startKnown\t" + (StartKnown ? "1" : "0"));
            sb.AppendLine("startTime\t" + StartTime.ToString("R", Inv));
            sb.AppendLine("startKills\t" + StartKills.ToString(Inv));
            sb.AppendLine("startStations\t" + StartStations.ToString(Inv));
            sb.AppendLine("loadout\t" + (Loadout ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "));
            return sb.ToString();
        }

        private static void Read(string text)
        {
            string[] lines = (text ?? "").Replace("\r", "").Split('\n');

            if (lines.Length == 0 || lines[0].Trim() != Version)
                throw new FormatException("not a '" + Version + "' record");

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                int tab = line.IndexOf('\t');

                if (line.StartsWith("#", StringComparison.Ordinal) || tab <= 0)
                    continue;

                string key = line.Substring(0, tab);
                string value = line.Substring(tab + 1);

                switch (key)
                {
                    case "worlds": Worlds = Math.Max(0, Int(value)); break;
                    case "time": Time = Math.Max(0d, Num(value)); break;
                    case "kills": Kills = Math.Max(0, Int(value)); break;
                    case "stations": Stations = Math.Max(0, Int(value)); break;
                    case "queens": Queens = Math.Max(0, Int(value)); break;
                    case "startKnown": StartKnown = value.Trim() == "1"; break;
                    case "startTime": StartTime = Math.Max(0d, Num(value)); break;
                    case "startKills": StartKills = Math.Max(0, Int(value)); break;
                    case "startStations": StartStations = Math.Max(0, Int(value)); break;
                    case "loadout": Loadout = value.Trim(); break;
                }
            }
        }

        private static int Int(string s)
        {
            int v;
            return int.TryParse(s.Trim(), NumberStyles.Integer, Inv, out v) ? v : 0;
        }

        private static double Num(string s)
        {
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, Inv, out v) && !double.IsNaN(v) && !double.IsInfinity(v)
                ? v
                : 0d;
        }

        private static string PathFor(GameSaver saver, string folderName)
        {
            string root = null;

            try
            {
                if (saver != null)
                    root = Traverse.Create(saver).Field("savesRootDirectory").GetValue<string>();
            }
            catch (Exception)
            {
            }

            // The field is private; if it is ever renamed, rebuild it the way
            // the game's constructor does rather than failing.
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Application.persistentDataPath, "saves");

            return Path.Combine(Path.Combine(root, folderName ?? ""), FileName);
        }
    }

    // ---- the patches ------------------------------------------------------------

    [HarmonyPatch(typeof(RunData), "Initialize")]
    internal static class LbRunDataInitPatch
    {
        static void Prefix()
        {
            try
            {
                LbRun.OnRunDataInitialize();
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the board could not start this run's row: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameSaver), "Load")]
    internal static class LbLoadPatch
    {
        static void Prefix()
        {
            try
            {
                LbRun.OnLoadStarting();
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the board could not reset for a Continue: " + ex);
            }
        }

        static void Postfix(bool __result, GameSaver __instance, string folderName)
        {
            try
            {
                LbRun.OnLoadFinished(__result, __instance, folderName);
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the board could not read its record: " + ex);
            }
        }
    }

    // `Save(bool coop)` calls `Save(string)`, so one patch covers every save.
    // The overload is named or Harmony cannot tell the two apart.
    [HarmonyPatch(typeof(GameSaver), "Save", new Type[] { typeof(string) })]
    internal static class LbSavePatch
    {
        static void Postfix(SaveFolder __result)
        {
            try
            {
                // A readonly struct: never null.
                LbRun.OnSaved(__result.RootDirectory);
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the board could not write its record: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameController), "StartGame")]
    internal static class LbStartGamePatch
    {
        static void Postfix()
        {
            try
            {
                LbRun.OnGameStarted();
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("the board could not start the world: " + ex);
            }
        }
    }
}
