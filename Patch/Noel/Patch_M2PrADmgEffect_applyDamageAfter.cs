using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2PrADmgEffect), nameof(M2PrADmgEffect.applyDamageAfter))]
    public class Patch_M2PrADmgEffect_applyDamageAfter
    {
        [HarmonyPrefix]
        static bool Prefix(object __instance)
        {
            return __instance is PRNoel;
        }
    }
}
