using HarmonyLib;
using nel;
using WeNeedMoreNoels.DataStruct;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2PrOverChargeSlot), nameof(M2PrOverChargeSlot.clearMagic), [typeof(MagicItem), typeof(bool), typeof(bool)])]
    public class Patch_M2PrOverChargeSlot_clearMagic
    {
        static void Postfix(object __instance)
        {
            M2PrOverChargeSlot slot = (M2PrOverChargeSlot)__instance;
            if (slot.Pr is PRNoel)
            {
                NotifyNoelMagic mg = new()
                {
                    Type = NotifyMagicTpe.Kill
                };
                WNMNTools.SendMagicToAllPeers(WNMNTools.LocalID, mg);
            }
        }
    }
}
