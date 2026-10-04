using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Punk.SaveLoad;
using UnityEngine;

namespace GameModeForge.NewGamePlus
{
    // WHAT WORLD THIS IS, and the save's record of it.
    //
    // ★ THE HARD REQUIREMENT (CONCEPT.md 2.6), in his words: "If I save and
    // quit a run, close out of the game. I need to know that continuing the
    // game continues from the same new game plus run I'm doing." Most of a
    // world is in the game's own save already - after a jump the new world is
    // an ordinary run. What the game cannot know lives in `newgameplus.txt`
    // INSIDE the save folder: the plus, the rules locked for this world, NG+'s
    // own roll seed and the Queens killed in earlier worlds.
    //
    //   - written in a POSTFIX on GameSaver.Save(string), so every save (the
    //     game's, the debug menu's named slots, ours on arrival) carries it;
    //   - ★ read in a PREFIX on GameSaver.Load. The house rule is "read a
    //     record in a postfix", and here it is exactly wrong: Load restores
    //     every ENTITY before it restores RunData, so a postfix would hand the
    //     rules over after every enemy had loaded at normal strength (the
    //     ordering trap, 2.6). NG+'s own reset runs first in the same prefix.
    //   - ★ deleted for free. Death, the pause Restart, END RUN and the main
    //     menu's New Game all delete the whole save folder, and the file is in
    //     it. Dying ends the whole chain (DECIDED 2026-09-29).
    //
    // ★ THE RULES ARE LOCKED PER WORLD. A Continue gives the same world; an
    // edit to the settings applies at the next jump (or the next run, for
    // plus 0, whose rules lock when the run starts).
    internal static class NgRun
    {
        public const string FileName = "newgameplus.txt";
        private const string Version = "newgameplus 1";

        // ---- this world ----------------------------------------------

        internal static bool Live;          // a run is under way and this is its state
        internal static int Plus;
        internal static int Seed;           // NG+'s own roll seed (never the game's)
        internal static int QueensBefore;   // Queens killed in the earlier worlds of this chain
        internal static NgRules Rules = NgRules.None();

        // Reached by a jump and not yet announced + saved. Cleared BEFORE the
        // arrival save, so the record that save writes says it is done.
        internal static bool ArrivalPending;

        // Inside GameSaver.Load: enemies being restored are not new enemies.
        internal static bool Loading;

        internal static float HealthPercent
        {
            get { return Live ? Rules.HealthPercent(Plus) : 0f; }
        }

        internal static float HealthCoverage
        {
            get { return Live ? Rules.HealthCoverage(Plus) : 0f; }
        }

        internal static float CountPercent
        {
            get { return Live ? Rules.CountPercent(Plus) : 0f; }
        }

        internal static float DamagePercent
        {
            get { return Live ? Rules.DamagePercent(Plus) : 0f; }
        }

        internal static bool DamageEnvironment
        {
            get { return Live && Rules.DamageEnvironment; }
        }

        private static float _announceAt = -1f;
        private static float _saveAt = -1f;

        // ---- ONE reset, two entry points (the house rule) ---------------

        private static void ResetAll()
        {
            Live = false;
            Plus = 0;
            Seed = 0;
            QueensBefore = 0;
            Rules = NgRules.None();
            ArrivalPending = false;
            _announceAt = -1f;
            _saveAt = -1f;

            NgBuffs.Reset();
            NgDamage.Reset();
            NgVictory.Reset();
        }

        // RunData.Initialize, prefix. Runs for EVERY Game scene - a Continue
        // too, before GameSaver.Load - so it asks which one this is.
        internal static void OnRunDataInitialize()
        {
            RunArguments args = GameScene.arguments;

            if (args.isContinue)
            {
                // The Load prefix owns a Continue. A jump that was somehow
                // still pending belongs to nothing now.
                if (NgJump.Pending != null)
                {
                    NewGamePlusPlugin.Log.LogWarning(
                        "a jump was still pending when a saved run was continued - it is dropped.");
                    NgJump.Pending = null;
                    NgCarry.Abandon();
                }

                return;
            }

            ResetAll();

            NgJump.Payload jump = NgJump.Pending;
            NgJump.Pending = null;

            // A jump that never reached its ships (an error in the world it
            // was building) must not leak into this one.
            NgJump.Arriving = null;

            if (jump == null && NgCarry.Carrying)
                NgCarry.Abandon();

            if (jump != null)
            {
                NgJump.Arriving = jump;
                Begin(jump.toPlus, jump.seed, jump.queensBefore, jump.rules, true);
                return;
            }

            // A fresh run: plus 0, with the rules the file says now. A buff
            // with fromPlus 0 is live from here (DECIDED 2026-09-29).
            Begin(0, NewSeed(), 0, NgRules.LoadFile(), false);
        }

        private static void Begin(int plus, int seed, int queensBefore, NgRules rules, bool arrived)
        {
            Live = true;
            Plus = plus;
            Seed = seed;
            QueensBefore = queensBefore;
            Rules = rules ?? NgRules.None();
            ArrivalPending = arrived;

            NewGamePlusPlugin.Log.LogInfo(
                (arrived ? "ARRIVED in " : "run started: ") + NgMath.WorldName(plus) +
                " - " + Rules.Summary(plus) + "." +
                (arrived ? " (" + queensBefore + " Queen(s) killed in earlier worlds.)" : ""));

            // Enemy damage reads the world's numbers once, here, rather than on
            // every hit.
            NgDamage.Refresh();
        }

        internal static int NewSeed()
        {
            return new System.Random().Next(1, int.MaxValue);
        }

        // ---- the save's record ----------------------------------------

        // GameSaver.Load, prefix.
        internal static void OnLoadStarting(GameSaver saver, string folderName)
        {
            ResetAll();
            Loading = true;

            string path = PathFor(saver, folderName);

            try
            {
                if (path == null || !File.Exists(path))
                {
                    // A save made without New Game Plus installed never locked
                    // any rules. It is plus 0 with none, and stays that way.
                    Begin(0, NewSeed(), 0, NgRules.None(), false);
                    NewGamePlusPlugin.Log.LogInfo(
                        "this save has no New Game Plus record (it was made without New Game " +
                        "Plus, or before it was installed) - it continues as the first world with " +
                        "no rules. Its victory screen still offers NEW GAME PLUS.");
                    return;
                }

                Read(File.ReadAllLines(path));
            }
            catch (Exception ex)
            {
                // A record that cannot be read must not stop the save loading.
                Begin(0, NewSeed(), 0, NgRules.None(), false);
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT READ this save's New Game Plus record (" + ex.GetType().Name +
                    ": " + ex.Message + ") - the run continues as the first world with no rules.");
            }
        }

        // GameSaver.Load, postfix (and finalizer, so Loading always clears).
        internal static void OnLoadFinished(bool loaded)
        {
            Loading = false;

            if (loaded)
                NgBuffs.OnLoaded();
        }

        private static void Read(string[] lines)
        {
            int plus = 0, seed = 0, queens = 0;
            bool arrivalDone = true;
            string rulesText = null;
            bool sawVersion = false;

            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (!sawVersion)
                {
                    sawVersion = true;

                    if (line != Version)
                    {
                        NewGamePlusPlugin.Log.LogWarning(
                            "this save's New Game Plus record is '" + line + "' and this build " +
                            "reads '" + Version + "' - reading what it can.");
                    }

                    continue;
                }

                int tab = line.IndexOf('\t');

                if (tab < 0)
                    continue;

                string key = line.Substring(0, tab);
                string value = line.Substring(tab + 1);

                switch (key)
                {
                    case "plus": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out plus); break;
                    case "seed": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed); break;
                    case "queensBefore": int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out queens); break;
                    case "arrived": arrivalDone = value.Trim() != "0"; break;
                    case "rules": rulesText = value; break;
                }
            }

            NgRules rules = null;

            if (rulesText != null)
            {
                // ★ LOCKED: a buff the record does not name is OFF. A world
                // locked before enemy damage existed keeps playing without it.
                var problems = new List<string>();
                rules = NgRules.Parse(rulesText, problems, true);

                foreach (string p in problems)
                    NewGamePlusPlugin.Log.LogWarning("this save's locked rules: " + p);

                if (rules != null && rulesText.IndexOf("\"enemyDamage\"", StringComparison.Ordinal) < 0)
                {
                    NewGamePlusPlugin.Log.LogInfo(
                        "this world's rules were locked before enemy damage existed, so it plays " +
                        "without it. The next world takes it from the rules file.");
                }
            }

            if (rules == null)
            {
                NewGamePlusPlugin.Log.LogError(
                    "this save's New Game Plus record has no readable rules - this world " +
                    "continues with none. (The plus number is kept.)");
                rules = NgRules.None();
            }

            if (seed == 0)
                seed = NewSeed();

            Begin(Math.Max(0, plus), seed, Math.Max(0, queens), rules, false);

            // Saved before the arrival had finished (quit in the first second
            // of a world): the alert is owed, the save is not - this IS it.
            if (!arrivalDone)
                NewGamePlusPlugin.Log.LogInfo("(the arrival alert was never shown in this world; it is skipped on a Continue.)");

            NewGamePlusPlugin.Log.LogInfo(
                "continued " + NgMath.WorldName(Plus) + " from the save - " + Rules.Summary(Plus) +
                ". The same enemies keep the same buffs.");
        }

        // GameSaver.Save(string), postfix.
        internal static void OnSaved(string folder)
        {
            if (!Live || string.IsNullOrEmpty(folder))
                return;

            try
            {
                // ★ The game creates the folder on a WORKER THREAD, after
                // Save has already returned to us - so on a run's first save it
                // may not exist yet. Creating it here is safe either way.
                Directory.CreateDirectory(folder);

                var sb = new StringBuilder();
                sb.AppendLine(Version);
                sb.AppendLine("# New Game Plus's record of this world. Written with every save, read " +
                              "BEFORE the save loads (so the enemies load already buffed). Deleted " +
                              "with the save.");
                sb.AppendLine("plus\t" + Plus.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("seed\t" + Seed.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("queensBefore\t" + QueensBefore.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("arrived\t" + (ArrivalPending ? "0" : "1"));
                sb.AppendLine("rules\t" + Rules.ToJson(false));

                File.WriteAllText(Path.Combine(folder, FileName), sb.ToString());
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT SAVE New Game Plus's record with this save (" + ex.GetType().Name +
                    ": " + ex.Message + ") - a Continue of it would come back as the first world.");
            }
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

        // ---- arriving -----------------------------------------------------

        // GameController.StartGame, postfix. The ships exist and the start
        // sequence has begun.
        internal static void OnGameStarted()
        {
            if (!Live)
                return;

            if (ArrivalPending)
            {
                // Announce, then save - once the start sequence is under way.
                _announceAt = Time.unscaledTime + 1.5f;
                _saveAt = Time.unscaledTime + 2f;
                return;
            }

            // The first world of a run, with a rule already in force from plus
            // 0: say so once. Never on a Continue (said once per world).
            if (!GameScene.arguments.isContinue && Plus == 0 && Rules.AnyAt(0))
                _announceAt = Time.unscaledTime + 1.5f;
        }

        // The plugin's Update.
        internal static void Tick()
        {
            try
            {
                NgBuffs.Tick();
                NgDamage.EndFrame();
                NgVictory.Tick();

                float now = Time.unscaledTime;

                if (_announceAt > 0f && now >= _announceAt)
                {
                    _announceAt = -1f;
                    Announce();
                }

                if (_saveAt > 0f && now >= _saveAt)
                {
                    _saveAt = -1f;
                    SaveOnArrival();
                }
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus's per-frame work threw: " + ex);
            }
        }

        // DECIDED 2026-09-29: a ship-log line when you first enter a world,
        // naming the plus and its rules - once per world, not on a Continue.
        private static void Announce()
        {
            string text = NgMath.WorldName(Plus) + " - " + Rules.Summary(Plus);

            ShipManager ships;

            if (!ServiceLocator.TryGet<ShipManager>(out ships) || ships == null)
                return;

            foreach (Ship ship in ships.Ships)
            {
                try
                {
                    if (ship != null && ship.LogOutput != null)
                        ship.LogOutput.Log(AlertLogId, text, null, 10f);
                }
                catch (Exception)
                {
                    // One ship's log is cosmetic.
                }
            }

            NewGamePlusPlugin.Log.LogInfo("arrival alert: \"" + text + "\"");
        }

        // A log id of our own, so the line never replaces a game line.
        internal const int AlertLogId = 73900;

        // ★ NO CRASH WINDOW (2.6): save the moment the new world is running,
        // so quitting straight after a jump can never land you back in the
        // old world.
        private static void SaveOnArrival()
        {
            if (!ArrivalPending)
                return;

            GameController gc;
            GameSaver saver;

            if (!ServiceLocator.TryGet<GameController>(out gc) || gc == null ||
                !ServiceLocator.TryGet<GameSaver>(out saver) || saver == null)
            {
                return;
            }

            if (saver.IsSaveInProgress)
            {
                _saveAt = Time.unscaledTime + 0.5f;
                return;
            }

            ArrivalPending = false;

            try
            {
                saver.Save(gc.IsCoop);
                NewGamePlusPlugin.Log.LogInfo(
                    "saved on arrival - Save and quit now, relaunch and Continue, and you are in " +
                    NgMath.WorldName(Plus) + ".");
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "COULD NOT SAVE on arriving in the new world (" + ex.GetType().Name + ": " +
                    ex.Message + ") - the next save you make will carry it.");
            }
        }
    }

    // ---- the patches ------------------------------------------------------

    [HarmonyPatch(typeof(RunData), "Initialize")]
    internal static class NgRunDataInitPatch
    {
        static void Prefix()
        {
            try
            {
                NgRun.OnRunDataInitialize();
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not start this run's state: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameSaver), "Load")]
    internal static class NgLoadPatch
    {
        static void Prefix(GameSaver __instance, string folderName)
        {
            try
            {
                NgRun.OnLoadStarting(__instance, folderName);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not read the save's record: " + ex);
            }
        }

        static void Postfix(bool __result)
        {
            try
            {
                NgRun.OnLoadFinished(__result);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not finish loading: " + ex);
            }
        }

        // If the game's own Load throws, the postfix never runs - and a
        // `Loading` left true would stop every later enemy being buffed.
        static Exception Finalizer(Exception __exception)
        {
            NgRun.Loading = false;
            return __exception;
        }
    }

    // `Save(bool coop)` calls `Save(string)`, so one patch covers every save.
    // The overload is named or Harmony cannot tell the two apart.
    [HarmonyPatch(typeof(GameSaver), "Save", new Type[] { typeof(string) })]
    internal static class NgSavePatch
    {
        static void Postfix(SaveFolder __result)
        {
            try
            {
                NgRun.OnSaved(__result.RootDirectory);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not write its record: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameController), "StartGame")]
    internal static class NgStartGamePatch
    {
        static void Postfix()
        {
            try
            {
                NgRun.OnGameStarted();
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not start the world: " + ex);
            }
        }
    }
}
