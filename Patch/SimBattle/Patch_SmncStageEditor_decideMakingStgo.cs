using HarmonyLib;
using nel.mgm.smncr;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.decideMakingStgo))]
    public class Patc1h_SmncStageEditor_decideMakingStgo
    {
        [HarmonyPrefix]
        static bool Prefix(SmncStageEditor __instance, ref bool __result)
        {
            if (WNMNTools.IsSettingSpawnLocation)
            {
                __result = true;
                SmncStageEditorManager.StgObject stgObject = __instance.StgoMaking;
                WNMNTools.SettingResult = new(stgObject.x, stgObject.y);
                return false;
            }
            return true;
        }
    }
}
