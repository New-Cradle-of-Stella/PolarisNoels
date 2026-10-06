using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(MgWaterShard), nameof(MgWaterShard.fnManipulateWaterShard))]
    public class Patch_MgWaterShard_fnManipulateWaterShard
    {
        [HarmonyPostfix]
        static void Postfix(MagicItem Mg, M2MagicCaster _Mv)
            => MagicBroadcaster.OnWaterShardManipulate(Mg, _Mv);
    }
}
