using LiteNetLib;
using UnityEngine;
using m2d;
using nel;
using PixelLiner.PixelLinerLib;
using ProtoBuf;
using System.IO;
using System.Linq;
using WeNeedMoreNoels.CSNetworking;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.SN;
using XX;

namespace WeNeedMoreNoels
    {
    public static partial class WNMNTools
    {
        /// <summary>序列化并发给所有已连接的玩家。</summary>
        public static void Broadcast(WNMNPeerMessage message, DeliveryMethod delivery = DeliveryMethod.ReliableOrdered)
        {
            using MemoryStream stream = new();
            Serializer.Serialize(stream, message);
            peer.SendToAll(stream.ToArray(), delivery);
        }

        public static void SendBattleStartToAllPeers(string key, int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelStartBattle,
                PeerId = id,
                Battle = new()
                {
                    key = key,
                    isSim = false
                }
            };
            Broadcast(messageSend);
        }

        public static void SendSimBattleStartToAllPeers(int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelStartBattle,
                PeerId = id,
                Battle = new()
                {
                    isSim = true,
                    SpawnPoints = SpawnDic.Select(x => (x.Key, (DataStruct.Vector2Int)x.Value)).ToDictionary(x => x.Key, x => x.Item2)
                }
            };
            Broadcast(messageSend);
        }

        public static void SendBattleEndToAllPeers(string key, int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelEndBattle,
                PeerId = id,
                Battle = new()
                {
                    key = key
                }
            };
            Broadcast(messageSend);
        }

        public static void SendUpdatePeerInfoToAllPeers(int id, string nickname)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.UpdatePeerInfo,
                PeerId = id,
                UpdatePeerInfo = new()
                {
                    Type = UpdatePeerType.Nickname,
                    NickName = nickname
                }
            };
            Broadcast(messageSend);
        }

        public static void SendUpdatePeerInfoToAllPeers(int id, int party)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.UpdatePeerInfo,
                PeerId = id,
                UpdatePeerInfo = new()
                {
                    Type = UpdatePeerType.Party,
                    PartyID = party
                }
            };
            Broadcast(messageSend);
        }

        public static void SendNotifyNoelTransferToAllPeers(int id)
        {
            DB.MainPR.getPosition(out float x, out float y);
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelTransfer,
                PeerId = id,
                NotifyNoelTransfer = new()
                {
                    Key = DB.MainPR.Mp.key,
                    X = x,
                    Y = y
                }
            };
            Broadcast(messageSend);
        }

        public static void SendNotifyShortMsgToAllPeers(int id, string key)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyShortMsg,
                PeerId = id,
                NotifyShortMsg = new()
                {
                    ID = id,
                    key = key
                }
            };
            Broadcast(messageSend);
        }

        public static void SendGetItemToAllPeers(string key, int count, int grade)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyGetItem,
                PeerId = LocalID,
                NotifyItemChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    key = key,
                    count = count,
                    grade = grade
                }
            };
            Broadcast(messageSend);
        }

        public static void SendLoseItemToAllPeers(string key, int count, int grade)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyLoseItem,
                PeerId = LocalID,
                NotifyItemChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    key = key,
                    count = count,
                    grade = grade
                }
            };
            Broadcast(messageSend);
        }

        public static void SendGetCoinToAllPeers(CoinStorage.CTYPE type, int count)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyGetCoin,
                PeerId = LocalID,
                NotifyCoinChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    coinType = type,
                    count = count,
                }
            };
            Broadcast(messageSend);
        }

        public static void SendLoseCoinToAllPeers(CoinStorage.CTYPE type, int count)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyLoseCoin,
                PeerId = LocalID,
                NotifyCoinChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    coinType = type,
                    count = count,
                }
            };
            Broadcast(messageSend);
        }

        public static void SendNotifySimBattleToAllPeers(SimBattle battle)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifySimBattle,
                PeerId = LocalID,
                SimBattle = battle
            };
            Broadcast(messageSend);
        }

        public static void SendSimBattleSync(int id)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifySimBattleSync,
                PeerId = id,
                SyncSimBattle = new()
                {
                    SyncID = LocalID
                }
            };
            Broadcast(messageSend);
        }

        public static void SendBackSimBattleSyncData(int id)
        {
            ByteArray array = new(0U);
            array.writeMultiByte("tigrina chan no hutomomo tyokkei 440m by hashinomizuha", "utf-8");
            array.writeByte(13);
            USC.CurFile.writeBinaryTo(array);
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifySimBattleSync,
                PeerId = id,
                SyncSimBattle = new()
                {
                    SyncID = -1,
                    SyncSimBattleData = array.bytes
                }
            };
            Broadcast(messageSend);
            Plugin.Logger.LogInfo($"Transfered sync smncFile to peer:{id}");
        }

        static bool? lastSentPvp;
        static EnemySyncType? lastSentSyncType;
        static float nextRoomHeartbeat;

        /// <summary>
        /// 主机广播房间配置。配置变化时立即发送，否则每 2 秒心跳一次。
        /// 之前每帧都可靠发送，高延迟下会把可靠通道的发送窗口塞满。
        /// </summary>
        public static void UpdateRoomConfigToAllPeers()
        {
            bool changed = lastSentPvp != EnablePVP || lastSentSyncType != SyncType;
            if (!changed && Time.time < nextRoomHeartbeat)
            {
                return;
            }
            lastSentPvp = EnablePVP;
            lastSentSyncType = SyncType;
            nextRoomHeartbeat = Time.time + 2f;
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyRoomUpdate,
                PeerId = LocalID,
                NotifyRoomUpdate = new()
                {
                    EnablePVP = EnablePVP,
                    SyncType = SyncType
                }
            };
            Broadcast(messageSend);
        }

        public static void UpdateRoomConfig(NotifyRoomUpdate update)
        {
            EnablePVP = update.EnablePVP;
            SyncType = update.SyncType;
        }

        public static void SendMsg(int id, string txtID)
        {
            if (id == LocalID)
            {
                DB.MainPRMsg.ShowMsg(txtID);
            }
            else
            {
                if (DB.noelIns.TryGetValue(id, out var ins))
                {
                    ins.Noel.MsgIns?.ShowMsg(txtID);
                }
            }
        }
    }
}
