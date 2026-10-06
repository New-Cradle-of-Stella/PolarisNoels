using HarmonyLib;
using m2d;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2MoverPr), nameof(M2MoverPr.isAtkO))]
    public class Patch_M2MoverPr_isAtkO
    {
        [HarmonyPrefix]
        static bool Prefix(object __instance, ref bool __result)
        {
            if (__instance is ShadowNoel noel)
            {
                __result = noel.IsAtkO;
                return false;
            }
            return true;
        }
    }
}
