using HarmonyLib;
using nel;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.Patch
{
    /// <summary>影子玩家的魔法由 ShadowNoelMagic 驱动，不允许自己进入休眠流程。</summary>
    [HarmonyPatch(typeof(M2PrSkill), nameof(M2PrSkill.initMagicSleep))]
    public class Patch_M2PrSkill_initMagicSleep
    {
        [HarmonyPrefix]
        static bool Prefix(M2PrSkill __instance)
            => __instance.Pr is not ShadowNoel;
    }
}
