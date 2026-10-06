using HarmonyLib;
using nel.mgm.smncr;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiSmnCreator), nameof(UiSmnCreator.createMainBox))]
    public class Patch_UiSmnCreator_createMainBox
    {
        [HarmonyPostfix]
        static void Postfix(UiSmnCreator __instance)
            => SimBattleLobby.CreatePanels(__instance);
    }
}
