using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2LpSummon), nameof(M2LpSummon.openSummoner))]
    public class Patch_M2LpSummon_openSummoner
    {
        [HarmonyPrefix]
        static bool Prefix(M2LpSummon __instance)
            => BattleSession.BeforeOpen(__instance);

        [HarmonyPostfix]
        static void Postfix(M2LpSummon __instance)
            => BattleSession.AfterOpen(__instance);
    }
}
