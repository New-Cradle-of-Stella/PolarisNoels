using HarmonyLib;
using nel;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(NelPlayerCursor), nameof(NelPlayerCursor.initMagic))]
    public class Patch_NelPlayerCursor_initMagic
    {
        [HarmonyPostfix]
        static void Postfix(object __instance, MagicItem _Mg)
        {
            NelPlayerCursor cursor = (NelPlayerCursor)__instance;
            if (cursor.Pr is ShadowNoel noel)
            {
                cursor.hold_aim = noel.Magic.HoldAim;
                cursor.pre_hold_aim = noel.Magic.HoldAim;
                cursor.fineAim0(_Mg);
            }
        }
    }
}
