using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyRoomUpdateEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyRoomUpdate && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            PolarisNoelsTools.UpdateRoomConfig(message.NotifyRoomUpdate);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"Room:{JsonConvert.SerializeObject(message)}";
        }
    }
}
