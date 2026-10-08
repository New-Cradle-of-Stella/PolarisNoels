using System.Collections.Generic;
using ProtoBuf;
using System.IO;
using UnityEngine;
using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking
{
    /// <summary>
    /// 游戏内 P2P 外壳：不再持有 NetManager，只负责业务分发、地图过滤与实体补发。
    /// 轮询由 Plugin.Update 通过 NetworkRuntime 统一执行（标题/游戏/加载都 poll）。
    /// </summary>
    public class PolarisNoelsPeer : MonoBehaviour
    {
        readonly MapInterest maps = new();

        float nextAnnounce;
        float nextDelayRefresh;

        string CurrentMapKey => DB.MainPR?.NM2D?.transferring_game_stopping == true ? "" : DB.MainPR?.Mp?.key;

        public bool IsPeerOnCurrentMap(int id) => maps.IsSameMap(id, CurrentMapKey);

        public string GetPeerMap(int id) => maps.GetPeerMap(id);
        public string LocalMapVisit => maps.LocalVisit;
        public string GetPeerMapVisit(int id) => id == PolarisNoelsTools.LocalID ? maps.LocalVisit : maps.GetPeerVisit(id);

        static INetTransport Transport => NetworkRuntime.Transport;

        void Awake()
        {
            NetworkRuntime.RegisterGameHandler(OnGameMessage);
            NetworkRuntime.PeerConnected += OnPeerConnected;
            NetworkRuntime.PeerDisconnected += OnPeerDisconnected;
        }

        /// <summary>
        /// 外壳是在标题期完成连接之后才创建的：对已经连上的 peer 补一次宣告，
        /// 否则它们要等下一个 2 秒广播周期才知道本机进场。
        /// </summary>
        void Start()
        {
            if (NetworkRuntime.ConnectedPeers.Count > 0)
            {
                AnnounceIfDue();
            }
        }

        void OnPeerConnected(int peer)
        {
            Plugin.Logger.LogInfo($"peer connected: {peer}");
            AnnounceIfDue();
            SendMapPresence();
        }

        void OnPeerDisconnected(int peer, DisconnectReason reason)
        {
            Plugin.Logger.LogInfo($"peer {peer} disconnected ({reason})");
            maps.Remove(peer);
            PolarisNoelsTools.CleanUpClient(peer);
        }

        const float AnnounceInterval = 2f;

        /// <summary>周期性宣告本机玩家的存在，保证后加入的玩家一定能看到我们。</summary>
        void AnnounceIfDue()
        {
            bool changedMap = maps.SetLocalMap(CurrentMapKey);
            if (changedMap) CombatSync.OnLocalMapChanged();
            if (!changedMap && Time.time < nextAnnounce)
            {
                return;
            }
            nextAnnounce = Time.time + AnnounceInterval;
            EntityNet.AnnounceLocalPlayer();
            SendMapPresence();
            if (changedMap)
            {
                foreach (int id in NetworkRuntime.ConnectedPeers)
                {
                    if (IsPeerOnCurrentMap(id)) EntityNet.SendCurrentMapToPeer(id);
                }
            }
        }

        void SendMapPresence(int? targetId = null)
        {
            if (PolarisNoelsTools.LocalID < 0) return;
            string mapKey = CurrentMapKey ?? "";
            float x = 0, y = 0;
            if (!string.IsNullOrEmpty(mapKey)) DB.MainPR.getPosition(out x, out y);
            PolarisNoelsPeerMessage message = new()
            {
                Type = PolarisNoelsPeerMessageType.UpdatePeerInfo,
                PeerId = PolarisNoelsTools.LocalID,
                UpdatePeerInfo = new() { Type = UpdatePeerType.Map, MapKey = mapKey, PositionX = x, PositionY = y, MapVisit = maps.LocalVisit }
            };
            if (targetId.HasValue) SendToPeer(targetId.Value, message, NetChannel.Reliable);
            else PolarisNoelsTools.Broadcast(message);
        }

        void Update()
        {
            // 延迟 UI 每秒刷新一次，避免每帧调 pn_peer_stats。
            RefreshDelays();
            // 切图时即使暂时没有主角，也要广播离开原地图的通知。
            AnnounceIfDue();
            if (DB.MainPR == null || DB.MainPR.Mp == null || DB.MainPR.NM2D.transferring_game_stopping)
            {
                return;
            }
            if (PolarisNoelsTools.Type == NetWorkType.Host)
            {
                PolarisNoelsTools.UpdateRoomConfigToAllPeers();
            }
            PolarisNoelsTools.UpdateAllNoels();
            PolarisNoelsTools.SetAllNickNameBgs();
            BattleSession.Update();
            CombatSync.Update();
        }

        void RefreshDelays()
        {
            if (Time.time < nextDelayRefresh || Transport == null)
            {
                return;
            }
            nextDelayRefresh = Time.time + 1f;
            foreach (int id in NetworkRuntime.ConnectedPeers)
            {
                int rtt = Transport.GetRttMs(id);
                if (rtt > 0)
                {
                    DB.peerDelays[id] = rtt;
                }
            }
        }

        void OnGameMessage(int peerId, NetChannel channel, byte[] receivedData)
        {
            PolarisNoelsPeerMessage message;
            try
            {
                using MemoryStream stream = new(receivedData);
                message = Serializer.Deserialize<PolarisNoelsPeerMessage>(stream);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning($"bad peer message dropped: {e.Message}");
                return;
            }
            // 业务里的自报 ID 必须绑定 QUIC 给出的真实来源；结束通知只信战斗发起者。
            if (message.PeerId != peerId) return;
            if (message.Type == PolarisNoelsPeerMessageType.UpdatePeerInfo && message.UpdatePeerInfo?.Type == UpdatePeerType.Map)
            {
                bool changedMap = maps.SetPeerMap(message.PeerId, message.UpdatePeerInfo.MapKey, message.UpdatePeerInfo.MapVisit);
                if (DB.noelIns.TryGetValue(message.PeerId, out var ins))
                {
                    ins.MpKey = maps.GetPeerMap(message.PeerId);
                    // 异图不传动作快照，但传送菜单仍需要该地图里的低频位置。
                    if (!IsPeerOnCurrentMap(message.PeerId))
                    {
                        ins.NoelInfo.PositionX = message.UpdatePeerInfo.PositionX;
                        ins.NoelInfo.PositionY = message.UpdatePeerInfo.PositionY;
                    }
                }
                if (changedMap)
                {
                    if (IsPeerOnCurrentMap(message.PeerId))
                    {
                        SendMapPresence(message.PeerId);
                        EntityNet.SendCurrentMapToPeer(message.PeerId);
                    }
                    else EntityRegistry.DestroyReplicasOwnedBy(message.PeerId);
                }
            }
            // A target owner on another map still needs to reject a request explicitly.
            // Combat packets validate map visits and ownership themselves.
            if (message.Type == PolarisNoelsPeerMessageType.Entity &&
                (message.Entity?.Event?.Type == EntityEventType.DamageRequest || message.Entity?.Event?.Type == EntityEventType.DamageResult))
            {
                try { EntityNet.Receive(message); }
                catch (System.Exception e) { Plugin.Logger.LogError($"combat message failed: {e}"); }
                return;
            }
            // 可靠事件和数据报可能跨越切图边界；发送端过滤之外再挡住旧地图的包。
            if (!string.IsNullOrEmpty(message.MapKey)
                && (message.MapKey != DB.MainPR?.Mp?.key || !maps.IsSameMap(message.PeerId, message.MapKey))) return;
            foreach (PeerReceiveMessageBase receive in ReceiveMessageManager.GetAllReceives())
            {
                try
                {
                    if (receive.CheckMessage(message))
                    {
                        if (DB.ShowReceiveDebug)
                        {
                            Plugin.Logger.LogInfo(receive.ToMessageString(message));
                        }
                        receive.ReceiveMessage(message);
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Logger.LogError($"{receive.GetType().Name} failed: {e}");
                }
            }
            int rtt = Transport?.GetRttMs(peerId) ?? 0;
            if (rtt > 0)
            {
                DB.peerDelays[message.PeerId] = rtt;
            }
        }

        public void SendToAll(byte[] content, NetChannel channel)
        {
            Transport?.Send(-1, channel, content);
        }

        public bool HasMapRecipients(string mapKey)
        {
            if (string.IsNullOrEmpty(CurrentMapKey) || mapKey != CurrentMapKey) return false;
            foreach (int id in NetworkRuntime.ConnectedPeers)
            {
                if (maps.IsSameMap(id, mapKey)) return true;
            }
            return false;
        }

        public void SendToMap(byte[] content, NetChannel channel, string mapKey)
        {
            foreach (int id in NetworkRuntime.ConnectedPeers)
            {
                if (maps.IsSameMap(id, mapKey))
                {
                    Transport?.Send(id, channel, content);
                }
            }
        }

        public void SendToPeer(int id, PolarisNoelsPeerMessage message, NetChannel channel)
        {
            using MemoryStream stream = new();
            Serializer.Serialize(stream, message);
            Transport?.Send(id, channel, stream.ToArray());
        }

        void OnDestroy()
        {
            NetworkRuntime.UnregisterGameHandler(OnGameMessage);
            NetworkRuntime.PeerConnected -= OnPeerConnected;
            NetworkRuntime.PeerDisconnected -= OnPeerDisconnected;
            DB.InitConfig = null;
            DB.noelIns.Clear();
            DB.partyInfos.Clear();
            DB.peerConfigs.Clear();
            DB.peerDelays.Clear();
            DB.CleanUp();
            PolarisNoelsTools.CleanUp();
            // 离开游戏就退房：远端关闭/被踢时收尾，本地主动退出时也要断开而不是把连接悬在房间里。
            if (DB.PolarisNoelsHostClosed || DB.PolarisNoelsHostKicked || NetworkRuntime.IsActive)
            {
                NetworkRuntime.Shutdown();
            }
        }
    }
}
