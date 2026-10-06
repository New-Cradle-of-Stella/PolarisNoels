using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(EnemySummoner), nameof(EnemySummoner.close))]
    public class Patch_EnemySummoner_close
    {
        [HarmonyPrefix]
        static bool Prefix(EnemySummoner __instance, bool defeated, ref EnemySummoner __result)
        {
            if (!BattleSession.CanClose(__instance, defeated))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }
}
