using HarmonyLib;
using nel.mgm.smncr;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(UiSmnCreator), nameof(UiSmnCreator.changeState))]
    public class Patch_UiSmnCreator_changeState
    {
        [HarmonyPrefix]
        static bool Prefix(UiSmnCreator __instance, UiSmnCreator.STATE stt)
            => SimBattleLobby.TryEnter(__instance, stt);
    }
}
