using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2PrADmgEffect), nameof(M2PrADmgEffect.applyAbsorbDamage))]
    public class Patch_M2PrADmgEffect_applyAbsorbDamage
    {
        [HarmonyPrefix]
        static bool Prefix(M2PrADmgEffect __instance)
        {
            return DamageAuthority.CanDamage(__instance.Pr);
        }
    }
}
