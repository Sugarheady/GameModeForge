using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Punk.SaveLoad;
using UnityEngine;

namespace GameModeForge.EnemyTraits
{
    // A WORLD'S TRAITS, LOCKED - and the save's record of them (CONCEPT.md 3.6).
    //
    // The rules are read from the FILE when a world begins - a new run, and
    // every New Game Plus jump (which is a new run) - and kept for the whole
    // world: an edit (the builder page, the GAME MODE FORGE tab) applies from
    // the next world. What the game's save cannot know lives in
    // `enemytraits.txt` INSIDE the save folder: the rules locked for this world
    // and Enemy Traits' own roll seed. So a Continue gives the same traits on
    // the same enemies, with nothing stored per enemy.
    //
    //   - written in a POSTFIX on GameSaver.Save(string), after creating the
    //     folder (the game makes it on a worker thread);
    //   - read in a PREFIX on GameSaver.Load, with the reset first in the same
    //     prefix. Nothing is decided until two frames after the load anyway
    //     (the owner trap), so the order against New Game Plus's own prefix
    //     does not matter - and the PLUS is asked of New Game Plus only then;
    //   - deleted with the save (death, END RUN, New Game all delete the folder).
    internal static class EtRun
    {
        public const string FileName = "enemytraits.txt";
        private const string Version = "enemytraits 1";

        internal static bool Live;
        internal static int Seed;
        internal static EtRules Rules = EtRules.None();

        // Inside GameSaver.Load: enemies being restored are not new enemies.
        internal static bool Loading;

        private static EtNow _now;

        // ---- ONE reset, two entry points ----------------------------------------

        private static void ResetAll()
        {
            Live = false;
            Seed = 0;
            Rules = EtRules.None();
            _now = null;

            EtDecide.Reset();
            EtWeaponStats.Reset();
            EtInterop.Reset();
            EtShots.Reset();
        }

        // RunData.Initialize, prefix. Runs for a Continue too (before Load),
        // which the Load prefix owns.
        internal static void OnRunDataInitialize()
        {
            if (GameScene.arguments.isContinue)
                return;

            ResetAll();
            Begin(NewSeed(), EtRules.LoadFile());
        }

        private static void Begin(int seed, EtRules rules)
        {
            Live = true;
            Seed = seed;
            Rules = rules ?? EtRules.None();
            _now = null;
        }

        internal static int NewSeed()
        {
            return new System.Random().Next(1, int.MaxValue);
        }

        // The world's numbers at its plus, made once and kept. The plus is New
        // Game Plus's RULES plus (0 without it), asked when first needed - two
        // frames into the world at the earliest, long after both mods' run
        // entry has run.
        internal static EtNow Now
        {
            get
            {
                int plus = EtInterop.RulesPlus;

                if (_now == null || _now.plus != plus)
                    _now = EtNow.Make(Rules, plus);

                return _now;
            }
        }

        // The world's numbers if they have been made, else null - for the hot
        // paths (every shot fired), which must not ask New Game Plus by
        // reflection each time.
        internal static EtNow Cached
        {
            get { return _now; }
        }

        // ---- the save's record ------------------------------------------------------

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
                    // A save made without Enemy Traits never locked any traits:
                    // it continues with none, and the next world takes the file.
                    Begin(NewSeed(), EtRules.None());
                    EnemyTraitsPlugin.Log.LogInfo(
                        "this save has no Enemy Traits record (it was made without Enemy Traits) - this " +
                        "world continues with no traits; the next world takes them from the rules file.");
                    return;
                }

                Read(File.ReadAllLines(path));
            }
            catch (Exception ex)
            {
                Begin(NewSeed(), EtRules.None());
                EnemyTraitsPlugin.Log.LogError(
                    "COULD NOT READ this save's Enemy Traits record (" + ex.GetType().Name + ": " + ex.Message +
                    ") - this world continues with no traits.");
            }
        }

        // GameSaver.Load, postfix (and finalizer, so Loading always clears).
        internal static void OnLoadFinished(bool loaded)
        {
            Loading = false;

            if (loaded)
                EtDecide.OnLoaded();
        }

        private static void Read(string[] lines)
        {
            int seed = 0;
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
                        EnemyTraitsPlugin.Log.LogWarning(
                            "this save's Enemy Traits record is '" + line + "' and this build reads '" + Version +
                            "' - reading what it can.");

                    continue;
                }

                int tab = line.IndexOf('\t');

                if (tab < 0)
                    continue;

                string key = line.Substring(0, tab);
                string value = line.Substring(tab + 1);

                if (key == "seed")
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
                else if (key == "rules")
                    rulesText = value;
            }

            EtRules rules = null;

            if (rulesText != null)
            {
                // ★ LOCKED: a trait the record does not name is OFF.
                var problems = new List<string>();
                rules = EtRules.Parse(rulesText, problems, true);

                foreach (string p in problems)
                    EnemyTraitsPlugin.Log.LogWarning("this save's locked traits: " + p);
            }

            if (rules == null)
            {
                EnemyTraitsPlugin.Log.LogError(
                    "this save's Enemy Traits record has no readable rules - this world continues with none.");
                rules = EtRules.None();
            }

            Begin(seed != 0 ? seed : NewSeed(), rules);
            EnemyTraitsPlugin.Log.LogInfo(
                "continued with this world's locked traits - the same enemies keep the same traits.");
        }

        // GameSaver.Save(string), postfix.
        internal static void OnSaved(string folder)
        {
            if (!Live || string.IsNullOrEmpty(folder))
                return;

            try
            {
                Directory.CreateDirectory(folder);

                var sb = new StringBuilder();
                sb.AppendLine(Version);
                sb.AppendLine("# Enemy Traits' record of this world: its locked traits and its roll seed. " +
                              "Written with every save, read when the save loads. Deleted with the save.");
                sb.AppendLine("seed\t" + Seed.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("rules\t" + Rules.ToJson(false));

                File.WriteAllText(Path.Combine(folder, FileName), sb.ToString());
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError(
                    "COULD NOT SAVE Enemy Traits' record with this save (" + ex.GetType().Name + ": " + ex.Message +
                    ") - a Continue of it would come back with no traits.");
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

            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Application.persistentDataPath, "saves");

            return Path.Combine(Path.Combine(root, folderName ?? ""), FileName);
        }

        // GameController.StartGame, postfix: both mods have begun the world, so
        // the plus is known. Say what this world's traits are - and, with the
        // defaults and no New Game Plus, that there are none (3.7: "its tab row
        // and its log say so rather than sitting silent").
        internal static void OnGameStarted()
        {
            if (!Live)
                return;

            EtNow now = Now;
            string world = EtMath.WorldName(now.plus);
            List<string> parts = Rules.Parts(now.plus);

            if (parts.Count > 0)
            {
                EnemyTraitsPlugin.Log.LogInfo(
                    world + " (rules plus " + now.plus + "): " + string.Join("; ", parts.ToArray()) + "; " +
                    EtMath.VolleyText(now.volleys) + ".");
                return;
            }

            if (!EtInterop.NewGamePlusPresent && Rules.AnyAt(1))
            {
                EnemyTraitsPlugin.Log.LogInfo(
                    "no enemy traits in this world: New Game Plus is not installed (or is switched off), so every world is " +
                    "plus 0, and every trait in your rules starts at NEW GAME PLUS 1 or later. For harder " +
                    "enemies without New Game Plus, pick the FLAT preset in Enemy Traits Builder.html " +
                    "(every trait from plus 0).");
                return;
            }

            EnemyTraitsPlugin.Log.LogInfo("no enemy traits in " + world + " (the rules start them later, or they are off).");
        }

        // The plugin's Update.
        internal static void Tick()
        {
            try
            {
                EtDecide.Tick();
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits' per-frame work threw: " + ex);
            }
        }
    }

    // ONE WORLD'S NUMBERS: the locked rules read at the world's plus, every
    // per-plus box applied. Made once a world (EtRun.Now).
    internal sealed class EtNow
    {
        internal int plus;
        internal readonly float[] coverage = new float[EtMath.TraitCount];
        internal int limit;
        internal int volleys;

        // wind-up
        internal float windUpTime, windDownTime, grace;
        internal bool stepped;
        internal int steps;
        internal EtRewards windupRewards;

        // overload
        internal string fill;
        internal float fillTime, shots, hits, window, coolDelay, coolPerSecond;
        internal bool flat;
        internal EtRewards overloadRewards;

        // grow
        internal float from, to, span, curve;
        internal bool overTime, hitbox, clamp, growRockets;

        // ricochet
        internal int bounces;
        internal bool towardYou, ricochetRockets;
        internal float bounciness;

        internal bool Any
        {
            get
            {
                for (int t = 0; t < EtMath.TraitCount; t++)
                    if (coverage[t] > 0f)
                        return true;

                return false;
            }
        }

        internal static EtNow Make(EtRules r, int plus)
        {
            var n = new EtNow { plus = plus, volleys = r.VolleyMode, limit = r.limit.At(plus) };

            for (int t = 0; t < EtMath.TraitCount; t++)
                n.coverage[t] = r.Trait(t).CoverageAt(plus);

            EtWindupRules w = r.windup;
            n.windUpTime = Mathf.Max(0f, w.At(w.windUpTime, plus));
            n.windDownTime = Mathf.Max(0f, w.At(w.windDownTime, plus));
            n.grace = Mathf.Max(0f, w.At(w.grace, plus));
            n.stepped = w.ramp == "stepped";
            n.steps = Math.Max(1, w.steps);
            n.windupRewards = EtRewards.Resolve(w.rewards, w.fromPlus, plus);

            EtOverloadRules o = r.overload;
            n.fill = o.fill;
            n.fillTime = Mathf.Max(0.05f, o.At(o.fillTime, plus));
            n.shots = Mathf.Max(1f, o.At(o.shots, plus));
            n.hits = Mathf.Max(1f, o.At(o.hits, plus));
            n.window = Mathf.Max(0.05f, o.At(o.window, plus));
            n.coolDelay = Mathf.Max(0f, o.At(o.coolDelay, plus));
            float coolTime = o.At(o.coolTime, plus);
            n.coolPerSecond = coolTime > 0.0001f ? 1f / coolTime : 0f;   // 0: never cools
            n.flat = o.ramp == "flat";
            n.overloadRewards = EtRewards.Resolve(o.rewards, o.fromPlus, plus);

            EtGrowRules g = r.grow;
            n.from = Mathf.Max(0.05f, g.At(g.from, plus));
            n.to = Mathf.Max(0.05f, g.At(g.to, plus));
            n.span = Mathf.Max(0f, g.At(g.span, plus));
            n.curve = Mathf.Max(0.05f, g.At(g.curve, plus));
            n.overTime = g.overTime;
            n.hitbox = g.hitbox;
            n.clamp = g.clamp;
            n.growRockets = g.rockets;

            EtRicochetRules c = r.ricochet;
            n.bounces = c.BouncesAt(plus);
            n.towardYou = c.towardYou;
            n.ricochetRockets = c.rockets;
            n.bounciness = Mathf.Clamp(c.At(c.bounciness, plus), 0f, 1.5f);

            // A ricochet with no bounce is no trait at all.
            if (n.bounces <= 0)
                n.coverage[EtMath.Ricochet] = 0f;

            return n;
        }
    }

    // ---- the patches ------------------------------------------------------------

    [HarmonyPatch(typeof(RunData), "Initialize")]
    internal static class EtRunDataInitPatch
    {
        static void Prefix()
        {
            try
            {
                EtRun.OnRunDataInitialize();
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits could not start this run's state: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameSaver), "Load")]
    internal static class EtLoadPatch
    {
        static void Prefix(GameSaver __instance, string folderName)
        {
            try
            {
                EtRun.OnLoadStarting(__instance, folderName);
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits could not read the save's record: " + ex);
            }
        }

        static void Postfix(bool __result)
        {
            try
            {
                EtRun.OnLoadFinished(__result);
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits could not finish loading: " + ex);
            }
        }

        // A throw inside the game's Load skips the postfix; `Loading` left true
        // would stop every later enemy getting its traits.
        static Exception Finalizer(Exception __exception)
        {
            EtRun.Loading = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GameSaver), "Save", new Type[] { typeof(string) })]
    internal static class EtSavePatch
    {
        static void Postfix(SaveFolder __result)
        {
            try
            {
                EtRun.OnSaved(__result.RootDirectory);
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits could not write its record: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(GameController), "StartGame")]
    internal static class EtStartGamePatch
    {
        static void Postfix()
        {
            try
            {
                EtRun.OnGameStarted();
            }
            catch (Exception ex)
            {
                EnemyTraitsPlugin.Log.LogError("Enemy Traits could not start the world: " + ex);
            }
        }
    }
}
