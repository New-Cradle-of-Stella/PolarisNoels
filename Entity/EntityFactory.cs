using m2d;
using nel;
using UnityEngine;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels
{
    /// <summary>创建实体并挂上 <see cref="NetEntity"/> 与对应模块。</summary>
    public static class EntityFactory
    {
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
        public static void AttachAuthorityEnemy(NelEnemy enemy, string key, bool isBoss)
        {
            int id = EntityIds.NewLocal();
            EntityKind kind = isBoss ? EntityKind.Boss : EntityKind.Enemy;
            IEntityModule module = isBoss ? new BossEnemyModule() : new EnemyModule();
            NetEntity entity = enemy.gameObject.AddComponent<NetEntity>()
                .Setup(id, PolarisNoelsTools.LocalID, EntityRole.Authority, kind, module);
            entity.SpawnInfo = new EntitySpawn { Kind = kind, Key = key };
            EntityNet.SendSpawn(id, entity.SpawnInfo);
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
            if (enemy != null && enemy.is_alive)
            {
                DB.MainPR.Mp.removeMover(enemy);
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
                    // Boss 由游戏自己创建，这里只需等它出现后挂上副本同步器
                    if (DB.IsInBattle && PolarisNoelsTools.BattleStarterID != PolarisNoelsTools.LocalID && !EntityRegistry.TryGet(id, out _))
                    {
                        GameObject binder = new("BossBinder");
                        BossBinder component = binder.AddComponent<BossBinder>();
                        component.EntityId = id;
                        component.OwnerPeer = ownerPeer;
                    }
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

    /// <summary>等待游戏创建 Boss，创建后给它挂上副本同步器。</summary>
    public class BossBinder : MonoBehaviour
    {
        public int EntityId;
        public int OwnerPeer;

        void Update()
        {
            NelEnemyBoss boss = FindObjectOfType<NelEnemyBoss>();
            if (boss == null)
            {
                return;
            }
            DB.CurEnemies.Add(boss);
            if (!boss.TryGetComponent<NetEntity>(out _))
            {
                boss.gameObject.AddComponent<NetEntity>()
                    .Setup(EntityId, OwnerPeer, EntityRole.Replica, EntityKind.Boss, new BossEnemyModule());
            }
            Destroy(gameObject);
        }
    }
}
