using HarmonyLib;
using nel.title;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SceneTitleTemp), nameof(SceneTitleTemp.changeState))]
    public class Patch_SceneTitleTemp_changeState
    {
        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            SceneTitleTemp stt = (SceneTitleTemp)__instance;
            if (DB.WNMNUIClicking && stt.state == SceneTitleTemp.STATE.DIFF_SELECT)
            {
                stt.BxDiff.destruct(true);
                stt.BxDiff = new UITitleMultiplayerConfirm(null, -4.25f, stt, 0, 2);
                DB.WNMNUIClicking = false;
            }
            if (stt.state == SceneTitleTemp.STATE.TOP)
            {
                DB.WNMNHostSelectSVD = false;
            }
        }
    }
}
