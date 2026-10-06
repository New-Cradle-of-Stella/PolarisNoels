using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2PrSkill), nameof(M2PrSkill.reawakeMagic))]
    public class Patch_M2PrSkill_reawakeMagic
    {
        [HarmonyPostfix]
        static void Postfix(M2PrSkill __instance, MGKIND kind)
            => MagicBroadcaster.OnMagicReawake(__instance, kind);
    }
}
