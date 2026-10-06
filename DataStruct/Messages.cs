using ProtoBuf;

namespace WeNeedMoreNoels.DataStruct
{
    [ProtoContract]
    public class WNMNPeerMessage
    {
        [ProtoMember(1)]
        public int PeerId;
        [ProtoMember(2)]
        public WNMNPeerMessageType Type;
        [ProtoMember(7)]
        public UpdatePeerInfo UpdatePeerInfo;
        [ProtoMember(8)]
        public NotifyNoelTransfer NotifyNoelTransfer;
        [ProtoMember(9)]
        public NotifyShortMsg NotifyShortMsg;
        [ProtoMember(10)]
        public NotifyItemChanged NotifyItemChanged;
        [ProtoMember(11)]
        public NotifyCoinChanged NotifyCoinChanged;
        [ProtoMember(12)]
        public NotifyRoomUpdate NotifyRoomUpdate;
        [ProtoMember(14)]
        public BattleInfo Battle;
        [ProtoMember(15)]
        public SimBattle SimBattle;
        [ProtoMember(16)]
        public SimBattleSync SyncSimBattle;
        [ProtoMember(17)]
        public EntityMessage Entity;
    }

    [ProtoContract]
    public enum WNMNPeerMessageType
    {
        [ProtoEnum]
        NotifyNoelStartBattle,
        [ProtoEnum]
        NotifyNoelEndBattle,
        [ProtoEnum]
        UpdatePeerInfo,
        [ProtoEnum]
        NotifyNoelTransfer,
        [ProtoEnum]
        NotifyShortMsg,
        [ProtoEnum]
        NotifyGetItem,
        [ProtoEnum]
        NotifyLoseItem,
        [ProtoEnum]
        NotifyGetCoin,
        [ProtoEnum]
        NotifyLoseCoin,
        [ProtoEnum]
        NotifyRoomUpdate,
        [ProtoEnum]
        NotifySimBattle,
        [ProtoEnum]
        NotifySimBattleSync,
        [ProtoEnum]
        Entity
    }
}
