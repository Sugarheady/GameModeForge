using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameModeForge
{
    // Builds the "GAME MODE FORGE" tab by CLONING the game's own Gameplay tab
    // and re-dressing it.
    //
    // ★ WHY CLONE RATHER THAN BUILD. Every stock widget carries authored
    // animators, layout, fonts, gamepad hints and a selection highlight, and
    // `OptionsTab` drives all of it. A hand-built tab would have to reproduce
    // that and would still look foreign. This is the same call the wingman
    // bodies made: borrow the game's object, strip what does not belong.
    //
    // ⚠ AND IT INHERITS THAT FEATURE'S HARD-WON RULE. A borrowed prefab must
    // be handled with an ALLOW-list, not a deny-list: `FuelPickup` carried a
    // `ParticleLifetime` nobody had thought of, which destroyed the object
    // after five seconds. Here the equivalent is that we KEEP the whole clone
    // and only replace the one component we know about
    // (`GameplayOptionsTab` -> `ForgeOptionsTab`), rather than trying to
    // enumerate what to strip.
    //
    // ★ THE WHOLE BUILD IS WRAPPED. If any step fails, the options screen must
    // be left exactly as the game made it - three working tabs and no fourth.
    // A mod that breaks the settings menu is worse than a mod with no settings.
    public static class ForgeOptionsMenu
    {
        // ★ KEYED BY SCREEN, NOT A SESSION-WIDE BOOL. Every visit to the main
        // menu loads a fresh scene with a fresh OptionsScreen, and each one
        // needs its own tab. The first version latched a static `_built`
        // that nothing ever cleared, so the tab would have appeared on the
        // first visit and never again after a run - on exactly the path the
        // restart row exists for. The guard only has to stop a double build
        // of ONE screen, so it remembers the last screen it built.
        private static int _builtFor;

        // Named for the diagnostic-gate sweep, so a second run of a session
        // can report the same failures as the first.
        private static readonly HashSet<string> _warnedMissing =
            new HashSet<string>();

        private static bool _saidInRun;

        public static void Build(OptionsScreen screen)
        {
            if (screen == null || screen.GetInstanceID() == _builtFor)
                return;

            // ★ MID-RUN THE TAB IS BUILT WITHOUT ITS RESTART ROW - the author's choice,
            // 2026-09-28, when the pause menu's Options button got switched on
            // (by the separate Pause Options DLL; in the playtest that button
            // ships INACTIVE). "Restart the game" from a pause would quit a
            // run that has not been saved, so mid-run the switches are written
            // straight to their files and take effect at the next launch.
            //
            // Until then the tab was main-menu ONLY, for exactly that reason.
            // The hazard was the restart, never the switches.
            bool inRun = !InMainMenu(screen);

            if (inRun && !_saidInRun)
            {
                _saidInRun = true;
                GameModeForgePlugin.Log.LogInfo(
                    "options opened during a run - the Game Mode Forge tab is " +
                    "built WITHOUT its restart row (restarting from here would " +
                    "lose everything since your last save). Switches changed " +
                    "here are saved at once and take effect at the next launch.");
            }

            try
            {
                if (BuildInner(screen, inRun))
                {
                    _builtFor = screen.GetInstanceID();
                    GameModeForgePlugin.Log.LogInfo(
                        "settings tab added to the options screen.");
                }
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogError(
                    "COULD NOT BUILD THE SETTINGS TAB - the game's own " +
                    "options screen is untouched and still works. " + ex);
            }
        }

        // The main menu scene holds a `MainMenu`; the game scene does not.
        // Asked of the screen's OWN scene, and through FindObjectsOfTypeAll
        // so an inactive menu object still counts.
        private static bool InMainMenu(OptionsScreen screen)
        {
            try
            {
                var scene = screen.gameObject.scene;

                return Resources.FindObjectsOfTypeAll<MainMenu>()
                    .Any(m => m != null && m.gameObject.scene == scene);
            }
            catch (Exception)
            {
                // If the question cannot be answered, answer "not the main
                // menu": the tab is then built without its restart row. A
                // missing restart costs a trip to the main menu, a mid-run
                // restart costs a run.
                return false;
            }
        }

        private static bool BuildInner(OptionsScreen screen, bool inRun)
        {
            // ---- 1. reach the private wiring ---------------------------
            FieldInfo tabsField = AccessTools.Field(typeof(OptionsScreen), "tabs");
            FieldInfo imagesField =
                AccessTools.Field(typeof(OptionsScreen), "tabButtonImages");
            FieldInfo donorField =
                AccessTools.Field(typeof(OptionsScreen), "gameplayOptionsTab");

            if (!Found("OptionsScreen.tabs", tabsField) ||
                !Found("OptionsScreen.tabButtonImages", imagesField) ||
                !Found("OptionsScreen.gameplayOptionsTab", donorField))
                return false;

            OptionsTab[] tabs = tabsField.GetValue(screen) as OptionsTab[];
            Image[] images = imagesField.GetValue(screen) as Image[];
            OptionsTab donor = donorField.GetValue(screen) as OptionsTab;

            if (!Found("the tabs array", tabs) ||
                !Found("the tab button images", images) ||
                !Found("the Gameplay tab to clone", donor))
                return false;

            // ⚠ THE BOUNDS TRAP. `OptionsScreen.ShowTabCoroutine` walks
            // `tabButtonImages[i]` for every i < tabs.Length with NO guard, so
            // growing `tabs` without growing the images throws
            // IndexOutOfRange inside the game's own coroutine - every time the
            // player changes tab. Exactly the shape of
            // `HitscanWeapon.visualsInstances`. Refuse rather than half-do it.
            if (images.Length < tabs.Length)
            {
                GameModeForgePlugin.Log.LogError(
                    "the options screen has " + tabs.Length + " tabs but " +
                    images.Length + " tab buttons - refusing to add another, " +
                    "because ShowTabCoroutine indexes the buttons by tab " +
                    "number with no bounds check.");

                return false;
            }

            // ---- 2. clone the tab --------------------------------------
            //
            // ⚠ CLONED UNDER AN INACTIVE HOLDER, so nothing on the copy wakes
            // up. Instantiating an ACTIVE object runs Awake on the copy at
            // once, and `AnimatedScreen.Awake` collects its elements then -
            // including the donor rows this build is about to destroy, which
            // would leave the open animation walking destroyed objects.
            GameObject holder = new GameObject("GameModeForgeHolder");
            holder.SetActive(false);

            GameObject clone = UnityEngine.Object.Instantiate(
                donor.gameObject, holder.transform, false);

            clone.name = "GameModeForgeOptionsTab";
            clone.SetActive(false);
            clone.transform.SetParent(donor.transform.parent, false);
            UnityEngine.Object.Destroy(holder);

            // ★ ANY FAILURE FROM HERE ON DESTROYS THE CLONE. The first
            // version threw half way through and left an orphaned, inactive
            // copy of the Gameplay tab sitting in the menu. Nothing is
            // published until step 7, so destroying the clone really does put
            // the screen back exactly as the game made it.
            try
            {
                return BuildFromClone(screen, tabsField, imagesField, tabs, images, clone, inRun);
            }
            catch (Exception)
            {
                if (clone != null)
                    UnityEngine.Object.DestroyImmediate(clone);

                throw;
            }
        }

        private static bool BuildFromClone(
            OptionsScreen screen, FieldInfo tabsField, FieldInfo imagesField,
            OptionsTab[] tabs, Image[] images, GameObject clone, bool inRun)
        {
            // Read the donor's own row BEFORE its component is destroyed.
            // Unity remaps serialized references into the copied hierarchy, so
            // the clone's component points at the CLONE's children - which is
            // exactly the row we want to reuse.
            OptionsTab cloneTab = clone.GetComponent<OptionsTab>();

            OptionsMenuItemButtons template =
                AccessTools.Field(cloneTab.GetType(), "aimAssistButtons")
                    ?.GetValue(cloneTab) as OptionsMenuItemButtons;

            FieldInfo itemsField = AccessTools.Field(typeof(OptionsTab), "items");
            FieldInfo animField =
                AccessTools.Field(typeof(OptionsTab), "animatedScreen");
            FieldInfo closeField =
                AccessTools.Field(typeof(OptionsTab), "closeAnimDuration");

            OptionsMenuitemBase[] oldItems =
                itemsField?.GetValue(cloneTab) as OptionsMenuitemBase[];

            // Fall back to any two-button row in the clone if the named field
            // has moved - the field name is the game's, not ours.
            if (template == null && oldItems != null)
                template = oldItems.OfType<OptionsMenuItemButtons>().FirstOrDefault();

            if (!Found("an Off/On row to clone", template))
            {
                UnityEngine.Object.DestroyImmediate(clone);
                return false;
            }

            // The rumble row: a sibling's slider settings are clones of it.
            // Optional - with none, slider rows are simply not built.
            OptionsMenuItemSlider sliderTemplate =
                oldItems != null ? oldItems.OfType<OptionsMenuItemSlider>().FirstOrDefault() : null;

            float closeDuration =
                closeField != null ? (float)closeField.GetValue(cloneTab) : 0f;

            Transform itemParent = template.transform.parent;

            // ---- 3. build our rows from that one template ---------------
            var rows = new List<ForgeOptionsTab.Row>();
            var settings = new List<ForgeOptionsTab.SettingRow>();
            var newItems = new List<OptionsMenuitemBase>();

            ForgeModControl.Refresh();

            foreach (ForgeModControl.Entry mod in ForgeModControl.Present)
            {
                GameObject rowGo =
                    UnityEngine.Object.Instantiate(template.gameObject, itemParent);

                rowGo.name = "Row_" + mod.Guid;

                OptionsMenuItemButtons item =
                    rowGo.GetComponent<OptionsMenuItemButtons>();

                GameObject label = FindRowLabel(rowGo);
                SetLabel(label, mod.DisplayName);

                // A row can now say "OFF next launch" after its name, and a
                // caption is a fixed width with no wrapping - so let it shrink
                // rather than run under the Off/On buttons.
                FitLabel(label);

                rows.Add(new ForgeOptionsTab.Row
                {
                    mod = mod,
                    item = item,
                    label = label,
                });

                newItems.Add(item);

                // ★ The mod's own settings, straight under its switch - New
                // Game Plus's carry switches and sliders. Drawn here, owned
                // there (ForgeTabRows).
                BuildSettingRows(mod, template, sliderTemplate, itemParent, settings, newItems);
            }

            // ---- 4. the Apply & Restart row - MAIN MENU ONLY -------------
            //
            // Mid-run it is not built at all (see Build): a restart from a
            // pause loses the run.
            OptionsMenuItemButtons restartItem = null;
            GameObject restartLabel = null;

            if (!inRun)
                restartItem = BuildRestartRow(template, itemParent, out restartLabel);

            if (restartItem != null)
                newItems.Add(restartItem);

            // ---- 5. swap the component ----------------------------------
            //
            // The clone is INACTIVE, so AddComponent does not run Awake - and
            // `OptionsTab.Awake` is what normally sets `animatedScreen`. Set
            // it by hand rather than relying on an Awake that will not happen;
            // a null there is an NRE inside the game's own Show() coroutine.
            UnityEngine.Object.DestroyImmediate(cloneTab);

            ForgeOptionsTab tab = clone.AddComponent<ForgeOptionsTab>();

            tab.rows.AddRange(rows);
            tab.settings.AddRange(settings);
            tab.inRun = inRun;

            // ★ THE TAB SCROLLS. A row is 150 high and about four fit (the
            // stock tabs hold four), while the switches plus New Game Plus's
            // settings are more than a dozen. The rows are clipped to the
            // tab's own rect and the column is moved by the layout's top
            // padding as the selection moves (and by the mouse wheel).
            tab.EnableScrolling(clone.GetComponent<VerticalLayoutGroup>());
            tab.restartItem = restartItem;
            tab.restartLabel = restartLabel;
            tab.confirmPrompt = inRun ? null : FindPrompt();

            itemsField.SetValue(tab, newItems.ToArray());
            animField?.SetValue(tab, clone.GetComponent<AnimatedScreen>());
            closeField?.SetValue(tab, closeDuration);

            // Remove the donor rows we did not reuse. Done LAST, so anything
            // above that failed leaves a clone we can simply destroy whole.
            //
            // ★ THE TEMPLATE IS ONE OF THOSE ROWS - it is the Aim Assist row,
            // and that row is in `items` - so the loop has usually destroyed
            // it already. The first version then read `template.gameObject`
            // unguarded, which on a destroyed Unity object throws a
            // NullReferenceException from inside `Component.gameObject`. That
            // was the whole of R19 test 272: the build died on its last line
            // of cleanup, every time, and the tab never appeared. `!= null` is
            // Unity's overloaded test, which is true of a destroyed object.
            if (oldItems != null)
                foreach (OptionsMenuitemBase old in oldItems)
                    if (old != null)
                        UnityEngine.Object.DestroyImmediate(old.gameObject);

            if (template != null)
                UnityEngine.Object.DestroyImmediate(template.gameObject);

            // The element list is collected in AnimatedScreen.Awake, which has
            // not run on the clone yet - refreshed anyway, so the open
            // animation walks our rows whatever the activation order was.
            clone.GetComponent<AnimatedScreen>()?.RefreshElementList();

            // ---- 6. the tab button --------------------------------------
            int index = tabs.Length;

            Image button = CloneTabButton(screen, images, index);

            // ---- 7. publish ----------------------------------------------
            tabsField.SetValue(screen, Append(tabs, tab));
            imagesField.SetValue(screen, Append(images, button ?? images[0]));

            GameModeForgePlugin.Log.LogInfo(
                "tab " + index + " built with " + rows.Count +
                " mod switch(es)" +
                (settings.Count > 0 ? " and " + settings.Count + " setting row(s)" : "") +
                (inRun
                    ? " - mid-run, so NO restart row: a switch changed here is " +
                      "saved at once and takes effect at the next launch"
                    : " plus the restart row") +
                (button == null
                    ? " - NO tab button could be cloned, so reach it with the " +
                      "next/previous tab keys"
                    : "") +
                (!inRun && tab.confirmPrompt == null
                    ? " - no confirm prompt found, restart will not ask" : ""));

            return true;
        }

        // Deliberately the SAME widget as a toggle rather than a bare button:
        // `OptionsMenuItemButtons.OnEnable` resets the selection to 0 every
        // time the tab opens, which is precisely the behaviour an action row
        // wants - it re-arms itself and can never be left sitting on
        // "restart".
        private static OptionsMenuItemButtons BuildRestartRow(
            OptionsMenuItemButtons template, Transform itemParent,
            out GameObject restartLabel)
        {
            GameObject restartGo =
                UnityEngine.Object.Instantiate(template.gameObject, itemParent);

            restartGo.name = "Row_ApplyRestart";

            OptionsMenuItemButtons restartItem =
                restartGo.GetComponent<OptionsMenuItemButtons>();

            restartLabel = FindRowLabel(restartGo);

            LabelButtons(restartItem, "Not now", "Restart now");

            return restartItem;
        }

        // A sibling's settings rows (ForgeTabRows), under its switch. A toggle
        // is a clone of the Off/On row with its two captions; a slider is a
        // clone of the rumble slider set to the sibling's step count. Anything
        // that cannot be built is skipped and said - a missing row is better
        // than a tab that does not open.
        private static readonly FieldInfo SliderField =
            AccessTools.Field(typeof(OptionsMenuItemSlider), "slider");

        private static void BuildSettingRows(
            ForgeModControl.Entry mod, OptionsMenuItemButtons toggleTemplate,
            OptionsMenuItemSlider sliderTemplate, Transform itemParent,
            List<ForgeOptionsTab.SettingRow> settings, List<OptionsMenuitemBase> newItems)
        {
            if (string.IsNullOrEmpty(mod.RowsType))
                return;

            ForgeTabRows source = ForgeTabRows.Find(mod.RowsType, mod.DisplayName);

            if (source == null)
                return;

            int skipped = 0;

            foreach (ForgeTabRows.Row row in source.Rows())
            {
                try
                {
                    var s = new ForgeOptionsTab.SettingRow { source = source, row = row };

                    if (row.slider)
                    {
                        if (sliderTemplate == null)
                        {
                            skipped++;
                            continue;
                        }

                        GameObject go = UnityEngine.Object.Instantiate(sliderTemplate.gameObject, itemParent);
                        go.name = "Row_" + row.id;

                        s.slider = go.GetComponent<OptionsMenuItemSlider>();
                        s.ui = SliderField != null ? SliderField.GetValue(s.slider) as Slider : null;

                        if (s.slider == null || s.ui == null)
                        {
                            UnityEngine.Object.DestroyImmediate(go);
                            skipped++;
                            continue;
                        }

                        // One step per press of left / right (HandleRight adds
                        // exactly 1 to the slider's value).
                        s.ui.minValue = 0f;
                        s.ui.maxValue = row.steps;
                        s.ui.wholeNumbers = true;

                        // The number on the handle: PunkSlider shows
                        // value / maxValue * its own maxValue.
                        foreach (Component c in s.ui.GetComponents<Component>())
                        {
                            if (c == null || c.GetType().Name != "PunkSlider")
                                continue;

                            FieldInfo shown = AccessTools.Field(c.GetType(), "maxValue");

                            if (shown != null && shown.FieldType == typeof(int))
                                shown.SetValue(c, row.maxShown);
                        }

                        s.label = FindRowLabel(go);
                        newItems.Add(s.slider);
                    }
                    else
                    {
                        GameObject go = UnityEngine.Object.Instantiate(toggleTemplate.gameObject, itemParent);
                        go.name = "Row_" + row.id;

                        s.toggle = go.GetComponent<OptionsMenuItemButtons>();
                        LabelButtons(s.toggle, row.offText, row.onText);

                        s.label = FindRowLabel(go);
                        newItems.Add(s.toggle);
                    }

                    SetLabel(s.label, row.caption);
                    FitLabel(s.label);
                    settings.Add(s);
                }
                catch (Exception ex)
                {
                    skipped++;
                    GameModeForgePlugin.Log.LogWarning(
                        "could not build " + mod.DisplayName + "'s '" + row.id + "' row (" +
                        ex.GetType().Name + ") - it is left out of the tab.");
                }
            }

            if (skipped > 0)
                GameModeForgePlugin.Log.LogWarning(
                    skipped + " of " + mod.DisplayName + "'s setting row(s) could not be built " +
                    "and are left out" + (sliderTemplate == null ? " (no slider row to copy)" : "") + ".");
        }

        // ---- the tab button along the top -------------------------------
        private static Image CloneTabButton(
            OptionsScreen screen, Image[] images, int index)
        {
            try
            {
                Image donor = images[0];

                // ★ THE TRACKED IMAGE IS NOT THE BUTTON, IT IS INSIDE ONE.
                // Measured off MainMenu.unity: each tab is a wrapper
                // (`GameplayButton` / `VideoButton` / `AudioButton`, with an
                // Animator) holding a `ButtonBody` - and `tabButtonImages`
                // points at the BODY, which also carries the `Button` whose
                // onClick calls ShowTab. Cloning the body alone would put a
                // second body INSIDE the Gameplay button, on top of it.
                //
                // So clone the wrapper when every tab image has its own
                // parent - which is the measured layout - and fall back to
                // cloning the image itself only if they share one.
                bool ownWrappers = images.Length > 1 &&
                    donor.transform.parent != null &&
                    donor.transform.parent.parent != null &&
                    images.Where(i => i != null)
                          .Select(i => i.transform.parent)
                          .Distinct().Count() == images.Count(i => i != null);

                Transform source = ownWrappers ? donor.transform.parent : donor.transform;

                GameObject go = UnityEngine.Object.Instantiate(
                    source.gameObject, source.parent);

                go.name = "TabButton_GameModeForge";
                go.transform.SetAsLastSibling();

                // ★ PLACED BY HAND, BECAUSE NOTHING ELSE WILL. The tab row's
                // HorizontalLayoutGroup ships DISABLED (measured off
                // MainMenu.unity: `Buttons`, m_Enabled 0), so every tab sits
                // at a fixed anchoredPosition - and a copy keeps its donor's.
                // R19 test 272: "It replaced the general settings tab instead
                // of creating a new tab." It had not replaced anything: it was
                // sitting exactly on top of GAMEPLAY, covering it, with
                // Gameplay still there underneath and reachable by Q / E.
                Relayout(images, go.transform as RectTransform, ownWrappers);

                // The image the screen tracks, at the same place in the copy.
                Image image = ownWrappers
                    ? FindSameImage(go.transform, donor)
                    : go.GetComponent<Image>();

                if (image == null)
                {
                    UnityEngine.Object.Destroy(go);
                    return null;
                }

                SetLabel(go, "GAME MODE FORGE");
                FitLabel(go);

                // ⚠ A CLONED BUTTON KEEPS ITS PREFAB-WIRED onClick, and
                // RemoveAllListeners does NOT remove a persistent one - it
                // only clears runtime listeners. So a clone of tab button 0
                // would still call ShowTab(0) as well as ours, and clicking
                // our tab would land on Gameplay. Persistent calls have to be
                // switched off one at a time.
                // Every click source in the copy is re-pointed, not just the
                // first: the measured tab has a plain `Button` on the body,
                // and a PunkButton anywhere in the copy would carry its own
                // ShowTab(0) the same way.
                foreach (PunkButton punk in go.GetComponentsInChildren<PunkButton>(true))
                {
                    UnityEventBase evt = punk.OnClick;

                    for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                        evt.SetPersistentListenerState(i, UnityEventCallState.Off);

                    punk.OnClick.RemoveAllListeners();
                    punk.OnClick.AddListener(() => screen.ShowTab(index));
                }

                foreach (Button plain in go.GetComponentsInChildren<Button>(true))
                {
                    for (int i = 0; i < plain.onClick.GetPersistentEventCount(); i++)
                        plain.onClick.SetPersistentListenerState(
                            i, UnityEventCallState.Off);

                    plain.onClick.RemoveAllListeners();
                    plain.onClick.AddListener(() => screen.ShowTab(index));
                }

                return image;
            }
            catch (Exception ex)
            {
                // Not fatal: tab cycling with the next/previous keys is driven
                // entirely by `tabs.Length`, so the tab is still reachable
                // without a button of its own.
                GameModeForgePlugin.Log.LogWarning(
                    "could not clone a tab button (" + ex.GetType().Name +
                    ") - the tab is still reachable with the tab keys.");

                return null;
            }
        }

        // Four tabs in the span three used to fill. The stock tabs are the
        // same width with the same gap (282.8 wide, 10.12 apart, measured),
        // so the row is rebuilt from its own numbers: the left edge of the
        // first tab to the right edge of the last, split four ways. Keeping
        // the span is what keeps the new tab off the right-hand key hint.
        //
        // Cosmetic and never fatal. Anything it cannot measure - a stretched
        // anchor, tabs of different widths - falls back to one tab-width to
        // the right of the last tab, which is still a separate button.
        private static void Relayout(
            Image[] images, RectTransform added, bool ownWrappers)
        {
            if (added == null)
                return;

            try
            {
                var tabs = new List<RectTransform>();

                foreach (Image img in images)
                {
                    if (img == null)
                        continue;

                    var rt = (ownWrappers ? img.transform.parent : img.transform)
                        as RectTransform;

                    if (rt != null && rt != added && !tabs.Contains(rt))
                        tabs.Add(rt);
                }

                if (tabs.Count == 0)
                    return;

                tabs.Sort((a, b) =>
                    a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));

                RectTransform first = tabs[0];
                RectTransform last = tabs[tabs.Count - 1];
                float w = first.sizeDelta.x;

                float gap = (tabs.Count > 1)
                    ? (tabs[1].anchoredPosition.x - first.anchoredPosition.x) - w
                    : 10f;

                bool measurable = w > 1f && gap >= 0f &&
                    tabs.TrueForAll(t =>
                        Mathf.Approximately(t.anchorMin.x, t.anchorMax.x) &&
                        Mathf.Abs(t.sizeDelta.x - w) < 0.5f);

                if (!measurable)
                {
                    added.anchoredPosition = last.anchoredPosition +
                        new Vector2(Mathf.Max(w, 50f) + Mathf.Max(gap, 0f), 0f);
                    return;
                }

                float left = first.anchoredPosition.x - w * first.pivot.x;
                float right = last.anchoredPosition.x + w * (1f - last.pivot.x);

                tabs.Add(added);

                int n = tabs.Count;
                float each = (right - left - gap * (n - 1)) / n;

                for (int i = 0; i < n; i++)
                {
                    RectTransform t = tabs[i];

                    t.sizeDelta = new Vector2(each, t.sizeDelta.y);
                    t.anchoredPosition = new Vector2(
                        left + each * t.pivot.x + i * (each + gap),
                        first.anchoredPosition.y);
                }
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogWarning(
                    "could not space the tab buttons (" + ex.GetType().Name +
                    ") - the new tab may overlap another.");
            }
        }

        // ---- small helpers ----------------------------------------------

        // The copy of `donor` inside a cloned wrapper: same name, directly
        // under the new root, falling back to the first Image carrying a
        // Button (which is what a tab body is).
        private static Image FindSameImage(Transform root, Image donor)
        {
            Transform same = root.Find(donor.name);

            if (same != null && same.GetComponent<Image>() != null)
                return same.GetComponent<Image>();

            foreach (Image img in root.GetComponentsInChildren<Image>(true))
                if (img.GetComponent<Button>() != null)
                    return img;

            return null;
        }

        // "GAME MODE FORGE" is longer than "GAMEPLAY" and a tab is a fixed
        // width, so let TextMeshPro shrink it to fit rather than spill over
        // the neighbouring tab. Reflection, cosmetic, never fatal.
        private static void FitLabel(GameObject target)
        {
            try
            {
                Type tmp = ForgeInterop.FindTypeQuietly("TMPro.TMP_Text");

                if (tmp == null)
                    return;

                Component[] found = target.GetComponentsInChildren(tmp, true);

                if (found.Length == 0)
                    return;

                // Capped at the size it already has: auto-size alone grows
                // text up to fontSizeMax (72 by default), which would make
                // this tab's caption LARGER than its neighbours.
                PropertyInfo size = tmp.GetProperty("fontSize");

                if (size != null)
                {
                    float current = (float)size.GetValue(found[0], null);

                    tmp.GetProperty("fontSizeMax")?.SetValue(found[0], current, null);
                    tmp.GetProperty("fontSizeMin")?.SetValue(found[0], current * 0.5f, null);
                }

                tmp.GetProperty("enableAutoSizing")?.SetValue(found[0], true, null);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        // The game's confirm dialog, borrowed from the main menu's Exit
        // prompt. Found by type so a renamed field does not lose it.
        private static Prompt FindPrompt()
        {
            try
            {
                // `Resources.FindObjectsOfTypeAll` is the house idiom here
                // because it sees INACTIVE objects, and a confirm dialog is
                // inactive until it is opened.
                //
                // ⚠ It also sees PREFABS, which is the reason for the scene
                // filter: opening a prompt that is a prefab rather than a live
                // object would do nothing and look like the confirm silently
                // failing. Same trap the resource-pickup cache hit from the
                // other direction, where seeing prefabs was the point.
                return Resources.FindObjectsOfTypeAll<Prompt>()
                    .FirstOrDefault(p => p != null &&
                                         p.gameObject.scene.IsValid());
            }
            catch (Exception)
            {
                return null;
            }
        }

        // A row's caption is the text that is NOT inside one of its buttons.
        private static GameObject FindRowLabel(GameObject row)
        {
            Type tmp = ForgeInterop.FindTypeQuietly("TMPro.TMP_Text");

            if (tmp == null)
                return null;

            foreach (Component text in row.GetComponentsInChildren(tmp, true))
            {
                if (!InsideButton(text.transform))
                    return text.gameObject;
            }

            return null;
        }

        // ★ WALKED BY HAND RATHER THAN WITH GetComponentInParent, because the
        // clone is INACTIVE while this runs and that helper skips inactive
        // parents unless given the right overload. Getting that wrong would
        // report every caption as "not in a button" and put the row's label on
        // a button face instead. `GetComponent` has no such condition.
        private static bool InsideButton(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p.GetComponent<PunkButton>() != null)
                    return true;

                if (p.GetComponent<Button>() != null)
                    return true;
            }

            return false;
        }

        private static void LabelButtons(
            OptionsMenuItemButtons item, params string[] captions)
        {
            FieldInfo field =
                AccessTools.Field(typeof(OptionsMenuItemButtons), "buttons");

            PunkButton[] buttons = field?.GetValue(item) as PunkButton[];

            if (buttons == null)
                return;

            for (int i = 0; i < buttons.Length && i < captions.Length; i++)
                if (buttons[i] != null)
                    SetLabel(buttons[i].gameObject, captions[i]);
        }

        // ★ TMP IS REACHED BY REFLECTION, NOT BY REFERENCE. Module Forge does
        // the same for the same reason: referencing Unity.TextMeshPro to set
        // one string widens this mod's compile-time surface for no benefit,
        // and a missing label is a cosmetic failure that must never stop the
        // tab being built.
        public static void SetLabel(GameObject target, string text)
        {
            if (target == null)
                return;

            try
            {
                Type tmp = ForgeInterop.FindTypeQuietly("TMPro.TMP_Text");

                if (tmp == null)
                    return;

                Component[] found = target.GetComponentsInChildren(tmp, true);

                if (found.Length == 0)
                    return;

                PropertyInfo prop = tmp.GetProperty("text");
                prop?.SetValue(found[0], text, null);
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        private static T[] Append<T>(T[] source, T item)
        {
            T[] result = new T[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = item;
            return result;
        }

        // Says what was missing, once per name, rather than failing silently -
        // every one of these is a private field of the game's, so any of them
        // can move in a playtest update and the log has to name which.
        private static bool Found(string what, object thing)
        {
            bool ok = thing is UnityEngine.Object
                ? (UnityEngine.Object)thing != null
                : thing != null;

            if (!ok && _warnedMissing.Add(what))
                GameModeForgePlugin.Log.LogError(
                    "could not find " + what + " - the settings tab will not " +
                    "be added. The game's own options screen is unaffected.");

            return ok;
        }
    }
}
