using System.Collections.Generic;
using System.Linq;
using PolarisNoels.DataStruct;

namespace PolarisNoels
{
    /// <summary>当前存活的所有 <see cref="NetEntity"/>。取代原来的 SyncHosts / SyncClients / peerClients。</summary>
    public static class EntityRegistry
    {
        static readonly Dictionary<int, NetEntity> entities = [];

        public static void Register(NetEntity entity)
        {
            entities[entity.Id] = entity;
        }

        public static void Unregister(NetEntity entity)
        {
            if (entities.TryGetValue(entity.Id, out NetEntity current) && current == entity)
            {
                entities.Remove(entity.Id);
            }
        }

        public static bool TryGet(int id, out NetEntity entity)
        {
            return entities.TryGetValue(id, out entity) && entity != null;
        }

        public static List<NetEntity> GetAuthorities() => entities.Values
            .Where(e => e != null && e.IsAuthority).ToList();

        /// <summary>是否还有存活的敌人副本（用于判断联机战斗是否结束）。</summary>
        public static bool HasEnemyReplica()
        {
            return entities.Values.Any(e => e != null && e.Role == EntityRole.Replica && e.Kind != EntityKind.Noel);
        }

        /// <summary>销毁某个玩家创建的所有敌人副本（该玩家掉线时调用）。</summary>
        public static void DestroyReplicasOwnedBy(int ownerPeer)
        {
            foreach (NetEntity entity in entities.Values.Where(e => e != null && e.Role == EntityRole.Replica && e.Kind != EntityKind.Noel && e.OwnerPeer == ownerPeer).ToList())
            {
                EntityFactory.DestroyEnemyReplica(entity);
            }
        }

        /// <summary>掉线强杀与普通的异图销毁分开：强杀只清该玩家的怪，绝不算整场胜利。</summary>
        public static void KillEnemiesOwnedBy(int ownerPeer)
        {
            foreach (var entity in entities.Values.Where(e => e != null && e.Kind != EntityKind.Noel && e.OwnerPeer == ownerPeer).ToList())
            {
                var enemy = entity.GetComponent<nel.NelEnemy>();
                if (enemy != null)
                {
                    enemy.hp = 0;
                    BattleSession.ObserveEnemy(enemy);
                }
                EntityFactory.DestroyEnemyReplica(entity);
            }
            EntityFactory.KillNativeReplicasOwnedBy(ownerPeer);
        }

        /// <summary>战斗结束：忘掉所有敌人实体（不销毁对象本身）。</summary>
        public static void ForgetEnemies()
        {
            foreach (int id in entities.Where(p => p.Value == null || p.Value.Kind != EntityKind.Noel).Select(p => p.Key).ToList())
            {
                entities.Remove(id);
            }
        }

        public static void Clear()
        {
            entities.Clear();
        }
    }
}
