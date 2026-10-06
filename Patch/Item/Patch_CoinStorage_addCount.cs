using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(CoinStorage), nameof(CoinStorage.addCount), [typeof(int), typeof(CoinStorage.CTYPE), typeof(bool)])]
    public class Patch_CoinStorage_addCount
    {
        [HarmonyPostfix]
        static void Postfix(int v, CoinStorage.CTYPE ctype)
        {
            if (!DB.IsMultiplayer || PolarisNoelsTools.ApplyingRemoteChange)
            {
                return;
            }
            PolarisNoelsTools.SendGetCoinToAllPeers(ctype, v);
        }
    }
}
