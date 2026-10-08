using HarmonyLib;
using nel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PolarisNoels.Patch
{
    [HarmonyPatch]
    public class Patch_NelEnemy_applyDamage
    {
        // Guard overrides as well as the base implementation: bosses can change phases,
        // parry damage to a parent, or run effects before/after base.applyDamage.
        internal static IEnumerable<MethodBase> DamageMethods(System.Type resultType, params string[] names)
        {
            return AccessTools.GetTypesFromAssembly(typeof(NelEnemy).Assembly)
                .Where(type => typeof(NelEnemy).IsAssignableFrom(type))
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(method => names.Contains(method.Name) && method.ReturnType == resultType && !method.IsAbstract);
        }

        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => DamageMethods(typeof(int),
            nameof(NelEnemy.applyDamage), "applyHpDamage", "applyMpDamage");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(NelEnemy __instance, MethodBase __originalMethod, object[] __args, ref int __result, out CombatSync.Scope __state)
        {
            bool allow = CombatSync.Before(__instance, __originalMethod, __args, out __state);
            if (allow && DamageAuthority.CanDamage(__instance)) return true;
            DamageAuthority.ClearOutputs(__originalMethod, __args);
            __result = 0;
            return false;
        }
        [HarmonyPostfix] static void Postfix(CombatSync.Scope __state, int __result, object[] __args) => CombatSync.Returned(__state, __result, __args);
        [HarmonyFinalizer] static System.Exception Finalizer(CombatSync.Scope __state, System.Exception __exception) => CombatSync.Finish(__state, __exception);
    }

    [HarmonyPatch]
    public class Patch_NelEnemy_damageStatus
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => Patch_NelEnemy_applyDamage.DamageMethods(typeof(bool),
            "applySlipDamage", "applyPressDamage", "applySerDamage");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(NelEnemy __instance, MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (DamageAuthority.CanDamage(__instance)) return true;
            DamageAuthority.ClearOutputs(__originalMethod, __args);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    public class Patch_NelEnemy_applyGasDamage
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => Patch_NelEnemy_applyDamage.DamageMethods(typeof(void), "applyGasDamage");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(NelEnemy __instance) => DamageAuthority.CanDamage(__instance);
    }
}
