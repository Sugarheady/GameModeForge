using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GameModeForge.NewGamePlus
{
    // TWO BUTTONS IN THE PAUSE MENU (CONCEPT.md 2.1, 2.10 - DECIDED
    // 2026-09-29, "The pause menu order you listed sounds perfect!"):
    //
    //   Continue, WORLD INFO, START NEW GAME PLUS, Restart, Save and quit, Options
    //
    //   WORLD INFO            there from the start of every run - plus 0 too -
    //                         so it never moves; straight under Continue.
    //   START NEW GAME PLUS   once this world's Queens are all dead (the count
    //                         is in the game's own save, so it is still there
    //                         after a Continue); straight under WORLD INFO.
    //                         It ASKS first - it leaves the world you chose to
    //                         stay in.
    //
    // ★ EACH BUTTON FINDS ITS PLACE FROM ITS NEIGHBOUR BY NAME, never by a
    // fixed index, so this and Pause Options (which puts OPTIONS under Save and
    // quit) each work with the other absent. Measured off Game.unity:
    // `UI/PauseMenu/Sorter/Menu/Buttons` is a VerticalLayoutGroup holding
    // ContinueButton, RestartButton, SaveAndQuitButton, ... each 389 wide.
    // The clones are copies of ContinueButton, so they look, size and animate
    // the same.
    //
    // ★ NAVIGATION IS EXPLICIT in that column (Continue down -> Restart), so
    // the links on either side of the new buttons are re-threaded every time
    // the pause opens - START NEW GAME PLUS comes and goes.
    internal static class NgPause
    {
        private sealed class Link
        {
            public int pauseId;
            public PauseScreen pause;
            public GameObject worldInfo;
            public GameObject startNg;
            public Transform column;
        }

        private static Link _link;

        internal static PauseScreen Pause
        {
            get { return _link != null ? _link.pause : null; }
        }

        internal static GameObject WorldInfoButton
        {
            get { return _link != null ? _link.worldInfo : null; }
        }

        // PauseScreen.Open, prefix: before UIScreen.Open enables the buttons
        // and plays their show animation, so a button switched on here behaves
        // exactly like its neighbours.
        internal static void OnPauseOpening(PauseScreen pause)
        {
            try
            {
                if (pause == null || !NgRun.Live)
                    return;

                if (_link == null || _link.pauseId != pause.GetInstanceID() || _link.worldInfo == null)
                    _link = Build(pause);

                if (_link == null)
                    return;

                if (_link.startNg != null)
                    _link.startNg.SetActive(QueensAllDead() && !NgJump.InProgress);

                Thread(_link);
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError(
                    "could not add New Game Plus's buttons to the pause menu - it is the game's own. " + ex);
            }
        }

        internal static bool QueensAllDead()
        {
            RunData run;

            return ServiceLocator.TryGet<RunData>(out run) && run != null &&
                   run.KilledBossCount >= NgVictory.BossCount;
        }

        private static Link Build(PauseScreen pause)
        {
            Transform cont = NgUi.Find(pause.transform, "ContinueButton");

            if (cont == null || cont.parent == null)
            {
                NewGamePlusPlugin.Log.LogWarning(
                    "the pause menu has no ContinueButton - the game's menu has changed, so New Game " +
                    "Plus adds no buttons to it. (WORLD INFO and START NEW GAME PLUS are missing.)");
                return null;
            }

            var link = new Link
            {
                pauseId = pause.GetInstanceID(),
                pause = pause,
                column = cont.parent,
            };

            link.worldInfo = MakeButton(cont, "NgWorldInfoButton", "WORLD INFO", OnWorldInfo);
            link.worldInfo.transform.SetSiblingIndex(cont.GetSiblingIndex() + 1);

            link.startNg = MakeButton(cont, "NgStartNewGamePlusButton", "START NEW GAME PLUS", OnStartNg);
            link.startNg.transform.SetSiblingIndex(link.worldInfo.transform.GetSiblingIndex() + 1);

            // AnimatedScreen collects its elements at Awake - refreshed so the
            // new buttons animate in with the rest (it includes inactive ones).
            AnimatedScreen anim = pause.GetComponent<AnimatedScreen>();

            if (anim != null)
                anim.RefreshElementList();

            NewGamePlusPlugin.Log.LogInfo(
                "pause menu: WORLD INFO under Continue, and START NEW GAME PLUS under it once " +
                "all " + NgVictory.BossCount + " Queens of a world are dead.");

            return link;
        }

        private static GameObject MakeButton(Transform donor, string name, string caption,
                                             UnityEngine.Events.UnityAction click)
        {
            GameObject holder;
            GameObject b = NgUi.CloneAsleep(donor.gameObject, name, out holder);

            // Switched off BEFORE it can wake: the donor's persistent onClick
            // (Continue - close the pause) comes with the copy.
            NgUi.Rewire(NgUi.ButtonOf(b), click);
            NgUi.SetText(b, caption);
            NgUi.Fit(NgUi.FirstText(b), 40f, 18f);

            NgUi.Place(b, holder, donor.parent);
            return b;
        }

        // Up / down links for the new buttons and the buttons either side of
        // them. Only links that touch a new button are written; every other
        // button in the column keeps the game's (and Pause Options') wiring.
        private static void Thread(Link link)
        {
            var column = new List<Selectable>();
            var ours = new HashSet<Selectable>();

            for (int i = 0; i < link.column.childCount; i++)
            {
                Transform t = link.column.GetChild(i);

                if (t == null || !t.gameObject.activeSelf)
                    continue;

                Button b = t.GetComponentInChildren<Button>(true);

                if (b == null)
                    continue;

                column.Add(b);

                if (t.gameObject == link.worldInfo || t.gameObject == link.startNg)
                    ours.Add(b);
            }

            for (int i = 0; i < column.Count; i++)
            {
                Selectable s = column[i];
                Selectable up = i > 0 ? column[i - 1] : null;
                Selectable down = i + 1 < column.Count ? column[i + 1] : null;

                Navigation nav = s.navigation;
                bool changed = false;

                if (ours.Contains(s))
                {
                    nav.mode = Navigation.Mode.Explicit;
                    nav.selectOnUp = up;
                    nav.selectOnDown = down;
                    changed = true;
                }
                else
                {
                    if (down != null && ours.Contains(down))
                    {
                        nav.selectOnDown = down;
                        changed = true;
                    }

                    if (up != null && ours.Contains(up))
                    {
                        nav.selectOnUp = up;
                        changed = true;
                    }

                    // A neighbour that pointed at a hidden START NEW GAME PLUS
                    // would lead nowhere.
                    Button hidden = link.startNg != null && !link.startNg.activeSelf
                        ? NgUi.ButtonOf(link.startNg) : null;

                    if (hidden != null)
                    {
                        if (nav.selectOnDown == hidden) { nav.selectOnDown = down; changed = true; }
                        if (nav.selectOnUp == hidden) { nav.selectOnUp = up; changed = true; }
                    }
                }

                if (changed)
                    s.navigation = nav;
            }
        }

        // ---- the buttons ----------------------------------------------------

        private static void OnWorldInfo()
        {
            if (_link == null || _link.pause == null)
                return;

            NgWorldInfo.Open(_link.pause, NgUi.ButtonOf(_link.worldInfo));
        }

        private static void OnStartNg()
        {
            if (_link == null || _link.pause == null || NgJump.InProgress)
                return;

            bool asked = NgUi.Confirm(
                "Leave " + NgMath.WorldName(NgRun.Plus) + " for " + NgMath.WorldName(NgRun.Plus + 1) +
                "? You keep your build; this world is gone.",
                StartConfirmed, null);

            if (!asked)
            {
                // Leaving a world you chose to stay in is not done unasked.
                NewGamePlusPlugin.Log.LogWarning(
                    "START NEW GAME PLUS: the confirm popup could not be found, so nothing happened.");
            }
        }

        private static void StartConfirmed()
        {
            PauseScreen pause = _link != null ? _link.pause : null;

            if (pause != null)
                pause.Close();

            NgJump.Begin("the pause menu");
        }
    }

    [HarmonyPatch(typeof(PauseScreen), "Open")]
    internal static class NgPauseOpenPatch
    {
        static void Prefix(PauseScreen __instance)
        {
            NgPause.OnPauseOpening(__instance);
        }
    }

    // False skips the pause menu's own Update - its Back and Pause keys -
    // while WORLD INFO is over it, so one Esc / B closes WORLD INFO and leaves
    // the game paused (the stock bug Pause Options fixed for its screen).
    [HarmonyPatch(typeof(PauseScreen), "Update")]
    internal static class NgPauseUpdatePatch
    {
        static bool Prefix()
        {
            try
            {
                return !NgWorldInfo.BlocksPause;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
