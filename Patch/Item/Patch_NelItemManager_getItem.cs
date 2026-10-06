using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(NelItemManager), nameof(NelItemManager.getItem))]
    public class Patch_NelItemManager_getItem
    {
        [HarmonyPostfix]
        static void Postfix(NelItem Itm, int count, int grade)
        {
            if (!DB.IsMultiplayer || PolarisNoelsTools.ApplyingRemoteChange)
            {
                return;
            }
            string id = Itm.key;
            PolarisNoelsTools.SendGetItemToAllPeers(id, count, grade);
        }
    }
}
