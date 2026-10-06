using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(NelEnemy), nameof(NelEnemy.initDeathEffect))]
    public class Patch_NelEnemy_initDeathEffect
    {
        [HarmonyPostfix]
        static void Postfix(NelEnemy __instance) => BattleSession.ObserveEnemy(__instance);
    }

    [HarmonyPatch(typeof(NelEnemy), nameof(NelEnemy.destruct))]
    public class Patch_NelEnemy_destruct
    {
        [HarmonyPrefix]
        static void Prefix(NelEnemy __instance) => BattleSession.ObserveEnemy(__instance);
    }
}
