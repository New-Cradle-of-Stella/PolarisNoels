using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(NelItemManager), nameof(NelItemManager.reduceItem))]
    public class Patch_NelItemManager_reduceItem
    {
        [HarmonyPostfix]
        static void Postfix(NelItem Itm, int count, int grade)
        {
            if (!DB.IsMultiplayer || PolarisNoelsTools.ApplyingRemoteChange)
            {
                return;
            }
            string id = Itm.key;
            PolarisNoelsTools.SendLoseItemToAllPeers(id, count, grade);
        }
    }
}
