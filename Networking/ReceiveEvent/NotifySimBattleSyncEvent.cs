using nel.mgm.smncr;
using Newtonsoft.Json;
using PixelLiner.PixelLinerLib;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking.ReceiveEvent
{
    public class NotifySimBattleSyncEvent : PeerReceiveMessageBase
    {
        public override bool CheckMessage(PolarisNoelsPeerMessage message)
        {
            return message.Type == PolarisNoelsPeerMessageType.NotifySimBattleSync && message.PeerId == PolarisNoelsTools.LocalID;
        }

        public override void ReceiveMessage(PolarisNoelsPeerMessage message)
        {
            if (message.SyncSimBattle.SyncID == -1)
            {
                ByteArray array = new(message.SyncSimBattle.SyncSimBattleData);
                SmncFile.readFromFile(array, PolarisNoelsTools.USC.M2D, (file, _) =>
                {
                    PolarisNoelsTools.CurSimFile = file;
                    PolarisNoelsTools.USC.AFiles.Add(file);
                    PolarisNoelsTools.USC.initFileSelection(file, true);
                    PolarisNoelsTools.SimBattleSynced = true;
                    PolarisNoelsTools.UpdateSimUI?.Invoke();
                });
            }
            else
            {
                PolarisNoelsTools.SendBackSimBattleSyncData(message.SyncSimBattle.SyncID);
            }
        }

        public override string ToMessageString(PolarisNoelsPeerMessage message)
        {
            return $"SimBattleSync:{JsonConvert.SerializeObject(message)}";
        }
    }
}
