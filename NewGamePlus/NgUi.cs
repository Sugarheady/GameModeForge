using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameModeForge.NewGamePlus
{
    // Small helpers the three screens share. Every one of them is cosmetic or
    // defensive, and none may throw into the game's own UI code.
    internal static class NgUi
    {
        // ---- TextMeshPro, by reflection (the house idiom) -----------------

        private static Type _tmp;
        private static bool _lookedForTmp;

        private static Type Tmp
        {
            get
            {
                // Latch on success only; a failed look is retried.
                if (_tmp == null && !_lookedForTmp)
                {
                    _tmp = FindTypeQuietly("TMPro.TMP_Text");
                    _lookedForTmp = _tmp != null;
                }

                return _tmp;
            }
        }

        // AccessTools.TypeByName logs a warning for an absent type; scanning
        // the loaded assemblies does not.
        internal static Type FindTypeQuietly(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullName, false);

                    if (t != null)
                        return t;
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        // The first text under `root` (inactive children included).
        internal static Component FirstText(GameObject root)
        {
            if (root == null || Tmp == null)
                return null;

            Component[] found = root.GetComponentsInChildren(Tmp, true);
            return found.Length > 0 ? found[0] : null;
        }

        internal static void SetText(Component text, string value)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                Tmp.GetProperty("text").SetValue(text, value, null);
            }
            catch (Exception)
            {
            }
        }

        internal static string GetText(Component text)
        {
            if (text == null || Tmp == null)
                return null;

            try
            {
                return Tmp.GetProperty("text").GetValue(text, null) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void SetText(GameObject root, string value)
        {
            SetText(FirstText(root), value);
        }

        // Let a caption shrink to fit instead of spilling out of its box.
        internal static void Fit(Component text, float max, float min)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                Tmp.GetProperty("fontSizeMax").SetValue(text, max, null);
                Tmp.GetProperty("fontSizeMin").SetValue(text, min, null);
                Tmp.GetProperty("enableAutoSizing").SetValue(text, true, null);
            }
            catch (Exception)
            {
            }
        }

        // ---- objects --------------------------------------------------------

        internal static Transform Find(Transform root, string name)
        {
            if (root == null)
                return null;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;

            return null;
        }

        // A live object of this scene (never a prefab: FindObjectsOfTypeAll
        // sees prefabs too, and opening a prefab does nothing).
        internal static T FindLive<T>() where T : Component
        {
            foreach (T c in Resources.FindObjectsOfTypeAll<T>())
            {
                if (c != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
                    return c;
            }

            return null;
        }

        // ★ CLONED UNDER AN INACTIVE HOLDER, so nothing on the copy wakes up
        // (Awake, OnEnable) until it is placed - the settings-tab rule. The
        // clone keeps its own active flag, so it wakes when it is reparented.
        internal static GameObject Clone(GameObject source, Transform parent, string name)
        {
            var holder = new GameObject("NgCloneHolder");
            holder.SetActive(false);

            GameObject clone = UnityEngine.Object.Instantiate(source, holder.transform, false);
            clone.name = name;

            return CloneFinish(clone, holder, parent);
        }

        // Used when the clone needs work done on it BEFORE it wakes: call
        // Clone-without-placing, edit it, then Place.
        internal static GameObject CloneAsleep(GameObject source, string name, out GameObject holder)
        {
            holder = new GameObject("NgCloneHolder");
            holder.SetActive(false);

            GameObject clone = UnityEngine.Object.Instantiate(source, holder.transform, false);
            clone.name = name;
            return clone;
        }

        internal static void Place(GameObject clone, GameObject holder, Transform parent)
        {
            CloneFinish(clone, holder, parent);
        }

        private static GameObject CloneFinish(GameObject clone, GameObject holder, Transform parent)
        {
            clone.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(holder);
            return clone;
        }

        // ★ A CLONED BUTTON KEEPS ITS PREFAB-WIRED onClick, and
        // RemoveAllListeners does NOT remove a persistent one. Each persistent
        // call is switched off by hand, then ours is the only listener.
        internal static void Rewire(Button button, UnityAction action)
        {
            if (button == null)
                return;

            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        // The Button of a menu button wrapper (the ButtonBody inside it).
        internal static Button ButtonOf(GameObject wrapper)
        {
            return wrapper != null ? wrapper.GetComponentInChildren<Button>(true) : null;
        }

        // ---- "are you sure?" --------------------------------------------------

        private static readonly FieldInfo PromptTitle = AccessTools.Field(typeof(Prompt), "title");

        // The game's own confirm popup (`UI/RestartConfirmPopup`, the pause
        // Restart's) with our question on it. The popup's own text is put back
        // whichever way it closes, so the pause Restart still asks its own.
        internal static bool Confirm(string question, Action yes, Action no)
        {
            Prompt prompt = FindRestartPrompt();

            if (prompt == null)
                return false;

            Component title = PromptTitle != null ? PromptTitle.GetValue(prompt) as Component : null;
            string original = GetText(title);

            SetText(title, question);

            Action restore = () =>
            {
                try
                {
                    prompt.Close();
                }
                catch (Exception)
                {
                }

                if (original != null)
                    SetText(title, original);
            };

            prompt.Open(
                () =>
                {
                    restore();
                    SafeRun(yes, "the confirmed action");
                },
                () =>
                {
                    restore();
                    SafeRun(no, "the cancel");
                });

            return true;
        }

        internal static void SafeRun(Action a, string what)
        {
            if (a == null)
                return;

            try
            {
                a();
            }
            catch (Exception ex)
            {
                NewGamePlusPlugin.Log.LogError("New Game Plus: " + what + " threw: " + ex);
            }
        }

        private static Prompt FindRestartPrompt()
        {
            // The pause menu's own field is the exact one; fall back to any
            // live prompt in the scene.
            PauseScreen pause = FindLive<PauseScreen>();

            if (pause != null)
            {
                FieldInfo f = AccessTools.Field(typeof(PauseScreen), "confirmRestartPrompt");
                Prompt p = f != null ? f.GetValue(pause) as Prompt : null;

                if (p != null)
                    return p;
            }

            return FindLive<Prompt>();
        }

        // ---- the pause menu's buttons, locked while a screen is over them ----

        // The same thing the game's UIScreen does when a screen closes, and
        // the fix Pause Options needed for its Options screen: with the
        // EventSystem still navigating and submitting on the pause buttons, a
        // gamepad could press one behind the screen on top.
        internal static List<Selectable> Mute(Component under)
        {
            var muted = new List<Selectable>();

            if (under == null)
                return muted;

            foreach (Selectable s in under.GetComponentsInChildren<Selectable>())
            {
                if (s != null && s.enabled)
                {
                    s.enabled = false;
                    muted.Add(s);
                }
            }

            return muted;
        }

        internal static void Unmute(List<Selectable> muted)
        {
            if (muted == null)
                return;

            foreach (Selectable s in muted)
                if (s != null)
                    s.enabled = true;

            muted.Clear();
        }
    }
}
