using HarmonyLib;
using nel.mgm.smncr;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.decideMakingStgo))]
    public class Patc1h_SmncStageEditor_decideMakingStgo
    {
        [HarmonyPrefix]
        static bool Prefix(SmncStageEditor __instance, ref bool __result)
        {
            if (PolarisNoelsTools.IsSettingSpawnLocation)
            {
                __result = true;
                SmncStageEditorManager.StgObject stgObject = __instance.StgoMaking;
                PolarisNoelsTools.SettingResult = new(stgObject.x, stgObject.y);
                return false;
            }
            return true;
        }
    }
}
