using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyNoelTransferEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyNoelTransfer && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            PolarisNoelsTools.TransferMainNoel(message.NotifyNoelTransfer);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"NotifyTransferInfo:{JsonConvert.SerializeObject(message)}";
        }
    }
}
