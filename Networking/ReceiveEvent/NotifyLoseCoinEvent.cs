using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyLoseCoinEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyLoseCoin && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            NotifyCoinChanged coin = message.NotifyCoinChanged;
            PolarisNoelsTools.LoseCoin(coin.PartyID, coin.coinType, coin.count);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"Item:{JsonConvert.SerializeObject(message)}";
        }
    }
}
