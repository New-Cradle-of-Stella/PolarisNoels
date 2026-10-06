using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2PrOverChargeSlot), nameof(M2PrOverChargeSlot.clearMagic), [typeof(MagicItem), typeof(bool), typeof(bool)])]
    public class Patch_M2PrOverChargeSlot_clearMagic
    {
        [HarmonyPostfix]
        static void Postfix(M2PrOverChargeSlot __instance)
            => MagicBroadcaster.OnMagicCleared(__instance);
    }
}
