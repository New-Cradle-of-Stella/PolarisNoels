using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(EnemySummoner), nameof(EnemySummoner.close))]
    public class Patch_EnemySummoner_close
    {
        [HarmonyPrefix]
        static bool Prefix(EnemySummoner __instance, ref EnemySummoner __result)
        {
            if (PolarisNoelsTools.SyncType != EnemySyncType.StarterOnly && PolarisNoelsTools.HasSyncEnemy())
            {
                __result = __instance;
                return false;
            }
            return true;
        }
    }
}
