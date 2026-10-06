using nel;
using nel.smnp;

namespace PolarisNoels
{
    /// <summary>召唤敌人时的联机同步决策：是否可同步、是否为 Boss，以及同步组件的挂载。</summary>
    public static class EnemySummonSync
    {
        public class SummonState
        {
            public bool IsBoss;

            public bool CanSync;
        }

        /// <summary>召唤前判定。返回 false 表示拦截原版召唤（此时 result 已置为 null）。</summary>
        public static bool BeforeSummon(SmnEnemyKind K, ref NelEnemy result, out SummonState state)
        {
            if (!DB.IsMultiplayer)
            {
                state = null;
                return true;
            }
            if (IsSyncBoss(K.enemyid))
            {
                state = new() { IsBoss = true, CanSync = true };
                return true;
            }
            bool belongBoss = BelongsToBoss(K.enemyid);
            if (PolarisNoelsTools.SyncType == EnemySyncType.StarterOnly && PolarisNoelsTools.BattleStarterID != PolarisNoelsTools.LocalID)
            {
                if (belongBoss)
                {
                    state = new() { IsBoss = true, CanSync = false };
                    return true;
                }
                result = null;
                state = new() { IsBoss = false, CanSync = false };
                return false;
            }
            state = new() { IsBoss = false, CanSync = !belongBoss };
            return true;
        }

        /// <summary>召唤后登记同步主机，并通知其他玩家。</summary>
        public static void AfterSummon(NelEnemy enemy, SmnEnemyKind K, SummonState state)
        {
            if (!DB.IsMultiplayer || state == null || !state.CanSync)
            {
                return;
            }
            if (state.IsBoss)
            {
                if (PolarisNoelsTools.BattleStarterID == PolarisNoelsTools.LocalID && IsSyncBoss(K.enemyid))
                {
                    RegisterHost(enemy, K.enemyid, true);
                }
                return;
            }
            RegisterHost(enemy, K.enemyid, false);
        }

        static void RegisterHost(NelEnemy enemy, string enemyId, bool isBoss)
        {
            DB.CurEnemies.Add(enemy);
            EntityFactory.AttachAuthorityEnemy(enemy, enemyId, isBoss);
        }

        /// <summary>需要单独同步的 Boss 本体。</summary>
        public static bool IsSyncBoss(string key)
        {
            var type = NDAT.getTypeAndId(key).EnemyType;
            return type == typeof(NelNBoss_Nusi) || type == typeof(NelNBossSpider);
        }

        /// <summary>属于 Boss 战的附属敌人（随 Boss 一起处理，不单独同步）。</summary>
        public static bool BelongsToBoss(string key)
        {
            return key.ToLower().Contains("boss");
        }
    }
}
