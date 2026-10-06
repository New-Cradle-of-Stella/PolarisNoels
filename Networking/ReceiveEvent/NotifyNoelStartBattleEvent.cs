using Newtonsoft.Json;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifyNoelStartBattleEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifyNoelStartBattle && message.PeerId != PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            if (message.Battle == null || DB.IsInBattle) return;
            if (message.Battle.isSim)
            {
                int x, y;
                if (message.Battle.SpawnPoints.ContainsKey(PolarisNoelsTools.LocalID))
                {
                    x = message.Battle.SpawnPoints[PolarisNoelsTools.LocalID].x;
                    y = message.Battle.SpawnPoints[PolarisNoelsTools.LocalID].y;
                }
                else
                {
                    x = message.Battle.SpawnPoints[-1].x;
                    y = message.Battle.SpawnPoints[-1].y;
                }
                ShadowNoelExtensions.StartSimBattle(message.PeerId, x, y);
            }
            else
            {
                if (DB.IsInBattle && PolarisNoelsTools.BattleStarterID == message.PeerId
                    && DB.CurSummoner?.key == message.Battle.key) return;
                ShadowNoelExtensions.StartCurMapBattle(message.Battle.key, message.PeerId);
            }
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"Notify start battle, info:{JsonConvert.SerializeObject(message)}";
        }
    }
}
