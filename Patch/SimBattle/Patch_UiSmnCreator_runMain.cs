using HarmonyLib;
using nel.mgm.smncr;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(UiSmnCreator), nameof(UiSmnCreator.runMain))]
    public class Patch_UiSmnCreator_runMain
    {
        [HarmonyPrefix]
        static void Prefix(UiSmnCreator __instance)
            => SimBattleLobby.TryLeave(__instance);
    }
}
