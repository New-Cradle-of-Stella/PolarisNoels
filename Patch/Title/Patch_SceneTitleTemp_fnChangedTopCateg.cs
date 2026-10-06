using HarmonyLib;
using nel.title;
using System.Reflection;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SceneTitleTemp), nameof(SceneTitleTemp.fnChangedTopCateg))]
    public class Patch_SceneTitleTemp_fnChangedTopCateg
    {
        [HarmonyPostfix]
        static void Postfix(object __instance, BtnContainerRadio<aBtn> _B, int pre_value, int cur_value)
        {
            if (cur_value < 0)
            {
                return;
            }
            string title = _B.Get(cur_value).title;
            if (title == "&&btn_multiplayer")
            {
                DB.WNMNUIClicking = true;
                MethodInfo method = AccessTools.Method(typeof(SceneTitleTemp), "changeState");
                method.Invoke(__instance, [SceneTitleTemp.STATE.DIFF_SELECT]);
            }
        }
    }
}
