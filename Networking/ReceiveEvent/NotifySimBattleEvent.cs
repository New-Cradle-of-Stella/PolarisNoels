using Newtonsoft.Json;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifySimBattleEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifySimBattle && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            PolarisNoelsTools.NotifySimBattle(message.PeerId, message.SimBattle);
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"SimBattle:{JsonConvert.SerializeObject(message)}";
        }
    }
}
