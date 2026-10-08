using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    // PrPoseContainer 里有几处会按 "pxl_dir + 角色名 + .pxls.bytes.texture_0" 去游戏自己的打包资源里重新载入图集
    // （刷新主图、套用 PCC 配色、检查图集是否就绪）。由 Core 的 Res 加载的 pxls 不在那里，贴图已经由 Core 换好，
    // 所以这几处对这类容器跳过，改由 AllReady 判断就绪。

    [HarmonyPatch(typeof(PrPoseContainer), nameof(PrPoseContainer.reloadAllMainImage))]
    public static class Patch_PrPoseContainer_reloadAllMainImage
    {
        [HarmonyPrefix]
        static bool Prefix(PrPoseContainer __instance) => !MTRExtension.IsCoreLoaded(__instance);
    }

    [HarmonyPatch(typeof(PrPoseContainer), nameof(PrPoseContainer.ApplyPCCData))]
    public static class Patch_PrPoseContainer_ApplyPCCData
    {
        [HarmonyPrefix]
        static bool Prefix(PrPoseContainer __instance) => !MTRExtension.IsCoreLoaded(__instance);
    }

    [HarmonyPatch(typeof(PrPoseContainer), nameof(PrPoseContainer.checkAllImageReloaded))]
    public static class Patch_PrPoseContainer_checkAllImageReloaded
    {
        [HarmonyPrefix]
        static bool Prefix(PrPoseContainer __instance, ref bool __result)
        {
            if (!MTRExtension.IsCoreLoaded(__instance))
            {
                return true;
            }

            __result = MTRExtension.AllReady(__instance);
            return false;
        }
    }
}
