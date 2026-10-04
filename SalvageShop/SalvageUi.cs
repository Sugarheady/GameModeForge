using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameModeForge.SalvageShop
{
    // Small helpers for the SALVAGE panel. Every one is cosmetic or defensive,
    // and none may throw into the game's own UI code. A deliberate copy of New
    // Game Plus's NgUi where they overlap (duplicate by default: a helper is
    // not worth a dependency between two DLLs).
    internal static class SalvageUi
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
            Set(text, "text", value);
        }

        internal static void SetText(GameObject root, string value)
        {
            SetText(FirstText(root), value);
        }

        internal static void SetFontSize(Component text, float size)
        {
            Set(text, "enableAutoSizing", false);
            Set(text, "fontSize", size);
        }

        // Let a caption shrink to fit instead of spilling out of its box.
        internal static void Fit(Component text, float max, float min)
        {
            Set(text, "fontSizeMax", max);
            Set(text, "fontSizeMin", min);
            Set(text, "enableAutoSizing", true);
        }

        // TextAlignmentOptions is a flags pair: Left 1 / Center 2 / Right 4,
        // Middle 512. So 513 is "middle left" and 514 "middle centre" - the
        // numbers TMP itself defines, set through the enum so either TMP
        // version (with or without the split horizontal property) takes it.
        internal static void Align(Component text, bool left)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo p = Tmp.GetProperty("alignment");

                if (p != null && p.PropertyType.IsEnum)
                    p.SetValue(text, Enum.ToObject(p.PropertyType, left ? 513 : 514), null);
            }
            catch (Exception)
            {
            }
        }

        // One line, cut with "..." rather than wrapping out of its box.
        internal static void OneLine(Component text)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo wrap = Tmp.GetProperty("enableWordWrapping");

                if (wrap != null && wrap.CanWrite)
                    wrap.SetValue(text, false, null);

                PropertyInfo over = Tmp.GetProperty("overflowMode");

                if (over != null && over.PropertyType.IsEnum)
                    over.SetValue(text, Enum.ToObject(over.PropertyType, 1), null);   // Ellipsis
            }
            catch (Exception)
            {
            }
        }

        internal static void Wrap(Component text)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo wrap = Tmp.GetProperty("enableWordWrapping");

                if (wrap != null && wrap.CanWrite)
                    wrap.SetValue(text, true, null);
            }
            catch (Exception)
            {
            }
        }

        internal static void RichText(Component text)
        {
            Set(text, "richText", true);
        }

        private static void Set(Component text, string property, object value)
        {
            if (text == null || Tmp == null)
                return;

            try
            {
                PropertyInfo p = Tmp.GetProperty(property);

                if (p != null && p.CanWrite)
                    p.SetValue(text, value, null);
            }
            catch (Exception)
            {
            }
        }

        // TMP_Text is a Graphic, so colour and raycasting need no reflection.
        internal static void SetRaycast(Component c, bool on)
        {
            var g = c as Graphic;

            if (g != null)
                g.raycastTarget = on;
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
        // (Awake, OnEnable) until it is placed - the settings-tab rule. The
        // clone keeps its own active flag, so it wakes when it is reparented.
        internal static GameObject Clone(GameObject source, Transform parent, string name)
        {
            var holder = new GameObject("SalvageCloneHolder");
            holder.SetActive(false);

            GameObject clone = UnityEngine.Object.Instantiate(source, holder.transform, false);
            clone.name = name;
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

        internal static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent != null ? parent.gameObject.layer : go.layer;
            return (RectTransform)go.transform;
        }

        // Top-left anchored box, `x`/`y` its top-left corner below the parent's
        // top-left, so a panel can be laid out like a page.
        internal static void Box(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // Stretched across the parent, inset by `left`/`right`, `y` down from
        // the top and `h` tall.
        internal static void Row(RectTransform rt, float left, float right, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -y - h);
            rt.offsetMax = new Vector2(-right, -y);
        }

        internal static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.white;
        }
    }
}
