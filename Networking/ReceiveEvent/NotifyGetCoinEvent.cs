using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyGetCoinEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyGetCoin && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            NotifyCoinChanged coin = message.NotifyCoinChanged;
            PolarisNoelsTools.GetCoin(coin.PartyID, coin.coinType, coin.count);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"Item:{JsonConvert.SerializeObject(message)}";
        }
    }
}
