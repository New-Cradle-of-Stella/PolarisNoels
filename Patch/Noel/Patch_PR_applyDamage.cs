using HarmonyLib;
using m2d;
using nel;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.Patch
{//NelAttackInfo Atk, ref HITTYPE add_hittype, bool force
    [HarmonyPatch(typeof(PR), nameof(PR.applyDamage), [typeof(NelAttackInfo), typeof(HITTYPE), typeof(bool)], [ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal])]
    public class Patch_PR_applyDamage
    {
        [HarmonyPrefix]
        static void Prefix(object __instance, NelAttackInfo Atk, bool force)
        {
            if (__instance is ShadowNoel noel && noel.TryGetComponent<NetEntity>(out var entity))
            {
                entity.ReportLocalDamage(Atk._hpdmg, Atk._mpdmg);
            }
        }
    }
}
