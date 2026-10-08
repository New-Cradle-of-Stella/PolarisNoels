using HarmonyLib;
using nel;
using nel.smnp;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(SummonerPlayer), nameof(SummonerPlayer.summonNewEnemy))]
    public class Patch_SummonerPlayer_summonNewEnemy
    {
        [HarmonyPrefix]
        static bool Prefix(SummonerPlayer __instance, ref NelEnemy __result, SmnEnemyKind K, out EnemySummonSync.SummonState __state)
        {
            bool run = EnemySummonSync.BeforeSummon(K, ref __result, out __state);
            // Native keys include this index. Skipping ordinary spawns must not shift
            // the identities of later bosses/parts relative to the owner's objects.
            if (!run) for (var current = K; current != null; current = current.DupeConnect) __instance.enemy_index++;
            return run;
        }

        [HarmonyPostfix]
        static void Postfix(NelEnemy __result, SmnEnemyKind K, EnemySummonSync.SummonState __state)
            => EnemySummonSync.AfterSummon(__result, K, __state);
        [HarmonyFinalizer]
        static Exception Finalizer(EnemySummonSync.SummonState __state, Exception __exception)
        { EnemySummonSync.AfterSummon(null, null, __state); return __exception; }
    }
    [HarmonyPatch]
    public class Patch_SummonerPlayer_registerEachEnemy
    {
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods() => AccessTools.GetTypesFromAssembly(typeof(SummonerPlayer).Assembly)
            .Where(type => typeof(SummonerPlayer).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == "initSummonedPost" && !method.IsAbstract);
        [HarmonyPostfix]
        static void Postfix(object[] __args) => EnemySummonSync.RegisterSummoned((NelEnemy)__args[0], (SmnEnemyKind)__args[1]);
    }
}
