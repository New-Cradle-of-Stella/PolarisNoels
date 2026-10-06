using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(MagicItem), nameof(MagicItem.calcAimPos))]
    public class Patch_MagicItem_calcAimPos
    {
        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            MagicItem item = (MagicItem)__instance;
            if (ShadowNoelMagic.TryGetOwner(item, out var magic))
            {
                item.aim_agR = magic.AimAgR;
            }
        }
    }
}
