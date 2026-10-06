using Newtonsoft.Json;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyNoelEndBattleEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyNoelEndBattle && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            ShadowNoelExtensions.EndCurMapBattle();
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"Notify end battle, info:{JsonConvert.SerializeObject(message)}";
        }
    }
}
