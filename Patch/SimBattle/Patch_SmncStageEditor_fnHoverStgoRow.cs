using HarmonyLib;
using nel.mgm.smncr;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SmncStageEditor), nameof(SmncStageEditor.fnHoverStgoRow))]
    public class Patch_SmncStageEditor_fnHoverStgoRow
    {
        [HarmonyPrefix]
        static void Prefix(SmncStageEditor __instance, aBtn B)
        {
            if (!DB.IsMultiplayer)
            {
                return;
            }
            if (B.title == "0" && !WNMNTools.IsSettingSpawnLocation)
            {
                B.SetLocked(true);
                UiMenuMul.BxSSI.activate();
                UiMenuMul.BxSSI.Focusable(true, true, null);
                UiMenuMul.BxSSI.Focus();
            }
            else
            {
                UiMenuMul.BxSSI.deactivate();
            }
            WNMNTools.SSE = __instance;
        }
    }
}
