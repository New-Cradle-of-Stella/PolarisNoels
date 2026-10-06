using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(nameof(MgWaterShard), nameof(MgWaterShard.checkExistAlready))]
    public class Patch_MgWaterShard_checkExistAlready
    {
        [HarmonyPrefix]
        static bool Prefix(M2MagicCaster Caster)
        {
            if (Caster is not PRNoel)
            {
                return false;
            }
            return true;
        }
    }
}
