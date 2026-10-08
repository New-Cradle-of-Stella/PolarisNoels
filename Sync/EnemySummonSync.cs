using nel;
using nel.smnp;
using System.Collections.Generic;

namespace PolarisNoels
{
    /// <summary>敌人的唯一权威是发起者；Boss 本地表现保留原版父子对象图，不拥有结算权。</summary>
    public static class EnemySummonSync
    {
        public class SummonState
        {
            public bool IsBoss;
            public bool CanSync;
            public bool NativeReplica;
        }
        static readonly List<SummonState> active = new();

        public static bool BeforeSummon(SmnEnemyKind K, ref NelEnemy result, out SummonState state)
        {
            if (!DB.IsMultiplayer)
            {
                state = null;
                return true;
            }
            bool boss = IsSyncBoss(K.enemyid);
            bool native = false;
            for (var current = K; current != null; current = current.DupeConnect)
                native |= IsSyncBoss(current.enemyid) || BelongsToBoss(current.enemyid) || current.no_add_appear_count;
            state = new() { IsBoss = boss, CanSync = BattleSession.IsAuthority, NativeReplica = native };
            if (state.CanSync || native) { active.Add(state); return true; }
            result = null;
            return false;
        }

        public static void AfterSummon(NelEnemy enemy, SmnEnemyKind K, SummonState state)
        {
            if (state != null) active.Remove(state);
        }
        public static void RegisterSummoned(NelEnemy enemy, SmnEnemyKind K)
        {
            if (!DB.IsMultiplayer || !DB.IsInBattle || enemy == null || enemy.TryGetComponent<NetEntity>(out _)) return;
            if (!BattleSession.IsAuthority)
            {
                EntityFactory.AttachNativeEnemyReplica(enemy, K.enemyid, enemy is NelEnemyBoss);
                return;
            }
            if (!DB.CurEnemies.Contains(enemy)) DB.CurEnemies.Add(enemy);
            bool native = active.Count > 0 && active[active.Count - 1].NativeReplica;
            EntityFactory.AttachAuthorityEnemy(enemy, K.enemyid, enemy is NelEnemyBoss, native);
        }

        public static bool IsSyncBoss(string key)
        {
            var type = NDAT.getTypeAndId(key).EnemyType;
            return type == typeof(NelNBoss_Nusi) || type == typeof(NelNBossSpider);
        }

        public static bool BelongsToBoss(string key) => key.ToLowerInvariant().Contains("boss");
    }
}
