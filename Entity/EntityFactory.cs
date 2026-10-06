using m2d;
using nel;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels
{
    /// <summary>创建实体并挂上 <see cref="NetEntity"/> 与对应模块。</summary>
    public static class EntityFactory
    {
        // 本地 Boss/附属图可以先创建，权威 Spawn 也可以先到；两种顺序都绑定同一个副本。
        static readonly Dictionary<NelEnemy, string> nativeReplicas = new();
        static readonly Dictionary<int, EntitySpawn> pendingNativeSpawns = new();

        #region 玩家
        /// <summary>给本机玩家挂上 Authority 同步器。ID 在握手完成后自动确定。</summary>
        public static void AttachLocalPlayer(PRNoel noel)
        {
            if (noel.TryGetComponent<NetEntity>(out _))
            {
                return;
            }
            noel.gameObject.AddComponent<NetEntity>()
                .Setup(-1, -1, EntityRole.Authority, EntityKind.Noel, new NoelModule());
        }

        /// <summary>给远程玩家的影子角色挂上 Replica 同步器。</summary>
        public static void AttachPlayerReplica(ShadowNoel noel, int peerId)
        {
            if (noel.TryGetComponent<NetEntity>(out _))
            {
                return;
            }
            noel.gameObject.AddComponent<NetEntity>()
                .Setup(EntityIds.ForPlayer(peerId), peerId, EntityRole.Replica, EntityKind.Noel, new NoelModule());
        }
        #endregion

        #region 敌人
        /// <summary>给本机召唤的敌人挂上 Authority 同步器，并通知其他玩家创建副本。</summary>
        public static void AttachAuthorityEnemy(NelEnemy enemy, string key, bool isBoss, bool nativeReplica = false)
        {
            if (!BattleSession.IsAuthority || enemy.TryGetComponent<NetEntity>(out _)) return;
            int id = EntityIds.NewLocal();
            EntityKind kind = isBoss ? EntityKind.Boss : EntityKind.Enemy;
            IEntityModule module = isBoss ? new BossEnemyModule() : new EnemyModule();
            NetEntity entity = enemy.gameObject.AddComponent<NetEntity>()
                .Setup(id, PolarisNoelsTools.LocalID, EntityRole.Authority, kind, module);
            entity.SpawnInfo = new EntitySpawn { Kind = kind, Key = key, NativeReplica = nativeReplica };
            BattleSession.RegisterEnemy(id, enemy);
            EntityNet.SendSpawn(id, entity.SpawnInfo);
        }

        public static void AttachNativeEnemyReplica(NelEnemy enemy, string key, bool isBoss)
        {
            if (BattleSession.IsAuthority || enemy.TryGetComponent<NetEntity>(out _)) return;
            enemy.gameObject.AddComponent<NetEntity>().Setup(-1, PolarisNoelsTools.BattleStarterID,
                EntityRole.Replica, isBoss ? EntityKind.Boss : EntityKind.Enemy,
                isBoss ? new BossEnemyModule() : new EnemyModule());
            DB.CurEnemies.Add(enemy);
            nativeReplicas[enemy] = key;
            BindNativeReplicas();
        }

        static void BindNativeReplicas()
        {
            foreach (var pair in pendingNativeSpawns.ToList())
            {
                var enemy = nativeReplicas.Keys.FirstOrDefault(e => e != null && nativeReplicas[e] == pair.Value.Key
                    && (e is NelEnemyBoss) == (pair.Value.Kind == EntityKind.Boss));
                if (enemy == null) continue;
                enemy.GetComponent<NetEntity>().BindReplicaId(pair.Key);
                nativeReplicas.Remove(enemy);
                pendingNativeSpawns.Remove(pair.Key);
            }
        }

        public static void ForgetPendingSpawn(int id) => pendingNativeSpawns.Remove(id);

        public static void KillNativeReplicasOwnedBy(int peer)
        {
            foreach (var enemy in nativeReplicas.Keys.ToList())
            {
                if (enemy == null) { nativeReplicas.Remove(enemy); continue; }
                var entity = enemy.GetComponent<NetEntity>();
                if (entity == null || entity.OwnerPeer != peer) continue;
                enemy.hp = 0;
                DestroyEnemyReplica(entity);
                nativeReplicas.Remove(enemy);
            }
            foreach (int id in pendingNativeSpawns.Keys.Where(id => EntityIds.Owner(id) == peer).ToList())
                pendingNativeSpawns.Remove(id);
        }

        public static void ClearBattleBindings()
        {
            nativeReplicas.Clear();
            pendingNativeSpawns.Clear();
        }

        static void CreateEnemyReplica(int id, int ownerPeer, EntitySpawn spawn)
        {
            Map2d map = DB.MainPR.Mp;
            NelEnemy enemy = NDAT.createByKey(map, spawn.Key, "-Summonned-" + map.key + "-{sync}" + id);
            map.assignMover(enemy);
            DB.CurEnemies.Add(enemy);
            enemy.gameObject.AddComponent<NetEntity>()
                .Setup(id, ownerPeer, EntityRole.Replica, EntityKind.Enemy, new EnemyModule());
        }

        /// <summary>销毁敌人副本（所有者那边的敌人消失或掉线）。</summary>
        public static void DestroyEnemyReplica(NetEntity entity)
        {
            NelEnemy enemy = entity.GetComponent<NelEnemy>();
            if (enemy != null)
            {
                enemy.Mp?.removeMover(enemy);
                enemy.destruct();
                Object.DestroyImmediate(enemy);
            }
            Object.Destroy(entity);
        }
        #endregion

        /// <summary>收到 Spawn 消息。</summary>
        public static void OnSpawn(int id, int ownerPeer, EntitySpawn spawn)
        {
            if (spawn == null || ownerPeer == PolarisNoelsTools.LocalID)
            {
                return;
            }
            if (spawn.Kind != EntityKind.Noel)
            {
                if (!DB.IsInBattle || ownerPeer != PolarisNoelsTools.BattleStarterID || EntityIds.Owner(id) != ownerPeer) return;
                if (spawn.NativeReplica)
                {
                    if (!EntityRegistry.TryGet(id, out _)) pendingNativeSpawns[id] = spawn;
                    BindNativeReplicas();
                    return;
                }
            }
            switch (spawn.Kind)
            {
                case EntityKind.Noel:
                    OnPlayerSpawn(ownerPeer, spawn.Noel);
                    break;
                case EntityKind.Enemy:
                    if (DB.IsInBattle && !EntityRegistry.TryGet(id, out _))
                    {
                        CreateEnemyReplica(id, ownerPeer, spawn);
                    }
                    break;
                case EntityKind.Boss:
                    // 新协议的 Boss 必须保留原版本地对象图，并通过 NativeReplica 绑定。
                    break;
            }
        }

        static void OnPlayerSpawn(int ownerPeer, IniConfig config)
        {
            if (config == null || DB.noelIns.ContainsKey(ownerPeer))
            {
                return;
            }
            DB.partyInfos[ownerPeer] = config.PartyConfig;
            ShadowNoelExtensions.GenerateShadowNoel(config.ClientConfig, ownerPeer);
        }
    }

}
