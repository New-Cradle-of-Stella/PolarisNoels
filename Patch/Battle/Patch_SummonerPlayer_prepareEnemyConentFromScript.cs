using HarmonyLib;
using nel.smnp;
using System.Collections.Generic;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SummonerPlayer), nameof(SummonerPlayer.prepareEnemyConentFromScript))]
    public class Patch_SummonerPlayer__prepareEnemyConentFromScript
    {
        [HarmonyPostfix]
        static void Postfix(SummonerPlayer __instance, List<SmnEnemyKind> AKindL)
        {
            if (WNMNTools.TotalBattleNoelCount == 1 || WNMNTools.SyncType != EnemySyncType.SmartAverage)
            {
                return;
            }
            float factor = 1f + 0.25f * (WNMNTools.TotalBattleNoelCount - 1);
            factor /= WNMNTools.TotalBattleNoelCount;
            __instance.max_enemy_appear_whole = X.IntC(__instance.max_enemy_appear_whole * factor);
            foreach (SmnEnemyKind k in AKindL)
            {
                k.def_count = X.IntC(k.def_count * factor);
            }
        }
    }
}
