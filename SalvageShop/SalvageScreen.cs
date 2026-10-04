using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GameModeForge.SalvageShop
{
    // THE PATCHES THAT PUT THE SALVAGE TAB INTO THE GRID SCREEN (19, "The
    // screen"). Each is gated on the plugin being on and the panel being built;
    // with either false, every one of them returns at once and the screen is
    // the game's.
    //
    //   ModuleGridScreen.OnOpened        build the tab once per screen instance
    //   ModuleGridScreen.OnClosed        close the panel (nothing is lost - the
    //                                    slot only ever held a reference)
    //   ModuleGridScreen.ShowShop/Vault  the SHOP / VAULT tabs close it
    //   ModuleGridScreen.OnModuleDropped a drop on the slot puts it in the slot
    //   ModuleGridScreen.OnBackPressed   B takes the module out of the slot
    //   ModuleGridInput.OnMouseMoved     ★ the mouse finds the slot (the game
    //                                    tests only vault, shop and grid)
    //   ModuleGridInput.OnConfirmPressed the pad's A sends a module to the slot
    //   ModuleGridInput.SelectShop/Vault RT / LT step VAULT <-> SHOP <-> SALVAGE;
    //                                    RT on SALVAGE switches the mode
    //   ModuleGridInput.OnUnequip        X on the SALVAGE tab: quick salvage
    //   ModuleGridInput.OnRightMouseDown a right-click on the slot takes it out
    //   ModuleGridInput.OnUpdate         the confirm key: pad Y / keyboard R
    internal static class SalvageScreen
    {
        internal static void OnRunEntry()
        {
            SalvagePanel.ForgetRankLookup();
        }

        private static SalvagePanel Open(Component c)
        {
            if (!SalvageShopPlugin.Active)
                return null;

            SalvagePanel p = SalvagePanel.For(c);
            return p != null && p.Built && p.IsOpen ? p : null;
        }

        [HarmonyPatch(typeof(ModuleGridScreen), "OnOpened")]
        public class OnOpened
        {
            static void Postfix(ModuleGridScreen __instance)
            {
                if (!SalvageShopPlugin.Active)
                    return;

                try
                {
                    SalvagePanel p = SalvagePanel.Ensure(__instance);

                    if (p != null && p.IsOpen)
                        p.Close();
                }
                catch (Exception ex)
                {
                    SalvageShopPlugin.Log.LogError("the SALVAGE tab could not open with the grid screen: " + ex);
                }
            }
        }

        [HarmonyPatch(typeof(ModuleGridScreen), "OnClosed")]
        public class OnClosed
        {
            static void Prefix(ModuleGridScreen __instance)
            {
                try
                {
                    SalvagePanel p = Open(__instance);

                    if (p != null)
                        p.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        [HarmonyPatch(typeof(ModuleGridScreen), "ShowShop")]
        public class OnShowShop
        {
            static void Postfix(ModuleGridScreen __instance)
            {
                SalvagePanel p = Open(__instance);

                if (p != null && !p.Opening)
                    p.Close();
            }
        }

        [HarmonyPatch(typeof(ModuleGridScreen), "ShowVault")]
        public class OnShowVault
        {
            static void Postfix(ModuleGridScreen __instance)
            {
                SalvagePanel p = Open(__instance);

                if (p != null && !p.Opening)
                    p.Close();
            }
        }

        // ★ The game's own drop routing already does the right thing for a
        // target it does not know: when the target's CanMoveTo says yes it hides
        // the dragged icon and completes the move, and when it says no it plays
        // "placement failed". So this only has to PUT the module in the slot,
        // and lets the original run either way.
        [HarmonyPatch(typeof(ModuleGridScreen), "OnModuleDropped")]
        public class OnDropped
        {
            static void Prefix(ModuleGridScreen __instance, ModuleGridInput.ModuleMoveArgs dropArgs)
            {
                SalvagePanel p = Open(__instance);

                if (p == null || !p.IsSlot(dropArgs.target))
                    return;

                try
                {
                    if (dropArgs.target.CanMoveTo(dropArgs.module, dropArgs.targetGridPosition))
                        p.Put(dropArgs.module, dropArgs.origin);
                }
                catch (Exception ex)
                {
                    SalvageShopPlugin.Log.LogWarning("the slot could not take the module (" + ex.Message + ").");
                }
            }
        }

        [HarmonyPatch(typeof(ModuleGridScreen), "OnBackPressed")]
        public class OnBack
        {
            static bool Prefix(ModuleGridScreen __instance, ref bool __result)
            {
                SalvagePanel p = Open(__instance);

                if (p == null || !p.Back())
                    return true;

                __result = true;
                return false;
            }
        }

        // ---- the input -------------------------------------------------------

        private static readonly AccessTools.FieldRef<ModuleGridInput, Vector2> Mouse = Ref<Vector2>("mousePosition");
        private static readonly AccessTools.FieldRef<ModuleGridInput, Module> Moved = Ref<Module>("movedModule");
        private static readonly AccessTools.FieldRef<ModuleGridInput, bool> ByDrag = Ref<bool>("movementStartedWithDrag");
        private static readonly FieldInfo Dragged = AccessTools.Field(typeof(ModuleGridInput), "ModuleDragged");

        private static AccessTools.FieldRef<ModuleGridInput, T> Ref<T>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<ModuleGridInput, T>(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ★ THE MOUSE FINDS THE SLOT. The game's OnMouseMoved tests the vault,
        // then the shop, and defaults to the grid. Over the panel this does what
        // it would do for the vault - make it the active widget, tell it where
        // the mouse is, and move the dragged icon - and skips the original.
        [HarmonyPatch(typeof(ModuleGridInput), "OnMouseMoved")]
        public class OnMouse
        {
            static bool Prefix(ModuleGridInput __instance)
            {
                SalvagePanel p = Open(__instance);

                if (p == null || p.slot == null || Mouse == null)
                    return true;

                try
                {
                    Vector2 mouse = Mouse(__instance);

                    if (!p.Contains(mouse))
                        return true;

                    p.SetActive(p.slot);

                    Module moving = Moved != null ? Moved(__instance) : null;
                    p.slot.OnMouseMoved(mouse, moving);

                    if (moving != null && ByDrag != null && ByDrag(__instance) && Dragged != null)
                    {
                        var evt = Dragged.GetValue(__instance) as Action<Vector2, Module>;

                        if (evt != null)
                            evt(mouse, p.slot.SelectedModule);
                    }

                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(ModuleGridInput), "OnConfirmPressed")]
        public class OnConfirm
        {
            static bool Prefix(ModuleGridInput __instance)
            {
                SalvagePanel p = Open(__instance);

                try
                {
                    return p == null || !__instance.IsEditingEnabled || !p.PickUp();
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        // RT / E. On SHOP it steps right to SALVAGE; on SALVAGE it switches the
        // mode (SALVAGE <-> UPGRADE); on VAULT it is the game's own (to SHOP).
        [HarmonyPatch(typeof(ModuleGridInput), "SelectShopPressed")]
        public class OnSelectShop
        {
            static bool Prefix(ModuleGridInput __instance)
            {
                if (!SalvageShopPlugin.Active || !__instance.IsEditingEnabled || __instance.IsMovingModule)
                    return true;

                SalvagePanel p = SalvagePanel.For(__instance);

                if (p == null || !p.Built)
                    return true;

                try
                {
                    if (p.IsOpen)
                    {
                        p.ToggleMode();
                        return false;
                    }

                    ShopWidget shop = ServiceLocator.Get<ShopWidget>();

                    if (shop != null && shop.IsOpened)
                    {
                        p.Open();
                        return !p.IsOpen;
                    }
                }
                catch (Exception)
                {
                }

                return true;
            }
        }

        // LT / Q. On SALVAGE it steps left to SHOP (the game's own SelectShop,
        // run once the panel is shut).
        [HarmonyPatch(typeof(ModuleGridInput), "SelectVaultPressed")]
        public class OnSelectVault
        {
            static bool Prefix(ModuleGridInput __instance, UnityEngine.InputSystem.InputAction.CallbackContext obj)
            {
                SalvagePanel p = Open(__instance);

                if (p == null || !__instance.IsEditingEnabled || __instance.IsMovingModule)
                    return true;

                try
                {
                    p.Close();
                    __instance.SelectShopPressed(obj);
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        // X (pad Unequip). On the SALVAGE tab it ticks QUICK SALVAGE - unless
        // the grid is the active widget, where it unequips as it always has.
        [HarmonyPatch(typeof(ModuleGridInput), "OnUnequip")]
        public class OnUnequip
        {
            static bool Prefix(ModuleGridInput __instance)
            {
                SalvagePanel p = Open(__instance);

                if (p == null)
                    return true;

                try
                {
                    if (p.Active is ModuleGridWidget)
                        return true;

                    p.ToggleQuick();
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(ModuleGridInput), "OnRightMouseDown")]
        public class OnRightClick
        {
            static bool Prefix(ModuleGridInput __instance)
            {
                SalvagePanel p = Open(__instance);

                if (p == null || __instance.IsMovingModule || Mouse == null)
                    return true;

                try
                {
                    return !p.RightClick(Mouse(__instance));
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        // ★ READ ABOVE NOTHING: OnUpdate has no early return, so a postfix runs
        // every frame the grid screen updates (its Update returns while the
        // canvas is hidden, which is when the keys should not count anyway).
        [HarmonyPatch(typeof(ModuleGridInput), "OnUpdate")]
        public class OnUpdate
        {
            static void Postfix(ModuleGridInput __instance)
            {
                SalvagePanel p = Open(__instance);

                if (p != null)
                    p.Keys();
            }
        }
    }
}
