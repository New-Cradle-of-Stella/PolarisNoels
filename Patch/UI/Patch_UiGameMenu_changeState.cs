using HarmonyLib;
using nel.gm;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.changeState))]
    public class Patch_UiGameMenu_changeState
    {
        [HarmonyPrefix]
        static bool Prefix(UiGameMenu __instance)
        {
            UiMenuMul.BxP?.deactivate();
            UiMenuMul.BxPD?.deactivate();
            return true;
        }
    }
}
