using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using nel;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using PolarisNoels.SN;

namespace PolarisNoels.CSNetworking
{
    /// <summary>
    /// 入房者侧业务握手。标题界面就建立连接并收存档；进入游戏后沿用同一条连接，不重连。
    /// 只有 JoinResult + HostMessage + Bulk 存档三者都到齐才算 Ready（标题界面据此进入游戏）。
    /// </summary>
    public sealed class ClientSession
    {
        readonly INetTransport transport;

        public ClientSession(INetTransport transport)
        {
            this.transport = transport;
            transport.JoinResult += OnJoinResult;
            transport.Message += OnMessage;
            transport.PeerDisconnected += OnPeerDisconnected;
            transport.NatInfo += OnNatInfo;
            transport.BulkProgress += OnBulkProgress;
        }

        public int LocalId { get; private set; } = -1;

        public bool JoinAccepted { get; private set; }

        public bool HostHandshakeDone { get; private set; }

        public bool SaveReceived { get; private set; }

        public bool Ready => JoinAccepted && HostHandshakeDone && SaveReceived;

        public int LastJoinError { get; private set; } = -1;

        public string NatJson { get; private set; }

        public uint SaveDone { get; private set; }

        public uint SaveTotal { get; private set; }

        public long ReceivedArchiveBytes { get; private set; }

        /// <summary>主机下发的其它玩家外观，等进入游戏世界后再生成影子（此时才有 Mover）。</summary>
        public readonly List<KeyValuePair<int, ClientConfig>> PendingConfigs = [];

        public event Action Updated;

        void OnJoinResult(int code)
        {
            LastJoinError = code;
            if (code == 0)
            {
                JoinAccepted = true;
            }
            else
            {
                Plugin.Logger.LogWarning($"join failed: {(NetJoinError)code}");
            }
            Updated?.Invoke();
        }

        void OnNatInfo(string json)
        {
            NatJson = json;
            // The JSON can contain a room descriptor; never put it in logs.
            Updated?.Invoke();
        }

        void OnBulkProgress(int peer, uint bulkId, uint done, uint total)
        {
            if (peer != 0)
            {
                return;
            }
            SaveDone = done;
            SaveTotal = total;
            Updated?.Invoke();
        }

        void OnMessage(int peer, NetChannel channel, byte[] data)
        {
            if (peer != 0 || channel != NetChannel.Reliable)
            {
                return;
            }
            if (!HandshakeCodec.TryDecode(data, HandshakeCodec.KindHostMessage, out PolarisNoelsHostMessage message))
            {
                return;
            }
            if (message.MutePlayer)
            {
                PolarisNoelsTools.ToggleMute();
                return;
            }
            ApplyHandshake(message);
        }

        void ApplyHandshake(PolarisNoelsHostMessage message)
        {
            LocalId = message.InitID;
            PolarisNoelsTools.LocalID = message.InitID;
            PolarisNoelsTools.SimBattleSyncHost = message.SyncHost;
            PolarisNoelsTools.SimBattleSyncList.Clear();
            if (message.SyncConnectedList != null)
            {
                PolarisNoelsTools.SimBattleSyncList.AddRange(message.SyncConnectedList);
            }
            DB.LocalNoelParty = message.InitID;
            if (message.PeerParties != null)
            {
                DB.partyInfos = message.PeerParties.Select(x => x.Value).ToDictionary(x => x.ID);
            }
            DB.partyInfos[message.InitID] = PartyManager.InitNewParty(message.InitID);
            PendingConfigs.Clear();
            if (message.PeerConfigs != null)
            {
                foreach (var pair in message.PeerConfigs)
                {
                    DB.peerConfigs[pair.Key] = pair.Value;
                    PendingConfigs.Add(pair);
                }
            }
            HostHandshakeDone = true;
            SendClientMessage();
            Plugin.Logger.LogInfo($"client: host handshake applied, id={LocalId}");
            Updated?.Invoke();
        }

        void SendClientMessage()
        {
            PolarisNoelsClientMessage message = new()
            {
                ID = LocalId,
                NickName = DB.InitConfig?.nickName ?? "",
                NoelType = DB.InitConfig?.NoelType ?? NoelType.Normal,
                NoelColor = DB.InitConfig?.NoelColor ?? ColorNoelColor.Red,
                Party = DB.partyInfos.TryGetValue(LocalId, out var party) ? party : PartyManager.InitNewParty(LocalId)
            };
            transport.Send(0, NetChannel.Reliable, HandshakeCodec.Encode(HandshakeCodec.KindClientMessage, message));
            Plugin.Logger.LogInfo("client: handshake reply sent");
        }

        /// <summary>BULK 存档到齐后由 NetworkRuntime 调用（载荷已去掉 BulkCodec 前缀）。</summary>
        public void OnSaveArchive(byte[] packed)
        {
            try
            {
            byte[] raw = SaveTransfer.Unpack(packed);
            DB.SyncSaveContentBuffer = raw;
            ReceivedArchiveBytes = raw.Length;
            string path = Path.Combine(SVD.getDir(), DB.SYNC_FILE_NAME);
            File.WriteAllBytes(path, raw);
            SaveReceived = true;
            Plugin.Logger.LogInfo($"client: sync save written to {path} ({raw.Length} bytes)");
            Updated?.Invoke();
            }
            catch (Exception error)
            {
                LastJoinError = (int)NetJoinError.HandshakeFailed;
                Plugin.Logger.LogError($"invalid save archive: {error.Message}");
                transport.Disconnect(0, DisconnectReason.Protocol);
                Updated?.Invoke();
            }
        }

        /// <summary>进入游戏世界后再生成影子与本地昵称：标题界面没有 Mover，不能提前做。</summary>
        public void OnEnterGameWorld()
        {
            if (LocalId < 0)
            {
                return;
            }
            PolarisNoelsTools.GenerateAllNoels(PendingConfigs);
            PolarisNoelsTools.SetAllNickNameBgs();
            LocalNickname.Apply();
        }

        void OnPeerDisconnected(int peer, DisconnectReason reason)
        {
            if (peer != 0)
            {
                // 其他成员由 PolarisNoelsPeer 的 peer 事件清理。
                return;
            }
            DB.PolarisNoelsHostKicked = reason == DisconnectReason.Kicked;
            DB.PolarisNoelsHostClosed = true;
            Plugin.Logger.LogWarning($"host connection lost: {reason}");
            if (DB.MainPR != null)
            {
                ((NelM2DBase)DB.MainPR.M2D).quitGame("SceneTitle");
            }
            Updated?.Invoke();
        }

        public string SaveProgressText()
        {
            if (SaveTotal > 0)
            {
                return $"{SaveDone * 100 / SaveTotal}% ({SaveDone}/{SaveTotal})";
            }
            return SaveDone > 0 ? $"{SaveDone} bytes" : "";
        }

        public void Shutdown()
        {
            transport.JoinResult -= OnJoinResult;
            transport.Message -= OnMessage;
            transport.PeerDisconnected -= OnPeerDisconnected;
            transport.NatInfo -= OnNatInfo;
            transport.BulkProgress -= OnBulkProgress;
        }
    }
}
