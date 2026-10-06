using HarmonyLib;
using nel;
using XX;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(NightController), nameof(NightController.SummonerDefeated))]
    public class Patch_NightController_SummonerDefeated
    {
        [HarmonyPrefix]
        static void Prefix(ref int ob_add)
        {
            switch (PolarisNoelsTools.SyncType)
            {
                case EnemySyncType.StarterOnly:
                    break;
                case EnemySyncType.SmartAverage:
                    float factor = 1 + 0.25f * (PolarisNoelsTools.TotalBattleNoelCount - 1);
                    ob_add = X.IntU(ob_add * factor);
                    break;
                case EnemySyncType.Independent:
                    ob_add *= PolarisNoelsTools.TotalBattleNoelCount;
                    break;
            }
        }
    }
}
