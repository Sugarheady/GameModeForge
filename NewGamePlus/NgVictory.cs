using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GameModeForge.NewGamePlus
{
    // THE WIN (CONCEPT.md 2.1, DECIDED).
    //
    // ★ THE TRIGGER IS THE 4TH QUEEN, off `RunData.RegisterBossKilled` -
    // NEVER `GameController.GameWon`. That event is declared with no caller,
    // and its other subscriber, `LeaderboardScoreSubmitter`, UPLOADS THE SCORE
    // to the platform leaderboard (2.9). Nothing here invokes it or hangs off
    // it; the playtest is live and the devs will wire it up.
    //
    // ★ THE SCREEN IS THE GAME'S OWN: `UI/GameWonSceen` (the scene's spelling),
    // built and never opened - titled END OF DEMO, with MAIN MENU and PLAY
    // AGAIN. NG+ opens it itself, retitles it (DECIDED: "End of Demo sounds
    // kind of eh" - WORLD 1 CLEARED, NEW GAME PLUS 1 CLEARED, ...) and gives
    // it three buttons:
    //
    //   KEEP PLAYING   close it; you stay in the cleared world, and the pause
    //                  menu gains START NEW GAME PLUS (NgPause).
    //   NEW GAME PLUS  the jump - it does not ask: it is what you just won.
    //   END RUN        asks first (it deletes the save), then the pause
    //                  Restart's own path: DeleteSave, then Restart (the
    //                  loadout selector).
    //
    // The game's screen does not pause time (its UIScreen ships pauseTime 0)
    // and switches your controls to the menu map, so NG+ pauses the world
    // while it is up: you cannot be killed by an enemy you cannot steer away
    // from while you read it.
    internal static class NgVictory
    {
        private static int _builtFor;
        private static GameWonScreen _won;
        private static UIScreen _screen;
        private static Component _title;
        private static Component _subtitle;
        private static bool _open;
        private static bool _pausedWorld;
        private static float _openAt = -1f;

        private static readonly object PauseOwner = new object();

        private static readonly FieldInfo ScreenField = AccessTools.Field(typeof(GameWonScreen), "screen");
        private static readonly FieldInfo BossCountField = AccessTools.Field(typeof(GameController), "bossCount");

        internal static void Reset()
        {
            _open = false;
            _pausedWorld = false;
            _openAt = -1f;

            // The screen objects belong to a scene; a new scene rebuilds them.
            _builtFor = 0;
            _won = null;
            _screen = null;
            _title = null;
            _subtitle = null;
        }

        // How many Queens a world has - the game's own number (4), read off
        // GameController so a future change there is followed.
        internal static int BossCount
        {
            get
            {
                try
                {
                    GameController gc;

                    if (BossCountField != null && ServiceLocator.TryGet<GameController>(out gc) && gc != null)
                        return (int)BossCountField.GetValue(gc);
                }
                catch (Exception)
                {
                }

                return 4;
            }
        }

        // RunData.RegisterBossKilled, postfix.
        internal static void OnBossKilled(RunData run)
        {
            if (!NgRun.Live || run == null || NgJump.InProgress)
                return;

            // Exactly on reaching the count: the 4th Queen, once. A Continue
            // of a cleared world does not re-open it (the count is saved at 4
            // and nothing registers another kill); the pause button is how you
            // come back to it.
            if (run.KilledBossCount != BossCount)
                return;

            NewGamePlusPlugin.Log.LogInfo(
                "the " + BossCount + "th Queen is dead - " + NgMath.WorldName(NgRun.Plus) +
                " CLEARED. The victory screen opens in a moment.");

            // The game's own screen waits `openDelay` (1s) before it opens.
            _openAt = Time.unscaledTime + 1f;
        }

        internal static void Tick()
        {
            if (_openAt > 0f && Time.unscaledTime >= _openAt)
            {
                _openAt = -1f;
                Open();
            }
        }

        private static void Open()
        {
            GameController gc;

            if (!ServiceLocator.TryGet<GameController>(out gc) || gc == null)
                return;

            if (!Build())
            {
                NewGamePlusPlugin.Log.LogWarning(
                    "the game's victory screen could not be found or rebuilt - you can still start " +
                    "NEW GAME PLUS from the pause menu.");
                return;
            }

            NgUi.SetText(_title, NgMath.WorldName(NgRun.Plus) + " CLEARED");

            // What the next world will be like: the rules file as it is now
            // (they lock at the jump, so this is what you will get).
            NgRules next = NgRules.LoadFile();
            NgUi.SetText(_subtitle,
                "All " + BossCount + " Queens are dead.\n" + NgMath.WorldName(NgRun.Plus + 1) +
                ": " + next.Summary(NgRun.Plus + 1));

            TimeManager time;

            if (ServiceLocator.TryGet<TimeManager>(out time) && time != null)
            {
                time.Pause(PauseOwner);
                _pausedWorld = true;
            }

            gc.isPaused = true;
            _open = true;

            _screen.Open();
        }

        private static void Close()
        {
            if (!_open)
                return;

            _open = false;

            try
            {
                _screen.Close();
            }
            catch (Exception)
            {
            }

            Unpause();
        }

        private static void Unpause()
        {
            GameController gc;

            if (ServiceLocator.TryGet<GameController>(out gc) && gc != null)
                gc.isPaused = false;

            TimeManager time;

            if (_pausedWorld && ServiceLocator.TryGet<TimeManager>(out time) && time != null)
                time.RemoveAllModifiers(PauseOwner);

            _pausedWorld = false;
        }

        // ---- the buttons ------------------------------------------------------

        private static void KeepPlaying()
        {
            Close();

            NewGamePlusPlugin.Log.LogInfo(
                "KEEP PLAYING - you stay in " + NgMath.WorldName(NgRun.Plus) + ". START NEW GAME " +
                "PLUS is in the pause menu whenever you want it.");
        }

        private static void NewGamePlus()
        {
            Close();
            NgJump.Begin("the victory screen");
        }

        private static void EndRun()
        {
            bool asked = NgUi.Confirm(
                "End this run? Your save is deleted and you go back to the loadout picker.",
                EndRunConfirmed, null);

            // No popup to ask with: ending a run without asking is the one
            // thing not to do, so the button does nothing and says so.
            if (!asked)
            {
                NewGamePlusPlugin.Log.LogWarning(
                    "END RUN: the confirm popup could not be found, so the run was NOT ended. Use " +
                    "the pause menu's Restart instead.");
            }
        }

        private static void EndRunConfirmed()
        {
            Close();

            GameController gc;
            Punk.SaveLoad.GameSaver saver;

            if (!ServiceLocator.TryGet<GameController>(out gc) || gc == null)
                return;

            // The pause Restart's own path (PauseScreen.RestartConfirmed).
            if (ServiceLocator.TryGet<Punk.SaveLoad.GameSaver>(out saver) && saver != null)
                saver.DeleteSave(gc.IsCoop);

            NewGamePlusPlugin.Log.LogInfo("END RUN - the save is deleted; back to the loadout picker.");

            gc.Restart();
        }

        // ---- the screen ---------------------------------------------------------

        private static bool Build()
        {
            GameWonScreen won = NgUi.FindLive<GameWonScreen>();

            if (won == null)
                return false;

            if (_builtFor == won.GetInstanceID() && _screen != null)
                return true;

            UIScreen screen = ScreenField != null ? ScreenField.GetValue(won) as UIScreen : null;

            if (screen == null)
                screen = won.GetComponent<UIScreen>();

            if (screen == null)
                return false;

            Transform root = won.transform;

            // Measured off Game.unity: GUITitle/Text and "GUITitle (1)"/Text,
            // then BasicButton_MAINMENU (x -200) and BasicButton_RESTART
            // (x 200), each a wrapper with a ButtonBody inside.
            Transform title = NgUi.Find(root, "GUITitle");
            Transform subtitle = NgUi.Find(root, "GUITitle (1)");
            Transform left = NgUi.Find(root, "BasicButton_MAINMENU");
            Transform centre = NgUi.Find(root, "BasicButton_RESTART");

            if (left == null || centre == null)
            {
                NewGamePlusPlugin.Log.LogWarning(
                    "the victory screen's buttons are not where Game.unity had them - it is left alone.");
                return false;
            }

            // The third button, cloned from the second so it looks and animates
            // the same. Made asleep, so its copied persistent onClick (PLAY
            // AGAIN) is switched off before it can ever run.
            GameObject holder;
            GameObject right = NgUi.CloneAsleep(centre.gameObject, "NgButton_ENDRUN", out holder);
            NgUi.Rewire(NgUi.ButtonOf(right), EndRun);
            NgUi.Place(right, holder, centre.parent);
            right.transform.SetSiblingIndex(centre.GetSiblingIndex() + 1);

            NgUi.Rewire(NgUi.ButtonOf(left.gameObject), KeepPlaying);
            NgUi.Rewire(NgUi.ButtonOf(centre.gameObject), NewGamePlus);

            NgUi.SetText(left.gameObject, "KEEP PLAYING");
            NgUi.SetText(centre.gameObject, "NEW GAME PLUS");
            NgUi.SetText(right, "END RUN");

            foreach (GameObject b in new[] { left.gameObject, centre.gameObject, right })
                NgUi.Fit(NgUi.FirstText(b), 36f, 18f);

            // Three across where two were: -400, 0, 400 (each is 349 wide).
            Spread(left as RectTransform, -400f);
            Spread(centre as RectTransform, 0f);
            Spread(right.transform as RectTransform, 400f);

            // NEW GAME PLUS is the centre button and the one selected on open
            // (the UIScreen already selects that ButtonBody).

            _title = title != null ? NgUi.FirstText(title.gameObject) : null;
            _subtitle = subtitle != null ? NgUi.FirstText(subtitle.gameObject) : null;

            // Measured: the title is a 657-wide box at size 140 (END OF DEMO
            // fits; NEW GAME PLUS 12 CLEARED does not), the subtitle a
            // 578-wide box at size 70 (THANKS FOR PALYING). Both texts stretch
            // with their box, so the boxes widen and the text may shrink.
            Widen(title, 1500f);
            Widen(subtitle, 1500f);
            NgUi.Fit(_title, 140f, 48f);
            NgUi.Fit(_subtitle, 44f, 18f);

            AnimatedScreen anim = won.GetComponent<AnimatedScreen>();

            if (anim != null)
                anim.RefreshElementList();

            _won = won;
            _screen = screen;
            _builtFor = won.GetInstanceID();

            NewGamePlusPlugin.Log.LogInfo(
                "victory screen ready: KEEP PLAYING, NEW GAME PLUS, END RUN (the game's own " +
                "END OF DEMO screen, retitled).");

            return true;
        }

        private static void Spread(RectTransform rt, float x)
        {
            if (rt != null)
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        }

        private static void Widen(Transform t, float width)
        {
            var rt = t as RectTransform;

            if (rt != null)
                rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        }
    }

    [HarmonyPatch(typeof(RunData), "RegisterBossKilled")]
    internal static class NgBossKilledPatch
    {
        static void Postfix(RunData __instance)
        {
            try
            {
                NgVictory.OnBossKilled(__instance);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus could not react to a Queen kill: " + ex);
            }
        }
    }
}
