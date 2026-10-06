using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2PrSkill), nameof(M2PrSkill.initItemBomb))]
    public class Patch_M2PrSkill_initItemBomb
    {
        [HarmonyPostfix]
        static void Postfix(M2PrSkill __instance, NelItem Itm, int grade)
            => MagicBroadcaster.OnItemBombInit(__instance, Itm, grade);
    }
}
