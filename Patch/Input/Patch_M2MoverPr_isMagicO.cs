using HarmonyLib;
using m2d;
using PolarisNoels.SN;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(M2MoverPr), nameof(M2MoverPr.isMagicO))]
    public class Patch_M2MoverPr_isMagicO
    {
        [HarmonyPrefix]
        static bool Prefix(object __instance, ref bool __result)
        {
            if (__instance is ShadowNoel noel)
            {
                __result = noel.Magic.Chanting;
                return false;
            }
            return true;
        }
    }
}
