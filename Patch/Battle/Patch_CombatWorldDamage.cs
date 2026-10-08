using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using m2d;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch]
    public class Patch_CombatWorldDamage
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(M2Mover).Assembly, typeof(NelEnemy).Assembly }.Distinct()
            .SelectMany(AccessTools.GetTypesFromAssembly)
            .Where(type => typeof(M2Mover).IsAssignableFrom(type) && typeof(IM2RayHitAble).IsAssignableFrom(type) && !typeof(NelEnemy).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => (method.Name == "applyHpDamage" || method.Name == "applyMpDamage") && method.ReturnType == typeof(int) && !method.IsAbstract
                && method.GetParameters().Length == 3 && method.GetParameters()[2].ParameterType == typeof(AttackInfo));
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool Prefix(M2Mover __instance, MethodBase __originalMethod, object[] __args, ref int __result, out CombatSync.Scope __state)
        {
            if (CombatSync.Before(__instance, __originalMethod, __args, out __state)) return true;
            DamageAuthority.ClearOutputs(__originalMethod, __args); __result = 0; return false;
        }
        [HarmonyPostfix] static void Postfix(CombatSync.Scope __state, int __result, object[] __args) => CombatSync.Returned(__state, __result, __args);
        [HarmonyFinalizer] static Exception Finalizer(CombatSync.Scope __state, Exception __exception) => CombatSync.Finish(__state, __exception);
    }

    [HarmonyPatch(typeof(M2BreakableWallMover), nameof(M2BreakableWallMover.runPre))]
    public class Patch_CombatWorldTimers
    {
        // Fall/fire HP ticks and auto-revert timers belong to the same owner as hits.
        [HarmonyPrefix] static bool Prefix(M2BreakableWallMover __instance) => CombatSync.Owns(__instance);
    }
    [HarmonyPatch]
    public class Patch_CombatParry
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => Patch_NelEnemy_applyDamage.DamageMethods(typeof(bool), nameof(NelEnemy.applyParry));
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool Prefix(NelEnemy __instance, object[] __args, ref bool __result)
        {
            if (CombatSync.BeforeParry(__instance, (XX.AIM)__args[0])) return true;
            __result = true; return false;
        }
    }
    [HarmonyPatch(typeof(M2ShieldHitable), nameof(M2ShieldHitable.applyHpDamage))]
    public class Patch_CombatShieldDamage
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool Prefix(M2ShieldHitable __instance, MethodBase __originalMethod, object[] __args, ref int __result, out CombatSync.Scope __state)
        {
            __state = null;
            if (__instance.Sld?.Mv is not PR target) return true;
            if (CombatSync.Before(target, __originalMethod, __args, out __state, DataStruct.CombatMode.Shield)) return true;
            __result = 0; return false;
        }
        [HarmonyPostfix] static void Postfix(CombatSync.Scope __state, int __result, object[] __args) => CombatSync.Returned(__state, __result, __args);
        [HarmonyFinalizer] static Exception Finalizer(CombatSync.Scope __state, Exception __exception) => CombatSync.Finish(__state, __exception);
    }
}
