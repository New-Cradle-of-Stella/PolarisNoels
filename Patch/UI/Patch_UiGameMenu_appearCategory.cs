using HarmonyLib;
using nel.gm;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.appearCategory))]
    internal class Patch_UiGameMenu_appearCategory
    {
        [HarmonyPrefix]
        private static bool Prefix(UiGameMenu __instance, CATEG ct)
        {
            UiMenuMul.BxP.deactivate();
            if ((int)ct == 10)
            {
                __instance.quitAppearCategory();
                if (__instance.AGmcCache[10] == null)
                {
                    __instance.AGmcCache[10] = new UiGMCMultiplayer(__instance, ct);
                }
                __instance.AppearC = __instance.AGmcCache[10];
                __instance.BxRRemake(true);
                __instance.AppearC?.initAppearWhole();
                return false;
            }
            return true;
        }
    }
}
