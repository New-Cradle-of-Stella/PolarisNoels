using ProtoBuf;

namespace WeNeedMoreNoels.DataStruct
{
    /// <summary>统一实体同步协议的信封。玩家、敌人等一切被同步的 Mover 都走这一条消息。</summary>
    [ProtoContract]
    public class EntityMessage
    {
        [ProtoMember(1)]
        public EntityMsgType Type;
        [ProtoMember(2)]
        public int EntityId;
        [ProtoMember(3)]
        public EntitySpawn Spawn;
        [ProtoMember(4)]
        public EntityState State;
        [ProtoMember(5)]
        public EntityEvent Event;
    }

    [ProtoContract]
    public enum EntityMsgType
    {
        /// <summary>实体出现（可靠）</summary>
        [ProtoEnum]
        Spawn,
        /// <summary>周期性状态快照（不可靠、按序，旧包直接丢弃）</summary>
        [ProtoEnum]
        State,
        /// <summary>一次性事件：受击、魔法等（可靠）</summary>
        [ProtoEnum]
        Event,
        /// <summary>实体消失（可靠）</summary>
        [ProtoEnum]
        Despawn
    }

    [ProtoContract]
    public enum EntityKind
    {
        [ProtoEnum]
        Noel,
        [ProtoEnum]
        Enemy,
        [ProtoEnum]
        Boss
    }

    [ProtoContract]
    public class EntitySpawn
    {
        [ProtoMember(1)]
        public EntityKind Kind;
        /// <summary>敌人的种类 key（Kind 为 Enemy / Boss 时使用）</summary>
        [ProtoMember(2)]
        public string Key;
        /// <summary>玩家的昵称、外观、队伍（Kind 为 Noel 时使用）</summary>
        [ProtoMember(3)]
        public IniConfig Noel;
    }

    /// <summary>状态快照。每个模块只填写自己负责的那一段，其余为 null。</summary>
    [ProtoContract]
    public class EntityState
    {
        [ProtoMember(1)]
        public UpdateNoelInfo Noel;
        [ProtoMember(2)]
        public UpdateEnemyInfo Enemy;
    }

    [ProtoContract]
    public class EntityEvent
    {
        [ProtoMember(1)]
        public EntityEventType Type;
        [ProtoMember(2)]
        public NotifyNoelDamage Damage;
        [ProtoMember(3)]
        public NotifyNoelMagic Magic;
    }

    [ProtoContract]
    public enum EntityEventType
    {
        /// <summary>副本被打 -> 通知所有者结算伤害</summary>
        [ProtoEnum]
        Damage,
        /// <summary>所有者的魔法变化 -> 通知副本重现</summary>
        [ProtoEnum]
        Magic
    }
}
