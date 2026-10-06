using HarmonyLib;
using nel;
using nel.mgm.smncr;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2LpSummon), nameof(M2LpSummon.closeSummoner))]
    public class Patch_M2LpSummon_closeSummoner
    {
        [HarmonyPrefix]
        static bool Prefix(M2LpSummon __instance, bool defeated, ref bool is_first_defeat, out bool __state)
        {
            __state = BattleSession.CanClose(__instance, defeated);
            if (!__state) is_first_defeat = false;
            return __state;
        }

        [HarmonyPostfix]
        static void Postfix(M2LpSummon __instance, bool defeated, bool __state)
        {
            if (__state) BattleSession.AfterClose(__instance, defeated);
        }
    }

    [HarmonyPatch(typeof(M2LpUiSmnCreator), nameof(M2LpUiSmnCreator.closeSummoner))]
    public class Patch_M2LpUiSmnCreator_closeSummoner
    {
        [HarmonyPrefix]
        static bool Prefix(M2LpUiSmnCreator __instance, bool defeated, ref bool is_first_defeat)
        {
            if (BattleSession.CanClose(__instance, defeated)) return true;
            is_first_defeat = false;
            return false;
        }
    }
}
