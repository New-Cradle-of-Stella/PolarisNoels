using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(MgItemBomb), nameof(MgItemBomb.run))]
    public class Patch_MgItemBomb_run
    {
        [HarmonyPostfix]
        static void Postfix(MagicItem Mg)
            => MagicBroadcaster.OnItemBombRun(Mg);
    }
}
