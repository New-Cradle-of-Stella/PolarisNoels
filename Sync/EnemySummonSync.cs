using nel;
using nel.smnp;

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

        public static bool BeforeSummon(SmnEnemyKind K, ref NelEnemy result, out SummonState state)
        {
            if (!DB.IsMultiplayer)
            {
                state = null;
                return true;
            }
            bool boss = IsSyncBoss(K.enemyid);
            bool native = boss || BelongsToBoss(K.enemyid) || K.no_add_appear_count;
            state = new() { IsBoss = boss, CanSync = BattleSession.IsAuthority, NativeReplica = native };
            if (state.CanSync || native) return true;
            result = null;
            return false;
        }

        public static void AfterSummon(NelEnemy enemy, SmnEnemyKind K, SummonState state)
        {
            if (!DB.IsMultiplayer || !DB.IsInBattle || enemy == null || state == null) return;
            if (!state.CanSync)
            {
                EntityFactory.AttachNativeEnemyReplica(enemy, K.enemyid, enemy is NelEnemyBoss);
                return;
            }
            DB.CurEnemies.Add(enemy);
            EntityFactory.AttachAuthorityEnemy(enemy, K.enemyid, enemy is NelEnemyBoss, state.NativeReplica);
        }

        public static bool IsSyncBoss(string key)
        {
            var type = NDAT.getTypeAndId(key).EnemyType;
            return type == typeof(NelNBoss_Nusi) || type == typeof(NelNBossSpider);
        }

        public static bool BelongsToBoss(string key) => key.ToLowerInvariant().Contains("boss");
    }
}
