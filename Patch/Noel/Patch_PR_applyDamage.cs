using HarmonyLib;
using nel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PolarisNoels.Patch
{
    [HarmonyPatch]
    public class Patch_PR_applyDamage
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => typeof(PR)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => new[] { nameof(PR.applyDamage), "applyWaterChokeDamage", "applyYdrgDamage", "applyPressDamage", "applyMpDamage" }
                .Contains(method.Name) && method.ReturnType == typeof(int));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(PR __instance, MethodBase __originalMethod, object[] __args, ref int __result, out CombatSync.Scope __state)
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

    // Map hazards and some skills enter the damage assistant directly, bypassing PR.
    [HarmonyPatch]
    public class Patch_M2PrADmg_applyDamage
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => typeof(M2PrADmg)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => (method.Name == nameof(M2PrADmg.applyDamage) || method.Name == "applyHpDamageSimple")
                && method.ReturnType == typeof(int));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(M2PrADmg __instance, MethodBase __originalMethod, object[] __args, ref int __result, out CombatSync.Scope __state)
        {
            bool allow = CombatSync.Before(__instance.Pr, __originalMethod, __args, out __state);
            if (allow && DamageAuthority.CanDamage(__instance.Pr)) return true;
            DamageAuthority.ClearOutputs(__originalMethod, __args);
            __result = 0;
            return false;
        }
        [HarmonyPostfix] static void Postfix(CombatSync.Scope __state, int __result, object[] __args) => CombatSync.Returned(__state, __result, __args);
        [HarmonyFinalizer] static System.Exception Finalizer(CombatSync.Scope __state, System.Exception __exception) => CombatSync.Finish(__state, __exception);
    }

    [HarmonyPatch]
    public class Patch_PR_damageStatus
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => typeof(PR)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => (method.Name == "applySlipDamage" || method.Name == "applyPressDamage")
                && method.ReturnType == typeof(bool));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(PR __instance, MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (DamageAuthority.CanDamage(__instance)) return true;
            DamageAuthority.ClearOutputs(__originalMethod, __args);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    public class Patch_PR_damageEffects
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => typeof(PR)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => new[] { "applyGasDamage", "applyAbsorbDamage", "applyParalysisDamage" }.Contains(method.Name)
                && method.ReturnType == typeof(void));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(PR __instance, MethodBase __originalMethod, object[] __args, out CombatSync.Scope __state)
            => CombatSync.Before(__instance, __originalMethod, __args, out __state) && DamageAuthority.CanDamage(__instance);
        [HarmonyFinalizer] static System.Exception Finalizer(CombatSync.Scope __state, System.Exception __exception) => CombatSync.Finish(__state, __exception);
    }

    [HarmonyPatch]
    public class Patch_M2PrADmg_damageEffects
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => typeof(M2PrADmg)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => (method.Name == "applyAbsorbDamage" || method.Name == "applyWormTrapDamage")
                && method.ReturnType == typeof(void));

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(M2PrADmg __instance, MethodBase __originalMethod, object[] __args, out CombatSync.Scope __state)
            => CombatSync.Before(__instance.Pr, __originalMethod, __args, out __state) && DamageAuthority.CanDamage(__instance.Pr);
        [HarmonyFinalizer] static System.Exception Finalizer(CombatSync.Scope __state, System.Exception __exception) => CombatSync.Finish(__state, __exception);
    }
}
