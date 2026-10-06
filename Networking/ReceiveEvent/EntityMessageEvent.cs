using Newtonsoft.Json;
using WeNeedMoreNoels.DataStruct;

namespace WeNeedMoreNoels.Networking.ReceiveEvent
{
    /// <summary>所有实体同步消息（Spawn / State / Event / Despawn）的统一入口。</summary>
    public class EntityMessageEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(WNMNPeerMessage message)
        {
            return message.Type == WNMNPeerMessageType.Entity && message.PeerId != WNMNTools.LocalID;
        }

        public override void ReceiveMessage(WNMNPeerMessage message)
        {
            EntityNet.Receive(message);
        }

        public override string ToMessageString(WNMNPeerMessage message)
        {
            return $"Entity:{JsonConvert.SerializeObject(message)}";
        }
    }
}
