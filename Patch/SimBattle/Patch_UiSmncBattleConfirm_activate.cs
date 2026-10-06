using HarmonyLib;
using nel.mgm.smncr;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiSmncBattleConfirm), nameof(UiSmncBattleConfirm.activate))]
    public class Patch_UiSmncBattleConfirm_activate
    {
        [HarmonyPrefix]
        static void Prefix(UiSmncBattleConfirm __instance, SmncFile _CurFile)
            => SimBattleLobby.OnConfirmActivate(__instance, _CurFile);
    }
}
