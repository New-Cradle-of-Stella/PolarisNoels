using HarmonyLib;
using nel;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(M2PrSkill), nameof(M2PrSkill.magicProgressable))]
    public class Patch_M2PrSkill_magicProgressable
    {
        [HarmonyPrefix]
        static bool Prefix(object __instance, ref bool __result)
        {
            if (__instance is ShadowNoel)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
