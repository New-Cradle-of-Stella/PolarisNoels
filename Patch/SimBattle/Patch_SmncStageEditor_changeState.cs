using HarmonyLib;
using nel.mgm.smncr;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.changeState))]
    public class Patch_SmncStageEditor_changeState
    {
        [HarmonyPrefix]
        static bool Prefix(SmncStageEditor __instance, SmncStageEditor.STATE stt)
        {
            if (PolarisNoelsTools.IsSettingSpawnLocation && stt == SmncStageEditor.STATE.LIST && __instance.state == SmncStageEditor.STATE.MOVE)
            {
                PolarisNoelsTools.ResumeUSBCPage();
                PolarisNoelsTools.IsSettingSpawnLocation = false;
                return false;
            }
            return true;
        }
    }
}
