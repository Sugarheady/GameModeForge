using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameModeForge.PauseOptions
{
    // Switches on the pause menu's own Options button, and makes Options
    // behave when it is opened over a paused run.
    //
    // ★ MEASURED OFF Game.unity (2026-09-28), and none of it is new UI:
    //
    //   UI/PauseMenu/Sorter/Menu/Buttons/OptionsButton   m_IsActive: 0
    //     ButtonBody: Button.onClick -> OptionsMenu's OptionsScreen.Open
    //                 (a persistent scene event; at no call site in the code)
    //   Buttons carries an ENABLED VerticalLayoutGroup (+ ContentSizeFitter),
    //     so a button switched on is laid out in the column - unlike the
    //     options tab row, whose layout group ships disabled. But it lays out
    //     HEIGHT only (Options keeps its own 325.9 width beside 389.084), and
    //     a 71-high `Spacer` sits between "Save and quit" and Options. R20 291
    //     saw both; `Tidy` moves it up and matches the width.
    //   Navigation is explicit and already threads through it:
    //     Save and quit (down) -> Options (up) -> Save and quit.
    //   OptionsMenu is the same screen as the main menu's (sorting order
    //     10000 over the pause menu's 300, a full-screen raycast blocker, and
    //     every wait in its animations is WaitForSecondsRealtime - so it runs
    //     while the pause has time stopped).
    //
    // ★ WHAT THE STOCK WIRING GETS WRONG WITH THE BUTTON ON - probably why it
    // ships off, though nothing says so:
    //
    //   1. ESC / B CLOSES BOTH. OptionsMenu/Close and Menu/Back are bound to
    //      the same keys (Escape, gamepad East), and PauseScreen.Update closes
    //      the pause on Menu/Back with no thought for a screen over it. The
    //      main menu has exactly that thought (`MainMenu.Update` ignores its
    //      exit key while `optionsScreen.isActiveAndEnabled`); the pause menu
    //      does not. So one press closed Options AND unpaused the game.
    //      -> PauseScreen.Update is skipped while Options is open.
    //
    //   2. A GAMEPAD CAN PRESS A HIDDEN PAUSE BUTTON. The options screen reads
    //      its own actions, but the EventSystem still navigates and submits on
    //      the pause buttons underneath: stick up, then A (which is also
    //      OptionsMenu/Apply), pressed "Save and quit" behind the Options
    //      screen. -> the pause buttons are switched off while Options is open,
    //      exactly the way the game's own UIScreen switches them off when the
    //      pause closes, and put back - with Options selected - after.
    //
    // ★ IF THE GAME EVER SWITCHES THE BUTTON ON ITSELF, THIS STANDS ASIDE.
    // The playtest is live and the devs may finish this; two fixes for one
    // bug fight. An Options button that is already on is theirs.
    internal static class PauseMenuOptions
    {
        // One pause menu per Game scene, measured at its first Open.
        private sealed class Link
        {
            public int pauseId;
            public PauseScreen pause;
            public GameObject body;          // the ButtonBody, which is selected after
            public OptionsScreen options;    // what that button opens
            public bool ours;                // we switched the button on

            public bool muting;
            public readonly List<Selectable> muted = new List<Selectable>();
        }

        private static Link _link;

        private static readonly FieldInfo IsOpenField =
            AccessTools.Field(typeof(PauseScreen), "isOpen");

        private static ManualLogSource Log
        {
            get { return PauseOptionsPlugin.Log; }
        }

        // ---- 1. the button ----------------------------------------------

        // Prefix on PauseScreen.Open, so the button is on BEFORE the game's
        // UIScreen.Open runs: that is what enables the menu's buttons and
        // plays their show animation (AnimatedScreen collected every element,
        // inactive ones included, at Awake), so the switched-on button then
        // behaves exactly like its neighbours - including being switched off
        // again when the pause closes.
        internal static void OnPauseOpening(PauseScreen pause)
        {
            try
            {
                if (pause == null)
                    return;

                if (_link != null && _link.pauseId == pause.GetInstanceID())
                    return;

                _link = Measure(pause);
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "could not switch on the pause menu's Options button - the " +
                    "pause menu is the game's own. " + ex);
            }
        }

        private static Link Measure(PauseScreen pause)
        {
            var link = new Link { pauseId = pause.GetInstanceID(), pause = pause };

            Transform wrapper = null;

            foreach (Transform t in pause.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "OptionsButton")
                {
                    wrapper = t;
                    break;
                }
            }

            if (wrapper == null)
            {
                Log.LogWarning(
                    "no OptionsButton in the pause menu - the game's menu has " +
                    "changed, so Pause Options leaves it alone.");
                return link;
            }

            Button button = wrapper.GetComponentInChildren<Button>(true);
            OptionsScreen options = button != null ? OpensOptions(button.onClick) : null;

            // ★ A button that opens nothing is worse than no button: it reads
            // as Options being broken. Only switch it on if it is still wired
            // to an options screen in this scene.
            if (options == null || options.gameObject.scene != pause.gameObject.scene)
            {
                Log.LogWarning(
                    "the pause menu's Options button no longer opens an options " +
                    "screen in this scene - leaving it off.");
                return link;
            }

            link.body = button.gameObject;
            link.options = options;

            if (wrapper.gameObject.activeSelf)
            {
                Log.LogInfo(
                    "the game's own pause menu shows Options now - Pause Options " +
                    "is standing aside (this DLL can be removed).");
                return link;
            }

            wrapper.gameObject.SetActive(true);

            // ★ A wired event on an inactive object is a feature that is not
            // there - and so is a switched-on button under a switched-off
            // parent. Check the whole path, and undo if it is not visible.
            if (!wrapper.gameObject.activeInHierarchy)
            {
                wrapper.gameObject.SetActive(false);
                Log.LogWarning(
                    "the pause menu's Options button is under a switched-off " +
                    "parent - leaving it off.");
                return link;
            }

            link.ours = true;

            Log.LogInfo(
                "Options switched on in the pause menu. Esc / B closes Options " +
                "and leaves the game paused; the pause buttons are locked while " +
                "Options is open.");

            Tidy(wrapper);

            return link;
        }

        // ★ R20 TEST 291: "the width of the options box doesnt match the same
        // width size as the other buttons. Also wouldnt mind the button right
        // below save and quit. right now there's a gap." Both measured off
        // Game.unity: the Buttons group lays out HEIGHT only, so each button
        // keeps its own width - OptionsButton ships 325.9 wide beside 389.084
        // for the other three - and the stock order is Continue, Restart, Save
        // and quit, Quit (off), a 71-high `Spacer`, THEN Options. The Spacer
        // is the gap. So Options moves up to sit straight under Save and quit
        // (the Spacer keeps its place at the bottom, which is where it was on
        // screen before this DLL), and takes that button's width.
        //
        // Cosmetic, so it can only cost itself: any doubt leaves the button
        // where the game put it, which is where round 20 tested it.
        private static void Tidy(Transform wrapper)
        {
            try
            {
                Transform parent = wrapper.parent;

                if (parent == null)
                    return;

                Transform above = null;

                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform t = parent.GetChild(i);

                    if (t != null && t.name == "SaveAndQuitButton" && t.gameObject.activeSelf)
                    {
                        above = t;
                        break;
                    }
                }

                // Renamed? The last switched-on BUTTON above Options stands in
                // for it - never the Spacer, which has no Button to be.
                if (above == null)
                {
                    for (int i = wrapper.GetSiblingIndex() - 1; i >= 0; i--)
                    {
                        Transform t = parent.GetChild(i);

                        if (t != null && t.gameObject.activeSelf &&
                            t.GetComponentInChildren<Button>(true) != null)
                        {
                            above = t;
                            break;
                        }
                    }
                }

                if (above == null)
                    return;

                var mine = wrapper as RectTransform;
                var theirs = above as RectTransform;

                if (mine != null && theirs != null &&
                    Mathf.Abs(mine.sizeDelta.x - theirs.sizeDelta.x) > 0.5f)
                {
                    mine.sizeDelta = new Vector2(theirs.sizeDelta.x, mine.sizeDelta.y);
                }

                int want = above.GetSiblingIndex() + 1;

                if (wrapper.GetSiblingIndex() != want)
                    wrapper.SetSiblingIndex(want);

                Log.LogInfo(
                    "Options sits straight under \"" + above.name + "\", " +
                    (theirs != null ? theirs.sizeDelta.x.ToString("0") : "?") +
                    " wide like it.");
            }
            catch (Exception ex)
            {
                Log.LogWarning(
                    "could not line the Options button up with Save and quit (" +
                    ex.GetType().Name + ") - it stays where the game put it.");
            }
        }

        // The options screen a button's persistent onClick opens, or null.
        private static OptionsScreen OpensOptions(UnityEvent evt)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentListenerState(i) == UnityEventCallState.Off)
                    continue;

                OptionsScreen target = evt.GetPersistentTarget(i) as OptionsScreen;

                if (target != null && evt.GetPersistentMethodName(i) == "Open")
                    return target;
            }

            return null;
        }

        // ---- 2. Esc / B closes Options, not the pause --------------------

        // Prefix on PauseScreen.Update. False skips it - the pause's back and
        // pause keys - for as long as our Options screen is up, which is the
        // main menu's own guard. Also covers the half-second the screen stays
        // active while it animates closed, so a second press cannot land on
        // the pause mid-animation.
        //
        // Every frame, so it only compares - the measuring is done at Open.
        //
        // ⚠ It must never throw: a throw here is a throw out of the game's
        // own Update, every frame, and the pause menu stops working. Any
        // doubt lets the game's Update run as it always did.
        internal static bool LetPauseUpdate(PauseScreen pause)
        {
            try
            {
                Link link = _link;

                if (link == null || !link.ours || pause == null ||
                    link.pauseId != pause.GetInstanceID())
                    return true;

                OptionsScreen options = link.options;

                return options == null || !options.isActiveAndEnabled;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // ---- 3. the pause buttons are locked while Options is open -------

        // Postfix on OptionsScreen.Open.
        internal static void OnOptionsOpened(OptionsScreen screen)
        {
            try
            {
                Link link = _link;

                // Already locked: Open can be called again on an open screen,
                // and re-scanning then would find every button already off,
                // remember none, and never put them back.
                if (link == null || !link.ours || link.muting ||
                    link.options != screen || link.pause == null)
                    return;

                link.muted.Clear();

                foreach (Selectable s in link.pause.GetComponentsInChildren<Selectable>())
                {
                    if (s != null && s.enabled)
                    {
                        s.enabled = false;
                        link.muted.Add(s);
                    }
                }

                link.muting = true;

                // Nothing behind the Options screen stays selected, so a
                // Submit has no hidden target at all.
                EventSystem es = EventSystem.current;

                if (es != null)
                    es.SetSelectedGameObject(null);
            }
            catch (Exception ex)
            {
                Log.LogError("could not lock the pause buttons behind Options: " + ex);
            }
        }

        // Postfix on OptionsScreen.OnDisable - the end of its close animation.
        internal static void OnOptionsClosed(OptionsScreen screen)
        {
            try
            {
                Link link = _link;

                if (link == null || !link.muting || link.options != screen)
                    return;

                link.muting = false;

                // Only put back what WE switched off, and only into a pause
                // menu that is still open. If the pause has closed (or the
                // scene is unloading) the game's own UIScreen owns the buttons
                // and has already switched them off.
                bool open = PauseIsOpen(link.pause);

                if (open)
                {
                    foreach (Selectable s in link.muted)
                        if (s != null)
                            s.enabled = true;
                }

                link.muted.Clear();

                if (open && link.body != null)
                {
                    EventSystem es = EventSystem.current;

                    if (es != null)
                        es.SetSelectedGameObject(link.body);
                }
            }
            catch (Exception ex)
            {
                Log.LogError("could not unlock the pause buttons after Options: " + ex);
            }
        }

        private static bool PauseIsOpen(PauseScreen pause)
        {
            if (pause == null)
                return false;

            // The field is the game's; if it moves, assume open - the pause
            // cannot have closed under Options, because its Update (the only
            // thing that closes it besides its own buttons, which were locked)
            // was skipped the whole time.
            if (IsOpenField == null)
                return true;

            try
            {
                return (bool)IsOpenField.GetValue(pause);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PauseScreen), "Open")]
    internal static class PauseOpenPatch
    {
        static void Prefix(PauseScreen __instance)
        {
            PauseMenuOptions.OnPauseOpening(__instance);
        }
    }

    [HarmonyPatch(typeof(PauseScreen), "Update")]
    internal static class PauseUpdatePatch
    {
        static bool Prefix(PauseScreen __instance)
        {
            return PauseMenuOptions.LetPauseUpdate(__instance);
        }
    }

    [HarmonyPatch(typeof(OptionsScreen), "Open")]
    internal static class OptionsOpenPatch
    {
        static void Postfix(OptionsScreen __instance)
        {
            PauseMenuOptions.OnOptionsOpened(__instance);
        }
    }

    [HarmonyPatch(typeof(OptionsScreen), "OnDisable")]
    internal static class OptionsClosedPatch
    {
        static void Postfix(OptionsScreen __instance)
        {
            PauseMenuOptions.OnOptionsClosed(__instance);
        }
    }
}
