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
        private static readonly FieldInfo ScreenCanvas = AccessTools.Field(typeof(UIScreen), "canvas");

        // ★ THE PAUSE MENU IS HIDDEN WHILE THIS IS UP (R21, his ask: "would it
        // be possible to hide the pause menu while you're reading the world
        // information? Also maybe make it less transparent"). Its canvas is
        // switched off - the one thing UIScreen itself switches - and back on
        // at Close, only if this switched it off and the pause is still open.
        private static Canvas _pauseCanvas;
        private static bool _hidPause;

        // The game-over screen's backdrop ships at 75% black; this one is
        // nearly solid so the text reads over a busy world.
        private const float BackdropAlpha = 0.96f;

        // The layout (R21 part 3, 339), in the canvas's reference units:
        // the title near the top, the rules in a box from under it down to
        // just above BACK (whose top is at y -306).
        private const float TitleY = 430f;
        private const float InfoTop = 360f;
        private const float InfoWidth = 1500f;
        private const float InfoHeight = 645f;

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

                // ★ R21 PART 3, 338: "Might need to remove the % In the world
                // info though since its those %'s are large as well." Written
                // as a word here whatever the font says - the same word the
                // victory screen uses for a font with no % of its own.
                NgUi.SetText(_stats, NgMath.PercentAsWord(Text()));
                NgUi.SayFit(_stats, "WORLD INFO", InfoWidth, InfoHeight);

                _muted = NgUi.Mute(pause);

                EventSystem es = EventSystem.current;

                if (es != null)
                    es.SetSelectedGameObject(null);

                HidePause(pause);

                _open = true;
                _screen.Open();
            }
            catch (Exception ex)
            {
                _open = false;
                NgUi.Unmute(_muted);
                ShowPause();
                NewGamePlusPlugin.Log.LogError("WORLD INFO failed to open: " + ex);
            }
        }

        private static void HidePause(PauseScreen pause)
        {
            try
            {
                UIScreen ui = pause.GetComponent<UIScreen>();
                Canvas canvas = ui != null && ScreenCanvas != null ? ScreenCanvas.GetValue(ui) as Canvas : null;

                if (canvas == null)
                    canvas = pause.GetComponent<Canvas>();

                if (canvas != null && canvas.enabled)
                {
                    canvas.enabled = false;
                    _pauseCanvas = canvas;
                    _hidPause = true;
                }
            }
            catch (Exception)
            {
                // Cosmetic: the pause menu simply stays visible underneath.
            }
        }

        private static void ShowPause()
        {
            try
            {
                if (_hidPause && _pauseCanvas != null)
                    _pauseCanvas.enabled = true;
            }
            catch (Exception)
            {
            }

            _hidPause = false;
            _pauseCanvas = null;
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
            // the game's own UIScreen owns its buttons now - and its canvas,
            // which its own Close has already switched off.
            if (PauseIsOpen(_pause))
            {
                ShowPause();
                NgUi.Unmute(_muted);

                EventSystem es = EventSystem.current;

                if (es != null && _returnTo != null)
                    es.SetSelectedGameObject(_returnTo.gameObject);
            }
            else
            {
                _hidPause = false;
                _pauseCanvas = null;
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
            sb.AppendLine("World: " + NgMath.WorldWithRules(NgRun.Plus, NgRun.Ahead) +
                          (NgRun.Ahead > 0 ? " - your first-world switch is on" : ""));
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

            // The rules are read one step on with the first-world switch.
            int plus = NgRun.RulesPlus;
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

            // Stage 2: the enemy weapon stats, each with its own share.
            var weapons = new List<string>();
            r.enemyWeapons.AddSummary(weapons, plus);

            if (weapons.Count > 0)
            {
                any = true;
                sb.AppendLine("Enemy weapons: " + string.Join(", ", weapons.ToArray()) +
                              ", bosses included (" + NgWeapons.ChangedGuns + " guns changed so far" +
                              (NgWeapons.PacedVolleys > 0
                                  ? ", " + NgWeapons.PacedVolleys + " volley patterns sped up"
                                  : "") +
                              "; minion and mine launchers are left alone)");
            }

            // Stage 2: the station unlock cost.
            if (r.stationCost.ActiveAt(plus))
            {
                any = true;
                sb.AppendLine("Stations cost " + r.stationCost.first + " money to unlock, +" +
                              r.stationCost.RiseAt(plus) + " more each in this world (" + NgStations.Paid +
                              " unlocked so far; the next costs " + NgStations.NextCostText() + ")" +
                              (r.stationCost.safetyNet
                                  ? ". One opens free when your fuel is critical or a co-op partner is down"
                                  : ""));
            }

            // Stage 2: money drops.
            float money = r.moneyDrops.PercentAt(plus);

            if (money > 0f)
            {
                any = true;
                sb.AppendLine("Money drops " + NgMath.Pct(money) + " (" + r.moneyDrops.SourcesText() + "): " +
                              NgMoney.Extra + " extra money pickups so far" +
                              (NgMoney.BigCoins > 0 ? ", and " + NgMoney.BigCoins + " big coins" : ""));
            }

            // Stage 2: harder enemies earlier and the enemy table - decided
            // when the world was made, so a Continue shows what the world got.
            string harder = NgWorld.InfoLine(r, plus);

            if (harder != null)
            {
                any = true;
                sb.AppendLine(harder);
            }

            // Stage 3: Enemy Traits - its own DLL, asked by type name (CONCEPT.md
            // 3.6: "WORLD INFO lists each trait with its numbers and share of
            // enemies"). Nothing when it is not installed.
            foreach (string line in NgTraitsInfo.WorldLines())
            {
                any = true;
                sb.AppendLine(line);
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
                {
                    NgUi.SetText(title.gameObject, "WORLD INFO");

                    // His 339 note: "the header can be at the middle top of
                    // the screen". Measured off Game.unity: the title is
                    // 112 high at y 322 (centre-anchored); this puts it near
                    // the top edge (540) and leaves the room under it to the
                    // rules.
                    var trt = title as RectTransform;

                    if (trt != null)
                        trt.anchoredPosition = new Vector2(trt.anchoredPosition.x, TitleY);
                }

                // Measured: `Background`, a full-screen Image at 75% black.
                Transform backdrop = NgUi.Find(clone.transform, "Background");
                Image backdropImage = backdrop != null ? backdrop.GetComponent<Image>() : null;

                if (backdropImage != null)
                {
                    Color c = backdropImage.color;
                    backdropImage.color = new Color(c.r, c.g, c.b, BackdropAlpha);
                }

                Transform stats = NgUi.Find(clone.transform, "Stats");
                Component statsText = stats != null ? NgUi.FirstText(stats.gameObject) : null;

                // ★ R21 PART 3, 339: "the worlds with longer text still kind
                // of go off screen. Probably worth having a smaller font
                // brighter text ... bring the info up a bit. its still kind of
                // clipping into the back button a tiny bit." Measured off
                // Game.unity: `Stats` is a FULL-SCREEN rect, centred, grey
                // (0.56) at size 50 - so a long list was centred on the whole
                // screen, under the title and over the buttons (BACK's top is
                // at y -306). It is a box now, from under the title down to
                // above BACK, reading from its top-left corner, nearly white,
                // auto-sized 30 down to 12.
                var srt = stats as RectTransform;

                if (srt != null)
                {
                    srt.anchorMin = new Vector2(0.5f, 0.5f);
                    srt.anchorMax = new Vector2(0.5f, 0.5f);
                    srt.pivot = new Vector2(0.5f, 1f);
                    srt.sizeDelta = new Vector2(InfoWidth, InfoHeight);
                    srt.anchoredPosition = new Vector2(0f, InfoTop);
                }

                NgUi.Fit(statsText, 30f, 12f);
                NgUi.Wrap(statsText);
                NgUi.AlignTopLeft(statsText);
                NgUi.Colour(statsText, new Color(0.94f, 0.94f, 0.94f, 1f));

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

    // THE GAME-OVER SCREEN NAMES THE NEW GAME PLUS YOU REACHED (R21, his ask:
    // "On the gameover screen, can you list the number of new game pluses the
    // player has gone through?"). `GameOverScreen.GetStats` builds its five
    // lines in one string; two more go under them. Measured: the stats text
    // fills the screen, centred, at size 50 - five lines are 300 high and the
    // gap between the title (bottom y 266) and the buttons (top y -306) is
    // 560, so seven fit without shrinking.
    [HarmonyPatch(typeof(GameOverScreen), "GetStats")]
    internal static class NgGameOverPatch
    {
        static void Postfix(ref string __result)
        {
            try
            {
                if (!NgRun.Live)
                    return;

                RunData run;
                ServiceLocator.TryGet<RunData>(out run);

                int queens = NgRun.QueensBefore + (run != null ? run.KilledBossCount : 0);

                __result = (__result ?? "") +
                           "New Game Plus reached: " +
                           (NgRun.Plus > 0 ? NgRun.Plus.ToString(CultureInfo.InvariantCulture) : "none (the first world)") +
                           "\n" +
                           (NgRun.Plus > 0
                               ? "Queens killed, every world: " + queens.ToString(CultureInfo.InvariantCulture) + "\n"
                               : "");
            }
            catch (Exception)
            {
                // The game's own lines stand.
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


    // ★ ENEMY TRAITS, FOUND BY TYPE NAME (stage 3) - its public `EtInfo`:
    // `string[] WorldLines()` for WORLD INFO and `string NextLine(int)` for the
    // victory screen. No reference either way; with Enemy Traits absent (or
    // throwing) this is simply nothing. Looked up again each run until found
    // (plugin load order is not guaranteed).
    internal static class NgTraitsInfo
    {
        private const string TypeName = "GameModeForge.EnemyTraits.EtInfo";

        private static System.Reflection.MethodInfo _world, _next;

        private static bool Find()
        {
            if (_world != null && _next != null)
                return true;

            try
            {
                Type t = NgUi.FindTypeQuietly(TypeName);

                if (t == null)
                    return false;

                const System.Reflection.BindingFlags S =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

                System.Reflection.MethodInfo w = t.GetMethod("WorldLines", S, null, Type.EmptyTypes, null);
                System.Reflection.MethodInfo n = t.GetMethod("NextLine", S, null, new[] { typeof(int) }, null);

                if (w == null || w.ReturnType != typeof(string[]) || n == null || n.ReturnType != typeof(string))
                    return false;

                _world = w;
                _next = n;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string[] WorldLines()
        {
            try
            {
                if (Find())
                    return (string[])_world.Invoke(null, null) ?? new string[0];
            }
            catch (Exception)
            {
            }

            return new string[0];
        }

        internal static string NextLine(int rulesPlus)
        {
            try
            {
                if (Find())
                    return (string)_next.Invoke(null, new object[] { rulesPlus }) ?? "";
            }
            catch (Exception)
            {
            }

            return "";
        }
    }
}
