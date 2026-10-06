using HarmonyLib;
using nel.mgm.smncr;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiSmncBattleConfirm), nameof(UiSmncBattleConfirm.fnClickSubmit))]
    public class Patch_UiSmncBattleConfirm_fnClickSubmit
    {
        [HarmonyPrefix]
        static bool Prefix(UiSmncBattleConfirm __instance, aBtn B)
            => SimBattleLobby.OnConfirmSubmit(__instance, B);
    }
}
