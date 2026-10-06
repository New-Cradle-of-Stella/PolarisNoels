using System.Collections.Generic;
using PolarisNoels.Networking;
using PolarisNoels.SN;

namespace PolarisNoels.CSNetworking
{
    /// <summary>主机侧业务握手：每个 peer 连上后发 HostMessage，再把存档走 BULK 发过去。</summary>
    public sealed class HostSession
    {
        readonly INetTransport transport;
        readonly PolarisNoelsTools.NetworkConfig config;

        public HostSession(INetTransport transport, PolarisNoelsTools.NetworkConfig config = null)
        {
            this.transport = transport;
            this.config = config;
            transport.PeerConnected += OnPeerConnected;
            transport.Message += OnMessage;
            transport.PeerDisconnected += OnPeerDisconnected;
        }

        public void Start()
        {
            PolarisNoelsTools.LocalID = 0;
            PolarisNoelsTools.Type = NetWorkType.Host;
            // 开始监听前必须就绪：本地外观配置（昵称/颜色）与要同步的存档内容。
            if (DB.InitConfig == null)
            {
                if (config != null)
                {
                    DB.InitConfig = config;
                }
                else
                {
                    Plugin.Logger.LogError("host: DB.InitConfig is null when starting to listen; local appearance will be missing");
                }
            }
            if (DB.SyncSaveContentBuffer == null)
            {
                Plugin.Logger.LogWarning("host: no save buffer when starting to listen; late joiners will not receive an archive");
            }
            if (DB.InitConfig != null && !DB.peerConfigs.ContainsKey(0))
            {
                DB.peerConfigs[0] = DB.InitConfig;
            }
            if (!DB.partyInfos.ContainsKey(0))
            {
                DB.partyInfos[0] = PartyManager.InitNewParty(0);
            }
            transport.StartHost();
        }

        /// <summary>人数上限由原生 pn_config.max_peers 保证；这里只做业务记录。</summary>
        void OnPeerConnected(int peer)
        {
            Plugin.Logger.LogInfo($"host: peer {peer} connected, sending handshake");
            SendHandshake(peer);
        }

        public void SendHandshake(int peer)
        {
            PolarisNoelsHostMessage message = new()
            {
                InitID = peer,
                PeerConfigs = [.. DB.peerConfigs],
                PeerParties = [.. DB.partyInfos],
                SyncHost = PolarisNoelsTools.SimBattleSyncHost,
                SyncConnectedList = PolarisNoelsTools.SimBattleSyncList
            };
            transport.Send(peer, NetChannel.Reliable, HandshakeCodec.Encode(HandshakeCodec.KindHostMessage, message));
            SendSave(peer);
        }

        /// <summary>存档是入房必需行为：BULK 通道 + SaveTransfer gzip，与 game protobuf 不共用流。</summary>
        void SendSave(int peer)
        {
            if (DB.SyncSaveContentBuffer == null)
            {
                Plugin.Logger.LogWarning("host: no sync save content, skip archive transfer");
                return;
            }
            byte[] packed = SaveTransfer.Pack(DB.SyncSaveContentBuffer);
            Plugin.Logger.LogInfo($"host: sending archive to peer {peer} ({DB.SyncSaveContentBuffer.Length} -> {packed.Length} bytes)");
            transport.Send(peer, NetChannel.Bulk, BulkCodec.Wrap(BulkCodec.KindSaveArchive, packed));
        }

        void OnMessage(int peer, NetChannel channel, byte[] data)
        {
            if (channel != NetChannel.Reliable)
            {
                return;
            }
            if (!HandshakeCodec.TryDecode(data, HandshakeCodec.KindClientMessage, out PolarisNoelsClientMessage message))
            {
                return;
            }
            // 键用发送者 id（来自原生层），不信消息里自报的 ID。
            DB.peerConfigs[peer] = new()
            {
                Nickname = message.NickName,
                NoelType = message.NoelType,
                NoelColor = message.NoelColor
            };
            DB.partyInfos[peer] = message.Party ?? PartyManager.InitNewParty(peer);
            Plugin.Logger.LogInfo($"host: recorded player {peer} ({message.NickName})");
        }

        void OnPeerDisconnected(int peer, DisconnectReason reason)
        {
            Plugin.Logger.LogInfo($"host: peer {peer} disconnected ({reason})");
            PolarisNoelsTools.CleanUpClient(peer);
        }

        public void SendMute(int id)
        {
            PolarisNoelsHostMessage message = new()
            {
                MutePlayer = true,
                PlayerID = id
            };
            transport.Send(id, NetChannel.Reliable, HandshakeCodec.Encode(HandshakeCodec.KindHostMessage, message));
        }

        public void Shutdown()
        {
            transport.PeerConnected -= OnPeerConnected;
            transport.Message -= OnMessage;
            transport.PeerDisconnected -= OnPeerDisconnected;
        }
    }
}
