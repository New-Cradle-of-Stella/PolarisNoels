using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(NelItemManager), nameof(NelItemManager.reduceItem))]
    public class Patch_NelItemManager_reduceItem
    {
        [HarmonyPostfix]
        static void Postfix(NelItem Itm, int count, int grade)
        {
            if (!DB.IsMultiplayer || WNMNTools.ApplyingRemoteChange)
            {
                return;
            }
            string id = Itm.key;
            WNMNTools.SendLoseItemToAllPeers(id, count, grade);
        }
    }
}
