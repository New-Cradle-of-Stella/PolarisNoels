using HarmonyLib;
using nel.mgm.smncr;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiSmnCreator), nameof(UiSmnCreator.fnChangedFile))]
    public class Patch_UiSmnCreator_fnChangedFile
    {
        [HarmonyPrefix]
        static bool Prefix(UiSmnCreator __instance, BtnContainerRadio<aBtn> _B, int cur_value, ref bool __result)
        {
            aBtn aBtn = _B.Get(cur_value);
            string title = aBtn.title;
            if (title == "&&multiplayer_simbattle_btn")
            {
                __instance.changeState(SimBattleLobby.State);
                __result = true;
                return false;
            }
            return true;
        }
    }
}
