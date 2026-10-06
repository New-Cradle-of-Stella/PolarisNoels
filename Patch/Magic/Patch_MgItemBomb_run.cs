using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(MgItemBomb), nameof(MgItemBomb.run))]
    public class Patch_MgItemBomb_run
    {
        [HarmonyPostfix]
        static void Postfix(MagicItem Mg)
            => MagicBroadcaster.OnItemBombRun(Mg);
    }
}
