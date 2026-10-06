using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(BetobetoManager), nameof(BetobetoManager.Check), [typeof(PR), typeof(NelAttackInfo), typeof(bool), typeof(bool)])]
    public class Patch_BetobetoManager_Check
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            return !DB.IsMultiplayer;
        }
    }
}
