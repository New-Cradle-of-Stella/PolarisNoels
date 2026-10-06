using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    /// <summary>所有实体同步消息（Spawn / State / Event / Despawn）的统一入口。</summary>
    public class EntityMessageEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.Entity && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            EntityNet.Receive(message);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"Entity:{JsonConvert.SerializeObject(message)}";
        }
    }
}
