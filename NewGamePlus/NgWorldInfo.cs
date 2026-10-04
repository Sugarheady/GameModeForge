using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameModeForge.NewGamePlus
{
    // WORLD INFO (CONCEPT.md 2.10) - his idea: "a button in the pause menu
    // called 'World Info' which has the stats of the run plus the world
    // affixes. That way its in a place the player can see."
    //
    // ★ BUILT FROM THE GAME-OVER SCREEN. `UI/GameOverSceen` is a UIScreen with
    // a background, a title, ONE stats text block and two buttons - the
    // nearest ready-made shape (measured off Game.unity). It is cloned asleep,
    // its GameOverScreen component removed before it can subscribe to the
    // game-over event, MAIN MENU deleted and PLAY AGAIN turned into BACK.
    //
    // ★ IT SITS OVER THE PAUSE MENU, SO IT MEETS BOTH STOCK BUGS PAUSE OPTIONS
    // HAD TO FIX - and handles them itself, since Pause Options may not be
    // installed:
    //   1. Esc / B would close it AND the pause (the pause's Update closes on
    //      the same Back key). -> the pause's Update is skipped while this is
    //      up, and for a moment after, so the press that closed it cannot land
    //      on the pause mid-animation.
    //   2. A gamepad could press a hidden pause button underneath. -> the pause
    //      buttons are switched off while this is open, and back on after,
    //      with WORLD INFO selected.
    internal static class NgWorldInfo
    {
        private static int _builtForScene = -1;
        private static GameObject _root;
        private static UIScreen _screen;
        private static Component _stats;
        private static Selectable _back;

        private static PauseScreen _pause;
        private static Selectable _returnTo;
        private static List<Selectable> _muted = new List<Selectable>();

        private static bool _open;
        private static float _closedAt = -10f;

        private static readonly FieldInfo BackAction = AccessTools.Field(typeof(PauseScreen), "backAction");
        private static readonly FieldInfo PauseAction = AccessTools.Field(typeof(PauseScreen), "pauseAction");
        private static readonly FieldInfo CloseDelay = AccessTools.Field(typeof(UIScreen), "closeDelay");
        private static readonly FieldInfo IsOpenField = AccessTools.Field(typeof(PauseScreen), "isOpen");

        // Open, or closed less than 0.4s ago (the Back press that closed it).
        internal static bool BlocksPause
        {
            get { return _open || Time.unscaledTime - _closedAt < 0.4f; }
        }

        internal static void Open(PauseScreen pause, Selectable returnTo)
        {
            try
            {
                if (_open || pause == null)
                    return;

                if (!Build(pause))
                {
                    NewGamePlusPlugin.Log.LogWarning(
                        "WORLD INFO could not be built (the game-over screen it is made from was not " +
                        "found) - here is what it would show:\n" + Text());
                    return;
                }

                _pause = pause;
                _returnTo = returnTo;

                NgUi.SetText(_stats, Text());

                _muted = NgUi.Mute(pause);

                EventSystem es = EventSystem.current;

                if (es != null)
                    es.SetSelectedGameObject(null);

                _open = true;
                _screen.Open();
            }
            catch (Exception ex)
            {
                _open = false;
                NgUi.Unmute(_muted);
                NewGamePlusPlugin.Log.LogError("WORLD INFO failed to open: " + ex);
            }
        }

        internal static void Close()
        {
            if (!_open)
                return;

            _open = false;
            _closedAt = Time.unscaledTime;

            try
            {
                _screen.Close();
            }
            catch (Exception)
            {
            }

            // Only back into a pause menu that is still open; if it closed,
            // the game's own UIScreen owns its buttons now.
            if (PauseIsOpen(_pause))
            {
                NgUi.Unmute(_muted);

                EventSystem es = EventSystem.current;

                if (es != null && _returnTo != null)
                    es.SetSelectedGameObject(_returnTo.gameObject);
            }
            else
            {
                _muted.Clear();
            }
        }

        // Called every frame by the screen's own component while it is open.
        internal static void Poll()
        {
            if (!_open || _pause == null)
                return;

            ShipManager ships;

            if (!ServiceLocator.TryGet<ShipManager>(out ships) || ships == null)
                return;

            if (Pressed(ships, BackAction) || Pressed(ships, PauseAction))
                Close();
        }

        private static bool Pressed(ShipManager ships, FieldInfo field)
        {
            try
            {
                InputActionReference r = field != null ? field.GetValue(_pause) as InputActionReference : null;
                return r != null && r.action != null && ships.WasPerformedThisFrame(r.action);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool PauseIsOpen(PauseScreen pause)
        {
            if (pause == null)
                return false;

            try
            {
                return IsOpenField == null || (bool)IsOpenField.GetValue(pause);
            }
            catch (Exception)
            {
                return true;
            }
        }

        // ---- what it says --------------------------------------------------

        internal static string Text()
        {
            var sb = new StringBuilder();

            RunData run;
            ServiceLocator.TryGet<RunData>(out run);

            // The run - first the five lines the game-over screen builds
            // (GameOverScreen.GetStats), in its own words.
            sb.AppendLine("<b>THE RUN</b>");

            if (run != null)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Run duration: {0:hh':'mm':'ss}", TimeSpan.FromSeconds(run.TotalRunTime)));
                sb.AppendLine("Stations unlocked: " + run.UnlockedStationCount);
                sb.AppendLine("Enemies killed: " + run.KilledEnemyCount);
                sb.AppendLine("Final bosses killed: " + run.KilledBossCount + "/" + NgVictory.BossCount);
            }

            MetaProgressManager meta;

            if (ServiceLocator.TryGet<MetaProgressManager>(out meta) && meta != null)
                sb.AppendLine("Total deaths: " + meta.GetTotalDeathCount());

            // ...then New Game Plus's own.
            sb.AppendLine("World: " + NgMath.WorldName(NgRun.Plus));
            sb.AppendLine("Queens killed, every world: " +
                          (NgRun.QueensBefore + (run != null ? run.KilledBossCount : 0)));

            GameController gc;

            if (ServiceLocator.TryGet<GameController>(out gc) && gc != null)
            {
                try
                {
                    sb.AppendLine("Money (the score): " + gc.Score);
                }
                catch (Exception)
                {
                }
            }

            sb.AppendLine();
            sb.AppendLine("<b>THIS WORLD'S RULES</b>");

            int plus = NgRun.Plus;
            NgRules r = NgRun.Rules;
            bool any = false;

            float h = r.HealthPercent(plus);
            float hc = r.HealthCoverage(plus);

            if (h > 0f && hc > 0f)
            {
                any = true;
                sb.AppendLine("Enemy health " + NgMath.Pct(h) + " on " +
                              Math.Round(hc).ToString(CultureInfo.InvariantCulture) + "% of enemies" +
                              " (" + NgBuffs.BuffedCount + " buffed so far), bosses included");
            }

            float c = r.CountPercent(plus);

            if (c > 0f)
            {
                any = true;
                sb.AppendLine("Enemy count " + NgMath.Pct(c) + " (every room's spawn budget)");
            }

            float d = r.DamagePercent(plus);

            if (d > 0f)
            {
                any = true;
                sb.AppendLine("Enemy damage " + NgMath.Pct(d) + " on every enemy, bosses included" +
                              (r.DamageEnvironment ? ", hazards too" : " (not hazards)") +
                              " (" + NgDamage.Boosted + " hits made harder so far" +
                              (NgDamage.Unknown > 0
                                  ? ", " + NgDamage.Unknown + " left alone - their source could not be told"
                                  : "") + ")");
            }

            if (!any)
                sb.AppendLine("No rules are in force in this world.");

            return sb.ToString().TrimEnd();
        }

        // ---- the screen -----------------------------------------------------

        private static bool Build(PauseScreen pause)
        {
            int scene = pause.gameObject.scene.handle;

            if (_root != null && _builtForScene == scene)
                return true;

            GameOverScreen over = NgUi.FindLive<GameOverScreen>();

            if (over == null)
                return false;

            GameObject holder;
            GameObject clone = NgUi.CloneAsleep(over.gameObject, "NgWorldInfo", out holder);

            try
            {
                // ★ Before it can wake: GameOverScreen subscribes to the
                // game-over event in OnEnable, and a second one would open this
                // copy as a second game-over screen when you die.
                GameOverScreen copy = clone.GetComponent<GameOverScreen>();

                if (copy != null)
                    UnityEngine.Object.DestroyImmediate(copy);

                Transform menuButton = NgUi.Find(clone.transform, "BasicButton_MAINMENU");

                if (menuButton != null)
                    UnityEngine.Object.DestroyImmediate(menuButton.gameObject);

                Transform back = NgUi.Find(clone.transform, "BasicButton_RESTART");
                Button backButton = back != null ? NgUi.ButtonOf(back.gameObject) : null;

                if (backButton != null)
                {
                    NgUi.Rewire(backButton, Close);
                    NgUi.SetText(back.gameObject, "BACK");

                    var rt = back as RectTransform;

                    if (rt != null)
                        rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
                }

                Transform title = NgUi.Find(clone.transform, "GUITitle");

                if (title != null)
                    NgUi.SetText(title.gameObject, "WORLD INFO");

                Transform stats = NgUi.Find(clone.transform, "Stats");
                Component statsText = stats != null ? NgUi.FirstText(stats.gameObject) : null;

                // The game-over stats are five lines at size 50; this is
                // about fifteen, so it shrinks to fit.
                NgUi.Fit(statsText, 34f, 16f);

                // Above the pause menu (both ship at sorting order 300), below
                // the confirm popup (1200) and Options (10000).
                Canvas canvas = clone.GetComponent<Canvas>();

                if (canvas != null)
                    canvas.sortingOrder = 900;

                UIScreen screen = clone.GetComponent<UIScreen>();

                if (screen == null)
                    throw new InvalidOperationException("the game-over screen has no UIScreen");

                // The game-over screen waits a whole second after closing;
                // over a pause menu that is a second of a screen in the way.
                if (CloseDelay != null)
                    CloseDelay.SetValue(screen, 0.25f);

                clone.AddComponent<NgWorldInfoScreen>();

                NgUi.Place(clone, holder, over.transform.parent);
                holder = null;

                _root = clone;
                _screen = screen;
                _stats = statsText;
                _back = backButton;
                _builtForScene = scene;

                NewGamePlusPlugin.Log.LogInfo("WORLD INFO screen ready (built from the game-over screen).");
                return true;
            }
            catch (Exception ex)
            {
                if (clone != null)
                    UnityEngine.Object.DestroyImmediate(clone);

                if (holder != null)
                    UnityEngine.Object.Destroy(holder);

                NewGamePlusPlugin.Log.LogError("could not build WORLD INFO: " + ex);
                return false;
            }
        }
    }

    // The screen's own Update: its Back / Pause key. The pause menu's Update
    // is skipped while this is up, so it has to read them itself.
    internal sealed class NgWorldInfoScreen : MonoBehaviour
    {
        private void Update()
        {
            try
            {
                NgWorldInfo.Poll();
            }
            catch (Exception)
            {
                // Never out of an Update.
            }
        }
    }
}
