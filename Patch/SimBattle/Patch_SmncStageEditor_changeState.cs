using HarmonyLib;
using nel.mgm.smncr;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.changeState))]
    public class Patch_SmncStageEditor_changeState
    {
        [HarmonyPrefix]
        static bool Prefix(SmncStageEditor __instance, SmncStageEditor.STATE stt)
        {
            if (WNMNTools.IsSettingSpawnLocation && stt == SmncStageEditor.STATE.LIST && __instance.state == SmncStageEditor.STATE.MOVE)
            {
                WNMNTools.ResumeUSBCPage();
                WNMNTools.IsSettingSpawnLocation = false;
                return false;
            }
            return true;
        }
    }
}
