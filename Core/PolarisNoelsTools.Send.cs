using UnityEngine;
using m2d;
using nel;
using PixelLiner.PixelLinerLib;
using ProtoBuf;
using System.IO;
using System.Linq;
using PolarisNoels.CSNetworking;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using PolarisNoels.SN;
using XX;

namespace PolarisNoels
    {
    public static partial class PolarisNoelsTools
    {
        /// <summary>序列化并发给所有已连接的玩家。通道语义与原 ReliableOrdered/Sequenced 一一对应。</summary>
        public static void Broadcast(PolarisNoelsPeerMessage message, NetChannel channel = NetChannel.Reliable, bool mapOnly = false)
        {
            if (mapOnly)
            {
                message.MapKey = DB.MainPR?.Mp?.key;
                if (!peer.HasMapRecipients(message.MapKey)) return;
            }
            using MemoryStream stream = new();
            Serializer.Serialize(stream, message);
            if (mapOnly) peer.SendToMap(stream.ToArray(), channel, message.MapKey);
            else peer.SendToAll(stream.ToArray(), channel);
        }

        public static void SendBattleStartToAllPeers(string key, int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyNoelStartBattle,
                PeerId = id,
                Battle = new()
                {
                    key = key,
                    isSim = false
                }
            };
            Broadcast(messageSend, mapOnly: true);
        }

        public static void SendSimBattleStartToAllPeers(int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyNoelStartBattle,
                PeerId = id,
                Battle = new()
                {
                    isSim = true,
                    SpawnPoints = SpawnDic.Select(x => (x.Key, (DataStruct.Vector2Int)x.Value)).ToDictionary(x => x.Key, x => x.Item2)
                }
            };
            Broadcast(messageSend);
        }

        public static void SendBattleEndToAllPeers(string key, int id, bool defeated = true)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyNoelEndBattle,
                PeerId = id,
                Battle = new()
                {
                    key = key,
                    Aborted = !defeated
                }
            };
            Broadcast(messageSend, mapOnly: true);
        }

        public static void SendUpdatePeerInfoToAllPeers(int id, string nickname)
        {
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.UpdatePeerInfo,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.UpdatePeerInfo,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyNoelTransfer,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyShortMsg,
                PeerId = id,
                NotifyShortMsg = new()
                {
                    ID = id,
                    key = key
                }
            };
            Broadcast(messageSend, mapOnly: true);
        }

        public static void SendGetItemToAllPeers(string key, int count, int grade)
        {
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyGetItem,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyLoseItem,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyGetCoin,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyLoseCoin,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifySimBattle,
                PeerId = LocalID,
                SimBattle = battle
            };
            Broadcast(messageSend);
        }

        public static void SendSimBattleSync(int id)
        {
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifySimBattleSync,
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifySimBattleSync,
                PeerId = id,
                SyncSimBattle = new()
                {
                    SyncID = -1,
                    SyncSimBattleData = array.bytes
                }
            };
            // 模拟战斗地图可能是几十 KB 的大包：走 BULK 通道（可靠流单帧上限 1MB），
            // 用 BulkCodec 包一层，接收侧还原成同一条 protobuf 消息。
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            peer.SendToAll(BulkCodec.Wrap(BulkCodec.KindGameMessage, stream.ToArray()), NetChannel.Bulk);
            Plugin.Logger.LogInfo($"Transfered sync smncFile to peer:{id} via bulk ({array.bytes.Length} bytes)");
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
            PolarisNoelsPeerMessage messageSend = new()
            {
                Type = PolarisNoelsPeerMessageType.NotifyRoomUpdate,
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
