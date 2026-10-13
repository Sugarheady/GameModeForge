using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameModeForge.Leaderboard
{
    // Small helpers for the main menu's LEADERBOARD button and the board.
    // Copied from New Game Plus's NgUi (the Leaderboard stands alone), only what
    // it needs, plus the text settings a list needs. Every one is cosmetic or
    // defensive, and none may throw into the game's own UI code.
    internal static class LbUi
    {
        // ---- TextMeshPro, by reflection (the house idiom) -------------------

        private static Type _tmp;
        private static bool _lookedForTmp;

        private static Type Tmp
        {
            get
            {
                // Latch on success only; a failed look is retried.
                if (_tmp == null && !_lookedForTmp)
                {
                    _tmp = LbInterop.FindTypeQuietly("TMPro.TMP_Text");
                    _lookedForTmp = _tmp != null;
                }

                return _tmp;
            }
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
            Set(text, "text", value ?? "");
        }

        internal static void SetText(GameObject root, string value)
        {
            SetText(FirstText(root), value);
        }

        // Any TextMeshPro property by name; an enum property takes its number.
        internal static void Set(Component text, string property, object value)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo p = Tmp.GetProperty(property);

                if (p == null || !p.CanWrite)
                    return;

                if (p.PropertyType.IsEnum && value is int)
                    value = Enum.ToObject(p.PropertyType, (int)value);

                p.SetValue(text, value, null);
            }
            catch (Exception)
            {
            }
        }

        // A list's text: a fixed size (no auto-size), no wrapping (the columns
        // are <pos> tags), never clipped by its own box, rich text on.
        internal static void ListStyle(Component text, float size, int alignment)
        {
            Set(text, "enableAutoSizing", false);
            Set(text, "fontSize", size);
            Set(text, "alignment", alignment);
            Set(text, "richText", true);

            // `textWrappingMode` in the version the game ships (NoWrap = 0),
            // `enableWordWrapping` before it.
            if (Tmp != null && Tmp.GetProperty("textWrappingMode") != null)
                Set(text, "textWrappingMode", 0);
            else
                Set(text, "enableWordWrapping", false);

            Set(text, "overflowMode", 0);   // TextOverflowModes.Overflow

            var g = text as Graphic;

            if (g != null)
                g.raycastTarget = false;     // the scroll area under it takes the wheel
        }

        // TextAlignmentOptions, by number (the enum is TextMeshPro's).
        internal const int TopLeft = 257;    // Left 1 | Top 256
        internal const int Centre = 514;     // Center 2 | Middle 512

        internal static void Colour(Component text, Color colour)
        {
            var g = text as Graphic;

            if (g != null)
                g.color = colour;
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

        // ★ CLONED UNDER AN INACTIVE HOLDER, so nothing on the copy wakes up
        // (Awake, OnEnable) until it is placed - the settings-tab rule.
        internal static GameObject CloneAsleep(GameObject source, string name, out GameObject holder)
        {
            holder = new GameObject("LbCloneHolder");
            holder.SetActive(false);

            GameObject clone = UnityEngine.Object.Instantiate(source, holder.transform, false);
            clone.name = name;
            return clone;
        }

        internal static void Place(GameObject clone, GameObject holder, Transform parent)
        {
            clone.transform.SetParent(parent, false);
            UnityEngine.Object.Destroy(holder);
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

        // A plain RectTransform child.
        internal static RectTransform Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        // Anchored to the top centre of its parent, `y` down from the top.
        internal static void TopCentre(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(width, height);
        }

        internal static Image Panel(Transform parent, string name, Color colour)
        {
            RectTransform rt = Child(parent, name);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = colour;
            return img;
        }
    }
}
