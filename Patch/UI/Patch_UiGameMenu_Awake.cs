using HarmonyLib;
using nel;
using nel.gm;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiGameMenu), nameof(UiGameMenu.Awake))]
    public class Patch_UiGameMenu_Awake
    {
        [HarmonyPostfix]
        static void Postfix(UiGameMenu __instance)
        {
            UiMenuMul.BxP = __instance.Create("mulP", 0f, 0f, 200f, 200f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
            UiMenuMul.BxPD = __instance.Create("mulPD", 0f, 0f, 200f, 200f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
        }
    }
}
