using HarmonyLib;
using nel;
using PolarisNoels.SN;

namespace PolarisNoels.Patch
{
    // 原版的 outfit_type 只认 PRNoel（is_noel），远程玩家的 ShadowNoel 不是 PRNoel，一律得到 NORMAL。
    // 这里让它改为返回同步过来的装扮，动画器就会挑对应服装的姿势素材。
    [HarmonyPatch(typeof(PrNoelAnimator), nameof(PrNoelAnimator.outfit_type), MethodType.Getter)]
    public class Patch_PrNoelAnimator_outfit_type
    {
        [HarmonyPostfix]
        static void Postfix(PrNoelAnimator __instance, ref PRNoel.OUTFIT __result)
        {
            if (__instance.Pr is ShadowNoel shadow)
            {
                __result = shadow.Outfit;
            }
        }
    }
}
