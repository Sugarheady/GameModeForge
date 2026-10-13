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
                Tmp.GetProperty("text").SetValue(text, Drawable(text, value), null);
            }
            catch (Exception)
            {
            }
        }

        // ★ A FONT WITH NO "%" (R21 part 2, 307: "for some reason the % is
        // like double the size of the font used for that status text").
        // Measured off the font assets: `Font_Minimum` - the victory screen's,
        // the game-over stats', the confirm popup's - has no %, # &, < >, @,
        // [ ], ^, `, { }, ~, and no fallback font of its own, so TextMeshPro
        // draws a missing glyph from its global fallback at that font's own
        // size. The "%" is written as a word instead wherever the font cannot
        // draw it. (`8-bit-hud SDF`, the options rows' font, has every one.)
        private static readonly Dictionary<object, bool> _hasPercent = new Dictionary<object, bool>();
        private static readonly HashSet<string> _saidNoGlyph = new HashSet<string>();

        internal static string Drawable(Component text, string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0 || Lacks(text, '%') != true)
                return value;

            return NgMath.PercentAsWord(value);
        }

        // true: the text's font cannot draw `c`; false: it can; null: unknown
        // (then the text is left exactly as it was).
        private static bool? Lacks(Component text, char c)
        {
            try
            {
                object font = Tmp.GetProperty("font").GetValue(text, null);

                if (font == null)
                    return null;

                bool has;

                if (c == '%' && _hasPercent.TryGetValue(font, out has))
                    return !has;

                MethodInfo m = font.GetType().GetMethod("HasCharacter", new[] { typeof(int) });

                if (m == null)
                    return null;

                has = (bool)m.Invoke(font, new object[] { (int)c });

                if (c == '%')
                    _hasPercent[font] = has;

                var named = font as UnityEngine.Object;
                string name = named != null ? named.name : font.ToString();

                if (!has && _saidNoGlyph.Add(name + c))
                {
                    NewGamePlusPlugin.Log.LogInfo(
                        "the font '" + name + "' has no '" + c + "', so New Game Plus writes it as a " +
                        "word in that font's texts (TextMeshPro would draw it from another font, " +
                        "at the wrong size).");
                }

                return !has;
            }
            catch (Exception)
            {
                return null;
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
                // The size too, so a text never stays at its stock size if
                // auto-sizing does not take (it overrides this when it does).
                Tmp.GetProperty("fontSize").SetValue(text, max, null);
                Tmp.GetProperty("fontSizeMax").SetValue(text, max, null);
                Tmp.GetProperty("fontSizeMin").SetValue(text, min, null);
                Tmp.GetProperty("enableAutoSizing").SetValue(text, true, null);
            }
            catch (Exception)
            {
            }
        }

        // What a fitted text came out at, said each time it is filled: its
        // size after auto-sizing, its lines, and whether it still overflows
        // its box. R21 307 could not say whether auto-sizing had taken; the
        // next run can. Diagnostic only - never throws.
        internal static void SayFit(Component text, string what, float width, float height)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                MethodInfo force = Tmp.GetMethod("ForceMeshUpdate", new[] { typeof(bool), typeof(bool) });

                if (force != null)
                    force.Invoke(text, new object[] { true, true });

                object size = Tmp.GetProperty("fontSize").GetValue(text, null);
                PropertyInfo overP = Tmp.GetProperty("isTextOverflowing");
                object over = overP != null ? overP.GetValue(text, null) : null;
                PropertyInfo infoP = Tmp.GetProperty("textInfo");
                object info = infoP != null ? infoP.GetValue(text, null) : null;
                FieldInfo linesF = info != null ? info.GetType().GetField("lineCount") : null;
                object lines = linesF != null ? linesF.GetValue(info) : null;
                var inv = System.Globalization.CultureInfo.InvariantCulture;

                NewGamePlusPlugin.Log.LogInfo(
                    what + " came out at size " +
                    (size is float ? ((float)size).ToString("0.#", inv) : "?") +
                    (lines != null ? ", " + lines + " line(s)," : "") +
                    " in its " + width.ToString("0", inv) + " x " + height.ToString("0", inv) + " box" +
                    (over is bool && (bool)over ? " - and it STILL OVERFLOWS the box" : "") + ".");
            }
            catch (Exception)
            {
                // A report must never cost the screen.
            }
        }

        // Read from the top-left corner (TextAlignmentOptions.TopLeft = 257:
        // Left 1 | Top 256), set by number because the enum is TextMeshPro's.
        internal static void AlignTopLeft(Component text)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo align = Tmp.GetProperty("alignment");

                if (align != null && align.CanWrite && align.PropertyType.IsEnum)
                    align.SetValue(text, Enum.ToObject(align.PropertyType, 257), null);
            }
            catch (Exception)
            {
            }
        }

        // The text's colour (a Graphic's, which TextMeshPro is).
        internal static void Colour(Component text, Color colour)
        {
            try
            {
                var g = text as Graphic;

                if (g != null)
                    g.color = colour;
            }
            catch (Exception)
            {
            }
        }

        // Wrapping ON, under whichever name this TextMeshPro has for it:
        // `textWrappingMode` in the version the game ships (Normal = 1),
        // `enableWordWrapping` before it.
        internal static void Wrap(Component text)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo mode = Tmp.GetProperty("textWrappingMode");

                if (mode != null && mode.CanWrite && mode.PropertyType.IsEnum)
                {
                    mode.SetValue(text, Enum.ToObject(mode.PropertyType, 1), null);
                    return;
                }

                PropertyInfo wrap = Tmp.GetProperty("enableWordWrapping");

                if (wrap != null && wrap.CanWrite)
                    wrap.SetValue(text, true, null);
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
        private static readonly FieldInfo PromptCanvas = AccessTools.Field(typeof(Prompt), "canvas");

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

                // ★ NOT NOW - ONCE IT HAS FADED OUT (R21 307). `Close` only
                // STARTS the popup's close animation. Putting the game's own
                // title back here swapped "End this run?" for the pause
                // Restart's question while the popup was still on screen, so
                // pressing Yes looked like a SECOND prompt opening - "another
                // prompt came up but like 1 second later i was in the loadout
                // window". The title goes back once the canvas is off; ending
                // the run loads a new scene first, and the popup goes with it.
                if (original != null)
                    RestoreWhenClosed(prompt, title, question, original);
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

        private static void RestoreWhenClosed(Prompt prompt, Component title, string ours, string original)
        {
            try
            {
                prompt.StartCoroutine(RestoreLater(prompt, title, ours, original));
            }
            catch (Exception)
            {
                // No coroutine (the popup is inactive): put it back now.
                SetText(title, original);
            }
        }

        private static System.Collections.IEnumerator RestoreLater(
            Prompt prompt, Component title, string ours, string original)
        {
            // `Prompt.AnimateClose` switches the canvas off as its last step.
            // Capped, so a canvas that never goes off cannot keep our words on
            // the game's popup.
            float until = Time.unscaledTime + 3f;

            while (Time.unscaledTime < until)
            {
                var canvas = PromptCanvas != null ? PromptCanvas.GetValue(prompt) as Canvas : null;

                if (canvas == null || !canvas.enabled)
                    break;

                yield return null;
            }

            // Only if it is still OUR question: another Confirm may have put
            // its own on the popup in the meantime.
            if (title != null && GetText(title) == ours)
                SetText(title, original);
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
