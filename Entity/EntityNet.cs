using LiteNetLib;
using ProtoBuf;
using System.IO;
using WeNeedMoreNoels.DataStruct;

namespace WeNeedMoreNoels
{
    /// <summary>实体同步的收发入口。所有实体消息都经过这里。</summary>
    public static class EntityNet
    {
        static bool CanSend => DB.IsMultiplayer && WNMNTools.peer != null && WNMNTools.LocalID >= 0;

        static void Send(EntityMessage message, DeliveryMethod delivery)
        {
            WNMNPeerMessage envelope = new()
            {
                Type = WNMNPeerMessageType.Entity,
                PeerId = WNMNTools.LocalID,
                Entity = message
            };
            WNMNTools.Broadcast(envelope, delivery);
        }

        #region 发送
        public static void SendSpawn(int id, EntitySpawn spawn)
        {
            if (!CanSend)
            {
                return;
            }
            Send(new EntityMessage { Type = EntityMsgType.Spawn, EntityId = id, Spawn = spawn }, DeliveryMethod.ReliableOrdered);
        }

        public static void SendState(NetEntity entity)
        {
            if (!CanSend || DB.MainPR == null || DB.MainPR.Mp == null)
            {
                return;
            }
            EntityState state = new();
            entity.WriteState(state);
            if (state.Noel == null && state.Enemy == null)
            {
                return;
            }
            // 状态是可以丢的：只要最新的一包，旧包到得晚就直接丢弃
            Send(new EntityMessage { Type = EntityMsgType.State, EntityId = entity.Id, State = state }, DeliveryMethod.Sequenced);
        }

        public static void SendEvent(int entityId, EntityEvent ev)
        {
            if (!CanSend)
            {
                return;
            }
            Send(new EntityMessage { Type = EntityMsgType.Event, EntityId = entityId, Event = ev }, DeliveryMethod.ReliableOrdered);
        }

        public static void SendDespawn(int id)
        {
            if (!CanSend)
            {
                return;
            }
            Send(new EntityMessage { Type = EntityMsgType.Despawn, EntityId = id }, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>向所有人宣告本机玩家的存在（昵称、外观、队伍）。</summary>
        public static void AnnounceLocalPlayer()
        {
            if (!CanSend || !DB.partyInfos.TryGetValue(WNMNTools.LocalID, out var party))
            {
                return;
            }
            SendSpawn(EntityIds.ForPlayer(WNMNTools.LocalID), new EntitySpawn
            {
                Kind = EntityKind.Noel,
                Noel = new IniConfig
                {
                    Id = WNMNTools.LocalID,
                    ClientConfig = DB.InitConfig,
                    PartyConfig = new PartyConfig
                    {
                        ID = WNMNTools.LocalID,
                        A = party.Color.a,
                        R = party.Color.r,
                        G = party.Color.g,
                        B = party.Color.b,
                        Name = party.Name
                    }
                }
            });
        }
        #endregion

        #region 接收
        public static void Receive(WNMNPeerMessage message)
        {
            EntityMessage m = message.Entity;
            if (m == null)
            {
                return;
            }
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
                WNMNTools.UpdateNoel(EntityIds.Owner(m.EntityId), m.State.Noel);
            }
        }
        #endregion
    }
}
