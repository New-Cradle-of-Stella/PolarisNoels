using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2LpSummon), nameof(M2LpSummon.closeSummoner))]
    public class Patch_M2LpSummon_closeSummoner
    {
        [HarmonyPostfix]
        static void Postfix(M2LpSummon __instance, bool defeated)
            => BattleSession.AfterClose(__instance, defeated);
    }
}
