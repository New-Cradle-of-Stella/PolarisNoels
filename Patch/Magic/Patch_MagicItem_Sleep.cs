using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(MagicItem), nameof(MagicItem.Sleep))]
    public class Patch_MagicItem_Sleep
    {
        [HarmonyPostfix]
        static void Postfix(MagicItem __instance)
            => MagicBroadcaster.OnMagicSleep(__instance);
    }
}
