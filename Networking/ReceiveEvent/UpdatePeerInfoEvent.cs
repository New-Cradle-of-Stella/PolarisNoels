using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class UpdatePeerInfoEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.UpdatePeerInfo && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            PolarisNoelsTools.UpdatePeer(message.PeerId, message.UpdatePeerInfo);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"UpdatePeerInfo:{JsonConvert.SerializeObject(message)}";
        }
    }
}
