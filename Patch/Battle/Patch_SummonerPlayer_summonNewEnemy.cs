using HarmonyLib;
using nel;
using nel.smnp;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SummonerPlayer), nameof(SummonerPlayer.summonNewEnemy))]
    public class Patch_SummonerPlayer_summonNewEnemy
    {
        [HarmonyPrefix]
        static bool Prefix(ref NelEnemy __result, SmnEnemyKind K, out EnemySummonSync.SummonState __state)
            => EnemySummonSync.BeforeSummon(K, ref __result, out __state);

        [HarmonyPostfix]
        static void Postfix(NelEnemy __result, SmnEnemyKind K, EnemySummonSync.SummonState __state)
            => EnemySummonSync.AfterSummon(__result, K, __state);
    }
}
