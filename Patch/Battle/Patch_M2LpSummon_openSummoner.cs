using HarmonyLib;
using nel;
using nel.mgm.smncr;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2LpSummon), nameof(M2LpSummon.openSummoner))]
    public class Patch_M2LpSummon_openSummoner
    {
        [HarmonyPrefix]
        static bool Prefix(M2LpSummon __instance)
            => BattleSession.BeforeOpen(__instance);

    }

    [HarmonyPatch(typeof(EnemySummoner), "activateInner")]
    public class Patch_EnemySummoner_activateInner
    {
        [HarmonyPrefix]
        static void Prefix(M2LpSummon _Lp) => BattleSession.BeforeActivate(_Lp);

        [HarmonyPostfix]
        static void Postfix(M2LpSummon _Lp) => BattleSession.AfterOpen(_Lp);
    }

    [HarmonyPatch(typeof(M2LpUiSmnCreator), nameof(M2LpUiSmnCreator.openSummoner), new[] { typeof(int), typeof(uint) })]
    public class Patch_M2LpUiSmnCreator_openSummoner
    {
        [HarmonyPrefix]
        static bool Prefix(M2LpUiSmnCreator __instance, ref bool __result)
        {
            if (BattleSession.BeforeOpen(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
