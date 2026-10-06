using HarmonyLib;
using m2d;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(NelEnemy), nameof(NelEnemy.applyDamage), [typeof(NelAttackInfo), typeof(HITTYPE), typeof(bool)], [ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal])]
    public class Patch_NelEnemy_applyDamage
    {
        [HarmonyPostfix]
        static void Postfix(NelEnemy __instance, NelAttackInfo Atk)
        {
            if (__instance == null)
            {
                return;
            }
            if (__instance.TryGetComponent<NetEntity>(out var entity))
            {
                entity.ReportLocalDamage(Atk.hpdmg0, Atk.mpdmg0);
            }
        }
    }
}
