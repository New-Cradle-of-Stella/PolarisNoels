using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(CoinStorage), nameof(CoinStorage.addCount), [typeof(int), typeof(CoinStorage.CTYPE), typeof(bool)])]
    public class Patch_CoinStorage_addCount
    {
        [HarmonyPostfix]
        static void Postfix(int v, CoinStorage.CTYPE ctype)
        {
            if (!DB.IsMultiplayer || WNMNTools.ApplyingRemoteChange)
            {
                return;
            }
            WNMNTools.SendGetCoinToAllPeers(ctype, v);
        }
    }
}
