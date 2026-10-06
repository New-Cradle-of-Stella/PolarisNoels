using HarmonyLib;
using nel.mgm.smncr;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.fnChangedListStgo))]
    public class Patch_SmncStageEditor_fnChangedListStgo
    {
        [HarmonyPrefix]
        static bool Prefix(int cur_value)
        {
            if (!DB.IsMultiplayer || WNMNTools.IsSettingSpawnLocation)
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
