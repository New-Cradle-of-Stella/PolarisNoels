using HarmonyLib;
using nel;
using System.Collections.Generic;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiCFG), nameof(UiCFG.PrepareEntries))]
    public static class Patch_UiCFG_PrepareEntries
    {
        [HarmonyPostfix]
        static void Postfix(List<CfgEntry> A, UiCFG.CATEG categ)
        {
            if (DB.IsMultiplayer && categ == UiCFG.CATEG.general)
            {
                CFGMultiplayer.PrepareEntries(A);
            }
        }
    }

    [HarmonyPatch(typeof(UiCFG), nameof(UiCFG.changeConfigValue))]
    public static class Patch_UiCFG_changeConfigValue
    {
        [HarmonyPrefix]
        static bool Prefix(string key, float cur_value, ref bool __result)
        {
            if (CFGMultiplayer.ChangeConfigValue(key, cur_value))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
