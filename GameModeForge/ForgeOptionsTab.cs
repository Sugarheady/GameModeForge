using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameModeForge
{
    // The "GAME MODE FORGE" tab inside the game's own options screen.
    //
    // It is a real `OptionsTab`, so keyboard and gamepad navigation, the
    // open/close animation, the selection highlight and the gamepad hints all
    // come from the game rather than being rebuilt. The rows are clones of the
    // game's own Off/On widget for the same reason.
    //
    // ★ THE ONE ORDERING RULE, AND IT IS COPIED FROM `GameplayOptionsTab`
    // RATHER THAN INVENTED: in OnOpened, PUSH the saved value with
    // SetSelection first and SUBSCRIBE to SelectionChanged second.
    // `OptionsMenuItemButtons.OnEnable` forces the selection back to 0 every
    // time the tab is shown, and firing our handler for that would silently
    // write "off" over the player's setting the moment they opened the menu.
    // The game's own tab gets this right; do not reorder it.
    public class ForgeOptionsTab : OptionsTab, IScrollHandler
    {
        // Filled in by ForgeOptionsMenu before the tab is ever shown.
        public readonly List<Row> rows = new List<Row>();

        // A sibling mod's own settings (ForgeTabRows) - New Game Plus's carry
        // switches and sliders. Unlike a mod switch, a setting is written the
        // moment it changes, in the main menu and mid-run alike: it takes
        // effect when that mod next reads it (New Game Plus: at the next
        // jump), never at a launch, so there is nothing for a restart to do.
        public readonly List<SettingRow> settings = new List<SettingRow>();
        public OptionsMenuItemButtons restartItem;
        public GameObject restartLabel;
        public Prompt confirmPrompt;

        // ★ Opened from the PAUSE menu. There is no restart row (restarting
        // would lose the run), so nothing would ever apply a pending switch -
        // a toggle is written to its mod's file the moment it is made, and
        // takes effect at the next launch. The row says so.
        public bool inRun;

        public sealed class Row
        {
            public ForgeModControl.Entry mod;
            public OptionsMenuItemButtons item;
            public GameObject label;
        }

        public sealed class SettingRow
        {
            public ForgeTabRows source;
            public ForgeTabRows.Row row;
            public OptionsMenuItemButtons toggle;   // a toggle row, or
            public OptionsMenuItemSlider slider;    // a slider row
            public Slider ui;                       // that slider's own Slider
            public GameObject label;
            public int value;
        }

        // Index 0 is OFF and index 1 is ON, matching the game's own
        // convention (`aimAssistButtons.SetSelection(disableAimAssist ? 0 : 1)`)
        // so a cloned row's buttons already read the right way round.
        private const int Off = 0;
        private const int On = 1;

        // The restart row: 0 is "leave it", 1 is "do it".
        private const int NotNow = 0;
        private const int DoIt = 1;

        private readonly Dictionary<OptionsMenuItemButtons, Action<int>> _handlers =
            new Dictionary<OptionsMenuItemButtons, Action<int>>();

        private readonly Dictionary<Slider, UnityAction<float>> _sliderHandlers =
            new Dictionary<Slider, UnityAction<float>>();

        protected override void OnOpened()
        {
            try
            {
                // Re-read the files every time. The player may have edited a
                // config by hand since the menu was last open, and a switch
                // showing a value the game is not actually using is worse than
                // no switch at all.
                ForgeModControl.Refresh();

                foreach (Row row in rows)
                {
                    if (row.item == null)
                        continue;

                    // PUSH FIRST.
                    row.item.SetSelection(row.mod.Enabled ? On : Off);

                    // SUBSCRIBE SECOND. Captured per row so the handler knows
                    // which mod it speaks for without a lookup.
                    Row captured = row;
                    Action<int> handler = sel => OnModToggled(captured, sel);

                    _handlers[row.item] = handler;
                    row.item.SelectionChanged += handler;
                }

                // The same ordering rule for the settings: PUSH the value the
                // owning mod reports, THEN subscribe - a subscription first
                // would write "off" over every setting the moment the tab
                // opened (OnEnable forces a toggle to 0).
                foreach (SettingRow s in settings)
                {
                    SettingRow captured = s;
                    s.value = s.source.Get(s.row.id);

                    if (s.toggle != null)
                    {
                        s.toggle.SetSelection(s.value != 0 ? On : Off);

                        Action<int> handler = sel => OnSettingToggled(captured, sel);
                        _handlers[s.toggle] = handler;
                        s.toggle.SelectionChanged += handler;
                    }
                    else if (s.ui != null)
                    {
                        s.ui.SetValueWithoutNotify(Mathf.Clamp(s.value, 0, s.row.steps));

                        UnityAction<float> handler = v => OnSettingSlid(captured, v);
                        _sliderHandlers[s.ui] = handler;
                        s.ui.onValueChanged.AddListener(handler);
                    }

                    PaintSetting(s);
                }

                if (restartItem != null)
                {
                    restartItem.SetSelection(NotNow);

                    Action<int> restart = OnRestartChosen;
                    _handlers[restartItem] = restart;
                    restartItem.SelectionChanged += restart;
                }

                ScrollTo(0);
                Repaint();
            }
            catch (Exception ex)
            {
                // A throw here happens inside the game's own Show()
                // coroutine, which would leave the options screen half open.
                GameModeForgePlugin.Log.LogError(
                    "GameModeForge tab failed to open: " + ex);
            }
        }

        protected override void OnClosed()
        {
            try
            {
                foreach (var pair in _handlers)
                    if (pair.Key != null)
                        pair.Key.SelectionChanged -= pair.Value;

                _handlers.Clear();

                foreach (var pair in _sliderHandlers)
                    if (pair.Key != null)
                        pair.Key.onValueChanged.RemoveListener(pair.Value);

                _sliderHandlers.Clear();

                // ★ ANYTHING NOT APPLIED IS DISCARDED. Leaving pending state
                // behind would mean reopening the tab shows switches in a
                // position the game is not in - the same lie as reading a
                // stale config. A refusal has to consume what provoked it.
                if (!ForgeRelaunch.InProgress)
                    ForgeModControl.Revert();
            }
            catch (Exception ex)
            {
                GameModeForgePlugin.Log.LogError(
                    "GameModeForge tab failed to close cleanly: " + ex);
            }
        }

        private void OnModToggled(Row row, int selection)
        {
            row.mod.Pending = (selection == On);

            // Mid-run there is no restart row to apply anything, so write it
            // now. A failed write puts the switch back where the file is -
            // a switch showing a value that was never saved is the lie this
            // tab exists to avoid. (SetSelection re-enters this handler with
            // the old value; Pending then equals Enabled and nothing is
            // written twice.)
            if (inRun && row.mod.Changed && !ForgeModControl.Commit())
            {
                row.mod.Pending = row.mod.Enabled;

                if (row.item != null)
                    row.item.SetSelection(row.mod.Enabled ? On : Off);
            }

            Repaint();
        }

        // ---- a sibling's settings --------------------------------------

        private void OnSettingToggled(SettingRow s, int selection)
        {
            int want = selection == On ? 1 : 0;

            if (want == s.value)
                return;

            if (s.source.Set(s.row.id, want))
            {
                s.value = want;
            }
            else if (s.toggle != null)
            {
                // Not saved: back to what the file says. (SetSelection
                // re-enters with the old value, which equals s.value now.)
                s.toggle.SetSelection(s.value != 0 ? On : Off);
            }

            PaintSetting(s);
        }

        private void OnSettingSlid(SettingRow s, float raw)
        {
            int want = Mathf.Clamp(Mathf.RoundToInt(raw), 0, s.row.steps);

            if (want == s.value)
                return;

            if (s.source.Set(s.row.id, want))
                s.value = want;
            else if (s.ui != null)
                s.ui.SetValueWithoutNotify(s.value);

            PaintSetting(s);
        }

        private void PaintSetting(SettingRow s)
        {
            try
            {
                ForgeOptionsMenu.SetLabel(s.label, s.source.Label(s.row.id, s.value, s.row.caption));
            }
            catch (Exception)
            {
                // Cosmetic only.
            }
        }

        // ---- scrolling -------------------------------------------------
        //
        // The rows are children of this object's VerticalLayoutGroup, laid out
        // from the top. A RectMask2D on this object clips them to the tab's own
        // rect (and a clipped row takes no clicks - the raycaster skips a
        // culled graphic), and the layout's top padding moves the column.

        private VerticalLayoutGroup _layout;
        private float _offset;

        public void EnableScrolling(VerticalLayoutGroup layout)
        {
            _layout = layout;

            if (_layout == null)
                return;

            if (GetComponent<RectMask2D>() == null)
                gameObject.AddComponent<RectMask2D>();

            // An invisible backdrop, so the mouse wheel works over the gaps
            // between rows as well as over them (the scroll event goes to what
            // the pointer is over and bubbles up to this component).
            if (GetComponent<Graphic>() == null)
            {
                Image catcher = gameObject.AddComponent<Image>();
                catcher.color = new Color(0f, 0f, 0f, 0f);
                catcher.raycastTarget = true;
            }
        }

        // OptionsTab.SetSelected, postfix (ForgeOptionsScrollPatch): keep the
        // selected row on screen as up / down move through the list.
        public void ScrollTo(int index)
        {
            if (_layout == null)
                return;

            try
            {
                OptionsMenuitemBase[] items = Items();

                if (items == null || index < 0 || index >= items.Length)
                    return;

                float top = 0f;

                for (int i = 0; i < index; i++)
                    top += RowHeight(items[i]) + _layout.spacing;

                float height = RowHeight(items[index]);
                float view = ((RectTransform)transform).rect.height;

                if (top < _offset)
                    _offset = top;
                else if (top + height > _offset + view)
                    _offset = top + height - view;

                Apply(items);
            }
            catch (Exception)
            {
                // Cosmetic: a row off screen is still reachable by keyboard.
            }
        }

        public void OnScroll(PointerEventData data)
        {
            if (_layout == null || data == null)
                return;

            try
            {
                OptionsMenuitemBase[] items = Items();

                if (items == null || items.Length == 0)
                    return;

                // Half a row per notch, whatever the device reports a notch as.
                float step = RowHeight(items[0]) * 0.5f;
                float dir = data.scrollDelta.y > 0f ? -1f : (data.scrollDelta.y < 0f ? 1f : 0f);

                _offset += dir * step;
                Apply(items);
            }
            catch (Exception)
            {
            }
        }

        private void Apply(OptionsMenuitemBase[] items)
        {
            float content = 0f;

            for (int i = 0; i < items.Length; i++)
                content += RowHeight(items[i]) + (i > 0 ? _layout.spacing : 0f);

            float view = ((RectTransform)transform).rect.height;
            _offset = Mathf.Clamp(_offset, 0f, Mathf.Max(0f, content - view));

            int top = -Mathf.RoundToInt(_offset);

            if (_layout.padding.top != top)
            {
                _layout.padding.top = top;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
            }
        }

        private static float RowHeight(OptionsMenuitemBase item)
        {
            var rt = item != null ? item.transform as RectTransform : null;
            return rt != null ? rt.rect.height : 150f;
        }

        private static readonly System.Reflection.FieldInfo ItemsField =
            HarmonyLib.AccessTools.Field(typeof(OptionsTab), "items");

        private OptionsMenuitemBase[] Items()
        {
            return ItemsField != null ? ItemsField.GetValue(this) as OptionsMenuitemBase[] : null;
        }

        private void OnRestartChosen(int selection)
        {
            if (selection != DoIt)
                return;

            // ★ IT RESTARTS WHETHER OR NOT ANYTHING CHANGED, and that is the
            // point of the row rather than an edge case. This mod has no hot
            // reload: weapon and module JSON is read once at startup, so
            // "restart the game" IS how you pick up an edit, and it is the
            // thing this row will be used for most.
            //
            // An earlier version bounced the row back when nothing was
            // pending, which made a restart button that refused to restart -
            // a refusal with no visible reason, which reads as the row being
            // broken. Anything pending is applied on the way out; with
            // nothing pending, Commit is a no-op that returns true.
            if (confirmPrompt != null)
            {
                confirmPrompt.Open(Apply, CancelRestart);
                return;
            }

            // No prompt available. Going ahead anyway is the right call:
            // choosing "restart now" on a row is already a deliberate act, and
            // refusing silently would look like the button not working.
            GameModeForgePlugin.Log.LogWarning(
                "no confirm prompt was found, restarting without one.");

            Apply();
        }

        private void CancelRestart()
        {
            if (confirmPrompt != null)
                confirmPrompt.Close();

            if (restartItem != null)
                restartItem.SetSelection(NotNow);

            Repaint();
        }

        private void Apply()
        {
            if (confirmPrompt != null)
                confirmPrompt.Close();

            // ⚠ COMMIT BEFORE RESTARTING, AND DO NOT RESTART IF IT FAILED.
            // Restarting after a failed write looks exactly like the switch
            // not working, and the only evidence would be a log line nobody
            // has a reason to read.
            if (!ForgeModControl.Commit())
            {
                if (restartItem != null)
                    restartItem.SetSelection(NotNow);

                Repaint();
                return;
            }

            ForgeRelaunch.Restart();
        }

        // Keep every caption honest about what the game is doing.
        private void Repaint()
        {
            foreach (Row row in rows)
                PaintRow(row);

            if (restartLabel == null)
                return;

            string text;

            if (ForgeModControl.WarnAboutSave())
            {
                // ⚠ The one case that can cost the player something. A saved
                // run that used Forge content cannot load with that mod off -
                // `Vault.RestoreFromMemento` calls DeepCopy on a registry
                // lookup that returns null, inside the game's own load path.
                // Checked FIRST: a switch flipped from the pause menu is
                // already in the file, so nothing is "pending" and the
                // restart below still runs the mod off.
                text = (ForgeModControl.AnyChanged ? "Apply & restart" : "Restart") +
                       "  -  WARNING: your saved run may not load with a mod " +
                       "switched off";
            }
            else if (!ForgeModControl.AnyChanged)
            {
                // No longer "No changes to apply" - that was a caption for a
                // row that refused to do anything, and the row does something
                // now. It says what pressing it will actually do.
                text = "Restart the game";
            }
            else
            {
                text = "Apply & restart the game";
            }

            ForgeOptionsMenu.SetLabel(restartLabel, text);
        }

        // A row whose switch differs from what THIS launch is running shows
        // the game's own "changed" marker (`SetDirty`, the same one the Video
        // tab uses for a setting not yet applied) and says when it happens.
        // Mid-run a content mod going off also warns about the run being
        // played, because that is the save it puts at risk.
        private void PaintRow(Row row)
        {
            try
            {
                ForgeModControl.Entry mod = row.mod;
                bool dirty = mod.DiffersFromLaunch;

                if (row.item != null)
                    row.item.SetDirty(dirty);

                string text = mod.DisplayName;

                if (dirty)
                    text += " - " + (mod.Pending ? "ON" : "OFF") + " next launch";

                if (dirty && inRun && ForgeModControl.WarnFor(mod))
                    text += " (this run may not load)";

                ForgeOptionsMenu.SetLabel(row.label, text);
            }
            catch (Exception)
            {
                // Cosmetic only - never let a caption break the tab.
            }
        }
    }
}
