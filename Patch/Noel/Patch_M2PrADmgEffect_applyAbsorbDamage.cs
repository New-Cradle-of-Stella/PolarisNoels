using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2PrADmgEffect), nameof(M2PrADmgEffect.applyAbsorbDamage))]
    public class Patch_M2PrADmgEffect_applyAbsorbDamage
    {
        [HarmonyPrefix]
        static bool Prefix(object __instance)
        {
            return __instance is PRNoel;
        }
    }
}
