using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameModeForge.Leaderboard
{
    // THE BOARD, IN THE MAIN MENU (CONCEPT.md 5.1, answer 3: "Main menu button" -
    // a LEADERBOARD button in the bottom row, beside SETTINGS; nothing on the
    // game-over screen, nothing after END RUN, no HTML page).
    //
    // ★ THE GAME'S OWN BOARD IS NOT THERE TO REUSE (measured 2026-10-12): its
    // screen is a scene (HighscoresScreen) that is not in the build, and the
    // main menu's "view highscores" button sits in an inactive footer and calls
    // a MainMenu.ShowLeaderboard that no longer exists. So the board is built
    // here, from the main menu's own parts:
    //
    //   - the button is a copy of SETTINGS (`Footer v2/BasicButton Settings`,
    //     measured: a HorizontalLayoutGroup row that does not size its
    //     children, explicit navigation - SETTINGS goes up and left, nothing to
    //     its right). The copy goes right of it, the row widens to fit four, and
    //     the navigation is threaded SETTINGS <-> LEADERBOARD.
    //   - ★ IT ANIMATES WITH ITS NEIGHBOURS BY FOLLOWING SETTINGS. Each footer
    //     button is an AnimatedScreenElement whose `Visible` flag the main
    //     menu's AnimatedScreen sets - from a list it collects ONCE, in Awake,
    //     and an element under an AnimatedScreen never shows itself. So the copy
    //     mirrors SETTINGS' `Visible` every frame (LbFollowVisible): in when the
    //     row comes in, out when it goes, whatever order things woke in.
    //   - the board is a canvas of its own over the menu: a near-solid backdrop,
    //     a title, a ScrollRect of rows (the wheel), a Scrollbar (a pad scrolls
    //     with up / down while it is selected - Unity's Scrollbar moves itself
    //     when no selectable lies that way) and BACK - a copy of SETTINGS' own
    //     body, which shows selection by its colour tint, no animator needed.
    //
    // ★ THREE THINGS THE MAIN MENU WOULD DO TO A SCREEN OVER IT, EACH HANDLED:
    //   1. Esc opens "quit the game?" (MainMenu.Update checks only the Options
    //      screen) - Pause Options' bug 1 again. -> MainMenu.Update is skipped
    //      while the board is up and for a moment after; the board reads the
    //      menu's own exitAction itself to close.
    //   2. The game's UiManager pulls a pad's selection back to the TOP
    //      DefaultSelector's default whenever the stick moves on something that
    //      is not its child. -> the board carries a DefaultSelector of its own:
    //      pushed when it opens, popped when it closes, the game's own system.
    //   3. A pad could navigate to a button behind. -> both of the board's
    //      selectables have explicit navigation to each other only.
    internal static class LbMenu
    {
        private const string DonorName = "BasicButton Settings";
        internal const string ButtonName = "BasicButton Leaderboard";

        // Above the main menu's canvas (1000), below Options (10000).
        private const int SortingOrder = 1500;

        private const float BackdropAlpha = 0.96f;

        // The rows' box, in the menu canvas's reference units (1920 x 1300,
        // matched by height - measured off MainMenu.unity).
        private const float AreaWidth = 1560f;
        private const float AreaTop = 290f;
        private const float AreaHeight = 800f;

        private static readonly Color Grey = new Color(0.72f, 0.72f, 0.72f, 1f);

        // The footer buttons' own selected colour (measured: the SETTINGS body's
        // ColorTint).
        private static readonly Color Orange = new Color(0.894f, 0.490f, 0.118f, 1f);

        private static readonly FieldInfo ExitAction = AccessTools.Field(typeof(MainMenu), "exitAction");

        private static MainMenu _menu;
        private static GameObject _footerButton;
        private static Button _footerBody;
        private static GameObject _donorBody;

        private static GameObject _board;
        private static Component _subtitle, _body;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static Scrollbar _bar;
        private static Button _back;

        private static bool _open;
        private static float _closedAt = -10f;
        private static bool _saidNoDonor;

        internal static bool IsOpen
        {
            get { return _open; }
        }

        // Open, or closed less than 0.4 s ago (the Esc that closed it).
        internal static bool Blocks
        {
            get { return _open || Time.unscaledTime - _closedAt < 0.4f; }
        }

        // ---- the button ------------------------------------------------------------

        // MainMenu.Awake, postfix. Every visit to the main menu is a new scene,
        // so everything built for the last one went with it.
        internal static void OnMainMenu(MainMenu menu)
        {
            _menu = menu;
            _footerButton = null;
            _footerBody = null;
            _donorBody = null;
            _board = null;
            _open = false;
            _closedAt = -10f;

            if (menu == null)
                return;

            Transform donor = LbUi.Find(menu.transform, DonorName);

            if (donor == null)
            {
                if (!_saidNoDonor)
                {
                    _saidNoDonor = true;
                    LeaderboardPlugin.Log.LogWarning(
                        "the main menu has no SETTINGS button (" + DonorName + ") - it has changed, so the " +
                        "LEADERBOARD button is not added. Runs still go on the board's file (BepInEx\\config\\" +
                        LbStore.FileName + "), and each one is in this log.");
                }

                return;
            }

            if (LbUi.Find(menu.transform, ButtonName) != null)
                return;   // never two

            Button settings = LbUi.ButtonOf(donor.gameObject);

            GameObject holder;
            GameObject clone = LbUi.CloneAsleep(donor.gameObject, ButtonName, out holder);

            try
            {
                Button body = LbUi.ButtonOf(clone);

                if (body == null || settings == null)
                    throw new InvalidOperationException("the SETTINGS button has no Button inside it");

                body.gameObject.name = "ButtonBody Leaderboard";
                LbUi.Rewire(body, Open);
                LbUi.SetText(clone, "LEADERBOARD");

                LbFollowVisible follow = clone.AddComponent<LbFollowVisible>();
                follow.donor = donor.GetComponent<Animator>();

                LbUi.Place(clone, holder, donor.parent);
                holder = null;

                clone.transform.SetSiblingIndex(donor.GetSiblingIndex() + 1);

                Thread(settings, body);
                Widen(donor.parent as RectTransform);

                _footerButton = clone;
                _footerBody = body;
                _donorBody = settings.gameObject;

                LeaderboardPlugin.Log.LogInfo("the main menu has a LEADERBOARD button, beside SETTINGS.");
            }
            catch (Exception ex)
            {
                if (clone != null)
                    UnityEngine.Object.Destroy(clone);

                if (holder != null)
                    UnityEngine.Object.Destroy(holder);

                LeaderboardPlugin.Log.LogError("could not add the LEADERBOARD button: " + ex);
            }
        }

        // ★ EXPLICIT NAVIGATION (measured: mode 4 - SETTINGS goes up and left,
        // nothing to its right). LEADERBOARD takes SETTINGS' up, and the two
        // point at each other.
        private static void Thread(Button settings, Button ours)
        {
            Navigation s = settings.navigation;

            var o = new Navigation();
            o.mode = Navigation.Mode.Explicit;
            o.selectOnLeft = settings;
            o.selectOnUp = s.selectOnUp;
            ours.navigation = o;

            if (s.mode == Navigation.Mode.Explicit)
            {
                s.selectOnRight = ours;
                settings.navigation = s;
            }
        }

        // The row lays its buttons out but does not size them (measured), so four
        // need a wider row, or the outer two hang past its rect.
        private static void Widen(RectTransform row)
        {
            if (row == null)
                return;

            HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
            float total = 0f;
            int n = 0;

            foreach (Transform c in row)
            {
                var rt = c as RectTransform;

                if (rt == null || !c.gameObject.activeSelf)
                    continue;

                total += rt.sizeDelta.x;
                n++;
            }

            if (h != null)
                total += h.spacing * Math.Max(0, n - 1) + h.padding.left + h.padding.right;

            if (total > row.sizeDelta.x)
                row.sizeDelta = new Vector2(total, row.sizeDelta.y);
        }

        // ---- the board -------------------------------------------------------------

        internal static void Open()
        {
            try
            {
                if (_open)
                    return;

                if (!Build())
                {
                    LeaderboardPlugin.Log.LogWarning(
                        "the board could not be built - every run on it is in BepInEx\\config\\" + LbStore.FileName + ".");
                    return;
                }

                int runs = Fill();

                // The board's DefaultSelector pushes itself as it wakes (and, on a
                // pad, selects the scrollbar).
                _open = true;
                _board.SetActive(true);

                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                _scroll.verticalNormalizedPosition = 1f;

                LeaderboardPlugin.Log.LogInfo("the board is open (" + runs + " run(s)).");
            }
            catch (Exception ex)
            {
                _open = false;

                try
                {
                    if (_board != null)
                        _board.SetActive(false);
                }
                catch (Exception)
                {
                }

                LeaderboardPlugin.Log.LogError("the board failed to open: " + ex);
            }
        }

        internal static void Close()
        {
            if (!_open)
                return;

            _open = false;
            _closedAt = Time.unscaledTime;

            EventSystem es = EventSystem.current;
            bool selecting = es != null && es.currentSelectedGameObject != null;

            try
            {
                // Its DefaultSelector pops, and the game's own selector takes the
                // pad back.
                _board.SetActive(false);
            }
            catch (Exception)
            {
            }

            // ...onto the button that opened it, when a pad was in use.
            try
            {
                if (selecting && es != null && _footerBody != null && _footerBody.isActiveAndEnabled)
                    es.SetSelectedGameObject(_footerBody.gameObject);
            }
            catch (Exception)
            {
            }
        }

        // The board went away without Close (its scene unloading).
        internal static void OnBoardGone()
        {
            if (_open)
            {
                _open = false;
                _closedAt = Time.unscaledTime;
            }
        }

        // MainMenu.Update, prefix. False skips the menu's own Update - the one
        // whose Esc opens "quit the game?".
        internal static bool LetMenuUpdate(MainMenu menu)
        {
            if (!Blocks)
                return true;

            if (_open && ExitPressed(menu))
                Close();

            return false;
        }

        private static bool ExitPressed(MainMenu menu)
        {
            try
            {
                var a = ExitAction != null ? ExitAction.GetValue(menu) as InputAction : null;
                return a != null && a.WasPerformedThisFrame();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int Fill()
        {
            int skipped;
            string problem;
            List<LbEntry> runs = LbStore.ReadFile(LbStore.FilePath, out skipped, out problem);
            List<LbEntry> ranked = LbMath.Ranked(runs ?? new List<LbEntry>());

            string sub = LbMath.Subtitle(ranked.Count, LbSettings.Recording);

            if (problem != null)
            {
                sub += "\nThe board's file could not be read (" + LbMath.Clean(problem, 80) + ") - it is left as it " +
                       "is; the next finished run keeps it aside, untouched, and starts a new board.";
            }
            else if (skipped > 0)
            {
                sub += "\n" + skipped + " row(s) in the board's file would not read - the next finished run keeps " +
                       "the file aside, untouched.";
            }

            LbUi.SetText(_subtitle, sub);
            LbUi.SetText(_body, LbMath.Board(ranked));

            return ranked.Count;
        }

        private static bool Build()
        {
            if (_board != null)
                return true;

            if (_menu == null || _footerButton == null || _donorBody == null)
                return false;

            // The footer caption: the main menu's own font (Font_Minimum).
            Component font = LbUi.FirstText(_footerButton);

            if (font == null)
                return false;

            var root = new GameObject("LbBoard", typeof(RectTransform));
            root.SetActive(false);

            try
            {
                var rt = (RectTransform)root.transform;
                rt.SetParent(_menu.transform, false);
                LbUi.Stretch(rt);
                rt.SetAsLastSibling();

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
                root.AddComponent<GraphicRaycaster>();

                // Near solid, and it takes every click meant for the menu behind.
                LbUi.Stretch(LbUi.Panel(rt, "Backdrop", new Color(0f, 0f, 0f, BackdropAlpha)).rectTransform);

                Component title = Text(font, rt, "Title", 64f, LbUi.Centre, Color.white);
                LbUi.TopCentre((RectTransform)title.transform, 0f, 50f, 1600f, 90f);
                LbUi.SetText(title, "LEADERBOARD");

                _subtitle = Text(font, rt, "Subtitle", 24f, LbUi.Centre, Grey);
                LbUi.TopCentre((RectTransform)_subtitle.transform, 0f, 140f, 1600f, 90f);

                Component header = Text(font, rt, "Header", 26f, LbUi.TopLeft, Orange);
                LbUi.TopCentre((RectTransform)header.transform, -20f, AreaTop - 46f, AreaWidth, 40f);
                LbUi.SetText(header, LbMath.Header());

                // The scroll area: a clear Image takes the wheel, the viewport
                // clips, the rows are one text that grows to fit.
                Image area = LbUi.Panel(rt, "Scroll", new Color(0f, 0f, 0f, 0f));
                LbUi.TopCentre(area.rectTransform, -20f, AreaTop, AreaWidth, AreaHeight);

                RectTransform viewport = LbUi.Child(area.rectTransform, "Viewport");
                LbUi.Stretch(viewport);
                viewport.gameObject.AddComponent<RectMask2D>();

                _body = Text(font, viewport, "Rows", 26f, LbUi.TopLeft, Color.white);
                _content = (RectTransform)_body.transform;
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.anchoredPosition = Vector2.zero;
                _content.sizeDelta = Vector2.zero;

                ContentSizeFitter fit = _body.gameObject.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                _bar = MakeScrollbar(rt);

                _scroll = area.gameObject.AddComponent<ScrollRect>();
                _scroll.viewport = viewport;
                _scroll.content = _content;
                _scroll.horizontal = false;
                _scroll.vertical = true;
                _scroll.movementType = ScrollRect.MovementType.Clamped;
                _scroll.inertia = false;
                _scroll.scrollSensitivity = 40f;
                _scroll.verticalScrollbar = _bar;
                _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

                _back = MakeBack(rt);

                // ★ Each of the two points only at the other (thing 3 above).
                // The scrollbar has nothing above or below it, so up / down move
                // it instead.
                var bn = new Navigation();
                bn.mode = Navigation.Mode.Explicit;
                bn.selectOnLeft = _back;
                bn.selectOnRight = _back;
                _bar.navigation = bn;

                var kn = new Navigation();
                kn.mode = Navigation.Mode.Explicit;
                kn.selectOnUp = _bar;
                kn.selectOnLeft = _bar;
                kn.selectOnRight = _bar;
                _back.navigation = kn;

                _bar.gameObject.AddComponent<LbCancel>();
                _back.gameObject.AddComponent<LbCancel>();

                // ★ Added while the board is still asleep: its Awake and OnEnable
                // (the push) wait until it opens, by which time it knows its
                // default.
                DefaultSelector selector = root.AddComponent<DefaultSelector>();
                selector.SetDefaultSelection(_bar.gameObject);

                root.AddComponent<LbBoardScreen>();

                _board = root;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Object.Destroy(root);
                LeaderboardPlugin.Log.LogError("could not build the board: " + ex);
                return false;
            }
        }

        // A copy of the footer caption (font, material), set out for a list.
        private static Component Text(Component font, Transform parent, string name, float size, int align, Color colour)
        {
            GameObject go = UnityEngine.Object.Instantiate(font.gameObject, parent, false);
            go.name = name;

            Component t = LbUi.FirstText(go);

            if (t == null)
                throw new InvalidOperationException("the copied caption has no text");

            LbUi.ListStyle(t, size, align);
            LbUi.Set(t, "margin", Vector4.zero);
            LbUi.Colour(t, colour);
            return t;
        }

        private static Scrollbar MakeScrollbar(RectTransform parent)
        {
            Image track = LbUi.Panel(parent, "Scrollbar", new Color(1f, 1f, 1f, 0.10f));
            LbUi.TopCentre(track.rectTransform, AreaWidth / 2f + 10f, AreaTop, 24f, AreaHeight);

            RectTransform sliding = LbUi.Child(track.rectTransform, "Sliding Area");
            LbUi.Stretch(sliding);

            Image handle = LbUi.Panel(sliding, "Handle", Color.white);
            LbUi.Stretch(handle.rectTransform);

            Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.targetGraphic = handle;
            bar.transition = Selectable.Transition.ColorTint;

            ColorBlock c = bar.colors;
            c.normalColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            c.highlightedColor = Orange;
            c.selectedColor = Orange;
            c.pressedColor = Color.white;
            c.colorMultiplier = 1f;
            bar.colors = c;

            return bar;
        }

        // A copy of SETTINGS' own body (Image + Button + its sounds), which shows
        // selection with its colour tint - no animator comes with it.
        private static Button MakeBack(RectTransform parent)
        {
            RectTransform holder = LbUi.Child(parent, "Back");
            holder.anchorMin = new Vector2(0.5f, 0f);
            holder.anchorMax = new Vector2(0.5f, 0f);
            holder.pivot = new Vector2(0.5f, 0f);
            holder.anchoredPosition = new Vector2(0f, 80f);

            var donorRect = _footerButton.transform as RectTransform;
            holder.sizeDelta = donorRect != null ? donorRect.sizeDelta : new Vector2(347f, 74f);

            GameObject body = UnityEngine.Object.Instantiate(_donorBody, holder, false);
            body.name = "ButtonBody Back";
            LbUi.Stretch((RectTransform)body.transform);

            Button b = body.GetComponent<Button>();

            if (b == null)
                throw new InvalidOperationException("the copied button body has no Button");

            LbUi.Rewire(b, Close);
            LbUi.SetText(body, "BACK");
            return b;
        }
    }

    // ★ The copy follows SETTINGS' `Visible` (see LbMenu's header). If the two
    // animators cannot be read, the copy simply shows itself once.
    internal sealed class LbFollowVisible : MonoBehaviour
    {
        private const string Visible = "Visible";

        public Animator donor;

        private Animator _mine;
        private bool _follow;

        private void Start()
        {
            try
            {
                _mine = GetComponent<Animator>();
                _follow = Has(_mine) && Has(donor);

                if (!_follow)
                {
                    AnimatedScreenElement el = GetComponent<AnimatedScreenElement>();

                    if (el != null)
                        el.Show();
                }
            }
            catch (Exception)
            {
                _follow = false;
            }
        }

        private static bool Has(Animator a)
        {
            if (a == null)
                return false;

            foreach (AnimatorControllerParameter p in a.parameters)
                if (p.name == Visible && p.type == AnimatorControllerParameterType.Bool)
                    return true;

            return false;
        }

        private void Update()
        {
            if (!_follow)
                return;

            try
            {
                if (donor == null || _mine == null)
                {
                    _follow = false;
                    return;
                }

                bool v = donor.GetBool(Visible);

                if (_mine.GetBool(Visible) != v)
                    _mine.SetBool(Visible, v);
            }
            catch (Exception)
            {
                _follow = false;   // never out of an Update
            }
        }
    }

    // B on a pad, Esc on a keyboard (the UI module's Cancel) - closes the board.
    internal sealed class LbCancel : MonoBehaviour, ICancelHandler
    {
        public void OnCancel(BaseEventData eventData)
        {
            try
            {
                LbMenu.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    internal sealed class LbBoardScreen : MonoBehaviour
    {
        private void OnDisable()
        {
            try
            {
                LbMenu.OnBoardGone();
            }
            catch (Exception)
            {
            }
        }
    }

    // ---- the patches ------------------------------------------------------------

    [HarmonyPatch(typeof(MainMenu), "Awake")]
    internal static class LbMainMenuAwakePatch
    {
        static void Postfix(MainMenu __instance)
        {
            try
            {
                LbMenu.OnMainMenu(__instance);
            }
            catch (Exception ex)
            {
                LeaderboardPlugin.Log.LogError("could not add the LEADERBOARD button: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(MainMenu), "Update")]
    internal static class LbMainMenuUpdatePatch
    {
        static bool Prefix(MainMenu __instance)
        {
            try
            {
                return LbMenu.LetMenuUpdate(__instance);
            }
            catch (Exception)
            {
                return true;   // the menu's own Update stands
            }
        }
    }
}
