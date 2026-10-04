using HarmonyLib;

namespace GameModeForge
{
    // The one hook the settings tab needs.
    //
    // ★ A POSTFIX ON `OptionsScreen.Awake`, and the timing is exact rather
    // than lucky. Awake is where the game fills `this.tabs` from its three
    // serialized fields, and `OnEnable` - which calls `ShowTab(0)` and so
    // reads that array - runs immediately afterwards in the same activation.
    // So a postfix on Awake is the last moment the array can be extended and
    // the first moment it exists.
    //
    // The options screen's GameObject is inactive until the player opens it,
    // so this fires the first time they go to Options rather than at startup.
    // That is why the build is idempotent: Awake can run again if the screen
    // is ever re-activated from scratch.
    [HarmonyPatch(typeof(OptionsScreen), "Awake")]
    public static class ForgeOptionsPatch
    {
        static void Postfix(OptionsScreen __instance)
        {
            ForgeOptionsMenu.Build(__instance);
        }
    }

    // The game's tabs never needed to scroll - four rows fit - so selection is
    // a private `SetSelected(int)` that knows nothing about the view. Ours has
    // more rows than fit, so it follows the selection (ForgeOptionsTab).
    // Every other tab passes straight through.
    [HarmonyPatch(typeof(OptionsTab), "SetSelected")]
    public static class ForgeOptionsScrollPatch
    {
        static void Postfix(OptionsTab __instance, int index)
        {
            ForgeOptionsTab tab = __instance as ForgeOptionsTab;

            if (tab != null)
                tab.ScrollTo(index);
        }
    }
}
