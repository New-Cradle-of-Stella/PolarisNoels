using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(MTR), nameof(MTR.initPrAnimatorPxl))]
    public class Patch_MTR_initPrAnimatorPxl
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            MTRExtension.LoadAllPxls();
        }
    }
}
