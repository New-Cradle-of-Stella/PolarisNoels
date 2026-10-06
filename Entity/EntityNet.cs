using ProtoBuf;
using System.IO;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;

namespace PolarisNoels
{
    /// <summary>实体同步的收发入口。所有实体消息都经过这里。</summary>
    public static class EntityNet
    {
        static bool CanSend => DB.IsMultiplayer && PolarisNoelsTools.peer != null && PolarisNoelsTools.LocalID >= 0;

        static void Send(EntityMessage message, NetChannel channel)
        {
            PolarisNoelsPeerMessage envelope = new()
            {
                Type = PolarisNoelsPeerMessageType.Entity,
                PeerId = PolarisNoelsTools.LocalID,
                Entity = message
            };
            bool playerAnnouncement = message.Type == EntityMsgType.Spawn && message.Spawn?.Kind == EntityKind.Noel;
            PolarisNoelsTools.Broadcast(envelope, channel, mapOnly: !playerAnnouncement);
        }

        #region 发送
        public static void SendSpawn(int id, EntitySpawn spawn)
        {
            if (!CanSend)
            {
                return;
            }
            Send(new EntityMessage { Type = EntityMsgType.Spawn, EntityId = id, Spawn = spawn }, NetChannel.Reliable);
        }

        public static void SendState(NetEntity entity)
        {
            if (!CanSend || DB.MainPR == null || DB.MainPR.Mp == null)
            {
                return;
            }
            // 没有同图接收者时连快照都不构建，避免魔法/动画采样和序列化开销。
            if (entity.MapKey != DB.MainPR.Mp.key || !PolarisNoelsTools.peer.HasMapRecipients(entity.MapKey)) return;
            EntityState state = new();
            entity.WriteState(state);
            if (state.Noel == null && state.Enemy == null)
            {
                return;
            }
            // 状态是可以丢的：只要最新的一包，旧包到得晚就直接丢弃
            Send(new EntityMessage { Type = EntityMsgType.State, EntityId = entity.Id, State = state }, NetChannel.Sequenced);
        }

        public static void SendEvent(int entityId, EntityEvent ev)
        {
            if (!CanSend)
            {
                return;
            }
            Send(new EntityMessage { Type = EntityMsgType.Event, EntityId = entityId, Event = ev }, NetChannel.Reliable);
        }

        public static void SendDespawn(int id)
        {
            if (!CanSend)
            {
                return;
            }
            Send(new EntityMessage { Type = EntityMsgType.Despawn, EntityId = id }, NetChannel.Reliable);
        }

        /// <summary>向所有人宣告本机玩家的存在（昵称、外观、队伍）。</summary>
        public static void AnnounceLocalPlayer()
        {
            if (!CanSend || !DB.partyInfos.TryGetValue(PolarisNoelsTools.LocalID, out var party))
            {
                return;
            }
            SendSpawn(EntityIds.ForPlayer(PolarisNoelsTools.LocalID), new EntitySpawn
            {
                Kind = EntityKind.Noel,
                Noel = new IniConfig
                {
                    Id = PolarisNoelsTools.LocalID,
                    ClientConfig = DB.InitConfig,
                    PartyConfig = new PartyConfig
                    {
                        ID = PolarisNoelsTools.LocalID,
                        A = party.Color.a,
                        R = party.Color.r,
                        G = party.Color.g,
                        B = party.Color.b,
                        Name = party.Name
                    }
                }
            });
        }

        /// <summary>恢复同图时补发当前实体，避免异图期间错过敌人的 Spawn 后只收到状态。</summary>
        public static void SendCurrentMapToPeer(int peerId)
        {
            if (!CanSend || DB.MainPR?.Mp == null || !PolarisNoelsTools.peer.IsPeerOnCurrentMap(peerId)) return;
            // 对方可能在异图期间错过战斗开始；先恢复战斗，再按序补发敌人。
            if (DB.IsInBattle && DB.CurSummoner?.Mp?.key == DB.MainPR.Mp.key
                && PolarisNoelsTools.BattleStarterID == PolarisNoelsTools.LocalID && !PolarisNoelsTools.SimBattleReady)
            {
                PolarisNoelsTools.peer.SendToPeer(peerId, new PolarisNoelsPeerMessage
                {
                    Type = PolarisNoelsPeerMessageType.NotifyNoelStartBattle,
                    PeerId = PolarisNoelsTools.LocalID,
                    MapKey = DB.MainPR.Mp.key,
                    Battle = new() { key = DB.CurSummoner.key, isSim = false }
                }, NetChannel.Reliable);
            }
            foreach (NetEntity entity in EntityRegistry.GetAuthorities())
            {
                if (entity.MapKey != DB.MainPR.Mp.key) continue;
                if (entity.SpawnInfo != null)
                {
                    SendInitial(new EntityMessage { Type = EntityMsgType.Spawn, EntityId = entity.Id, Spawn = entity.SpawnInfo });
                }
                EntityState state = new();
                entity.WriteState(state);
                if (state.Noel != null || state.Enemy != null)
                {
                    SendInitial(new EntityMessage { Type = EntityMsgType.State, EntityId = entity.Id, State = state });
                }
            }

            void SendInitial(EntityMessage message)
            {
                // 初始 Spawn 和快照走同一可靠通道，保证先创建副本再应用状态。
                PolarisNoelsTools.peer.SendToPeer(peerId, new PolarisNoelsPeerMessage
                {
                    Type = PolarisNoelsPeerMessageType.Entity,
                    PeerId = PolarisNoelsTools.LocalID,
                    MapKey = DB.MainPR.Mp.key,
                    Entity = message
                }, NetChannel.Reliable);
            }
        }
        #endregion

        #region 接收
        public static void Receive(PolarisNoelsPeerMessage message)
        {
            EntityMessage m = message.Entity;
            if (m == null)
            {
                return;
            }
            // 状态/生成/销毁只能由实体所有者发出；伤害请求允许其他玩家发送。
            bool damageRequest = m.Type == EntityMsgType.Event && m.Event?.Type == EntityEventType.Damage;
            if (!damageRequest && EntityIds.Owner(m.EntityId) != message.PeerId) return;
            switch (m.Type)
            {
                case EntityMsgType.Spawn:
                    EntityFactory.OnSpawn(m.EntityId, message.PeerId, m.Spawn);
                    break;
                case EntityMsgType.State:
                    OnState(m);
                    break;
                case EntityMsgType.Event:
                    if (EntityRegistry.TryGet(m.EntityId, out NetEntity target) && m.Event != null)
                    {
                        target.HandleEvent(m.Event);
                    }
                    break;
                case EntityMsgType.Despawn:
                    EntityFactory.ForgetPendingSpawn(m.EntityId);
                    if (EntityRegistry.TryGet(m.EntityId, out NetEntity gone) && gone.Role == EntityRole.Replica && gone.Kind != EntityKind.Noel)
                    {
                        EntityFactory.DestroyEnemyReplica(gone);
                    }
                    break;
            }
        }

        static void OnState(EntityMessage m)
        {
            if (EntityRegistry.TryGet(m.EntityId, out NetEntity entity))
            {
                if (entity.Role == EntityRole.Replica && m.State != null)
                {
                    entity.ReadState(m.State);
                }
                return;
            }
            // 玩家不在本地图时没有对应 Mover，但仍需记录最新状态（含所在地图），以便对方进入同图时立刻出现
            if (EntityIds.IsPlayer(m.EntityId) && m.State?.Noel != null)
            {
                PolarisNoelsTools.UpdateNoel(EntityIds.Owner(m.EntityId), m.State.Noel);
            }
        }
        #endregion
    }
}
