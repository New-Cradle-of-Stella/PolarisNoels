using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(CoinStorage), nameof(CoinStorage.reduceCount))]
    public class Patch_CoinStorage_reduceCount
    {
        [HarmonyPostfix]
        static void Postfix(int v, CoinStorage.CTYPE ctype)
        {
            if (!DB.IsMultiplayer || PolarisNoelsTools.ApplyingRemoteChange)
            {
                return;
            }
            PolarisNoelsTools.SendLoseCoinToAllPeers(ctype, v);
        }
    }
}
