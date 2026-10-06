using HarmonyLib;
using nel.mgm.smncr;
using XX;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.fnChangedListStgo))]
    public class Patch_SmncStageEditor_fnChangedListStgo
    {
        [HarmonyPrefix]
        static bool Prefix(int cur_value)
        {
            if (!DB.IsMultiplayer || PolarisNoelsTools.IsSettingSpawnLocation)
            {
                return true;
            }
            if (cur_value == 0)
            {
                SND.Ui.play("locked", false);
                return false;
            }
            return true;
        }
    }
}
