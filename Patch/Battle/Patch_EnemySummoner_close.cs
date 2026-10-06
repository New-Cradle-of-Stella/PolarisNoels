using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(EnemySummoner), nameof(EnemySummoner.close))]
    public class Patch_EnemySummoner_close
    {
        [HarmonyPrefix]
        static bool Prefix(EnemySummoner __instance, ref EnemySummoner __result)
        {
            if (WNMNTools.SyncType != EnemySyncType.StarterOnly && WNMNTools.HasSyncEnemy())
            {
                __result = __instance;
                return false;
            }
            return true;
        }
    }
}
