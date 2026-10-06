using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyShortMsg : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyShortMsg && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            PolarisNoelsTools.SendMsg(message.NotifyShortMsg.ID, message.NotifyShortMsg.key);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"ShortMsg:{JsonConvert.SerializeObject(message)}";
        }
    }
}
