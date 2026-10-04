using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameModeForge.SalvageShop
{
    // THE SLOT (13.1: "drag the module you're upgrading into this grid slot") -
    // a fourth drop target on the grid screen, beside the grid, the vault and
    // the shop.
    //
    // ★ THE GAME'S DROP TARGETS ARE HARD-CODED (12.3). ModuleGridInput picks
    // between the vault, the shop and the grid by testing the mouse against
    // each one's rectangle, and ModuleGridScreen routes a drop by comparing
    // the target with those three. So the slot is a real ModuleContainerWidget
    // - the type the input holds as its "active widget" - and SalvageScreen
    // teaches the two patched methods to find it (the mouse) and to route a
    // drop into it. A pad never drops anywhere but the grid, so a pad has its
    // own path: A sends the highlighted module straight here (SalvageScreen).
    //
    // ★ THE MODULE NEVER LEAVES WHERE IT IS. The slot holds a REFERENCE: an
    // equipped module stays installed and a vault module stays in the vault
    // while it is in the slot, so "it goes back where it came from" (14.1) is
    // true by construction, and closing the menu at any moment can lose
    // nothing. Scrapping is the one thing that takes it away (from the vault).
    internal sealed class SalvageSlot : ModuleContainerWidget
    {
        internal SalvagePanel panel;
        internal RectTransform rect;
        internal Image frame;
        internal RectTransform icon;

        private bool _hover;

        internal static readonly Color Idle = new Color(1f, 1f, 1f, 0.35f);
        internal static readonly Color Lit = new Color(0.894f, 0.49f, 0.118f, 0.9f);   // the game's highlight

        public override bool IsActive
        {
            get { return base.IsActive; }
            set
            {
                base.IsActive = value;
                Paint();
            }
        }

        public override void OnDraggedModuleEnter(Module module)
        {
            _hover = true;
            Paint();
        }

        public override void OnDraggedModuleExit()
        {
            _hover = false;
            Paint();
        }

        // Nothing is ever picked UP from the slot: it is not where a module is.
        public override bool CanMove(Module module)
        {
            return false;
        }

        public override bool CanMoveTo(Module module, Vector2Int gridPosition)
        {
            return panel != null && panel.Accepts(module);
        }

        public override void SelectModule(Module module)
        {
            SelectedModule = panel != null ? panel.Slotted : null;
        }

        public override void SelectFirstModule()
        {
            SelectedModule = panel != null ? panel.Slotted : null;
        }

        // Up / down move the stat cursor; everything is consumed, so a stick
        // nudge never wanders off into the grid while a module is in here.
        public override bool MoveSelection(Vector2Int direction)
        {
            if (panel != null && direction.y != 0)
                panel.MoveCursor(-direction.y);

            return true;
        }

        public override void OnMouseMoved(Vector2 cursorPosition, Module draggedModule)
        {
            Module want = panel != null && rect != null && rect.ContainsPoint(cursorPosition) ? panel.Slotted : null;

            if (!ReferenceEquals(want, SelectedModule))
                SelectedModule = want;
        }

        public override void TweenModuleToGrid(Module module, Vector3 startPosition, float duration)
        {
        }

        // The hovered card follows this (ModuleGridInput.OnSelectedModuleChanged).
        public override RectTransform GetModuleWidget(Module module)
        {
            return icon != null && icon.gameObject.activeInHierarchy ? icon : rect;
        }

        internal void Paint()
        {
            try
            {
                if (frame != null)
                    frame.color = _hover || IsActive ? Lit : Idle;
            }
            catch (Exception)
            {
            }
        }
    }
}
