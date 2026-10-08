using System;
using System.Collections.Generic;
using System.IO;
using PolarisNoels.CSNetworking;
using PolarisNoels.Networking.Native;

namespace PolarisNoels.Networking
{
    /// <summary>
    /// 联机运行时单例：持有唯一的 INetTransport（标题/游戏/加载期间都不销毁），
    /// 负责原生库加载、ABI 校验、事件路由，以及加载期间「只延迟业务分发、不暂停 poll」。
    /// </summary>
    public static class NetworkRuntime
    {
        const int MaxDeferredMessages = 4096;

        /// <summary>托管延迟队列的总字节上限，防止加载期间大量 BULK 游戏包把内存吃光。</summary>
        const long MaxDeferredBytes = 32L * 1024 * 1024;

        /// <summary>恢复分发后每帧最多排空多少条，避免一帧卡死。</summary>
        const int MaxFlushPerFrame = 256;

        static readonly HashSet<int> connectedPeers = [];
        static readonly LinkedList<DeferredMessage> deferred = new();

        static long deferredBytes;
        static bool deferredPaused;
        static bool deferredOverflowLogged;

        static Action<int, NetChannel, byte[]> gameHandler;

        public static INetTransport Transport { get; private set; }

        /// <summary>0 = 随机端口；值存在 Polaris 设置页（<see cref="NoelsSettings"/>）。</summary>
        public static int BindPort => NoelsSettings.BindPort;

        /// <summary>STUN 开关；由 BepInEx 配置与游戏内设置共同决定。</summary>
        public static bool EnableStun { get => NoelsSettings.EnableStun; set => NoelsSettings.EnableStun = value; }

        /// <summary>BepInEx 配置的网络人数上限（与游戏内 DB.MaxPlayerCount 取较小值）。</summary>
        public static int ConfiguredMaxPeers => NoelsSettings.MaxPlayers;

        public static HostSession Host { get; private set; }

        public static ClientSession Client { get; private set; }

        /// <summary>原生库缺失/ABI 不符时为 true：只禁用联机，不影响单机。</summary>
        public static bool NativeUnavailable { get; private set; }

        public static string NativeError { get; private set; }
        public static string NatType { get; private set; }

        public static bool IsActive => Transport is { IsReady: true };

        public static IReadOnlyCollection<int> ConnectedPeers => connectedPeers;

        public static event Action<int> PeerConnected;

        public static event Action<int, DisconnectReason> PeerDisconnected;

        public static event Action RoomCodeChanged;

        public static event Action ClientUpdated;

        /// <summary>地图加载期间业务分发延后（消息在托管队列里等着），但 pn_poll 照常每帧执行。</summary>
        public static bool DeferGameDispatch => DB.InitConfig != null
            && (DB.MainPR == null || DB.MainPR.Mp == null || DB.MainPR.NM2D?.transferring_game_stopping == true);

        /// <summary>延迟队列触及硬上限时暂停 pn_poll（QUIC 背压），等业务侧排空后自动恢复。</summary>
        public static bool DeferredPaused => deferredPaused;

        /// <summary>当前延迟队列里的消息数（诊断/测试用）。</summary>
        public static int DeferredCount => deferred.Count;

        /// <summary>Plugin.Awake 最前面调用：预加载 DLL 并比对 ABI。失败只禁用联机。</summary>
        public static bool PreloadNative()
        {
            if (PnNativeLoader.IsLoaded)
            {
                return true;
            }
            string assemblyDir = Path.GetDirectoryName(typeof(NetworkRuntime).Assembly.Location);
            string[] candidates =
            [
                assemblyDir,
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory()
            ];
            if (!PnNativeLoader.TryLoadFromCandidates(candidates, out string error))
            {
                NativeUnavailable = true;
                NativeError = error;
                Plugin.Logger.LogError($"multiplayer disabled: {error}");
                return false;
            }
            Plugin.Logger.LogInfo($"native transport loaded: {PnNativeLoader.LoadedPath} (ABI {PnAbi.Version})");
            return true;
        }

        public static bool EnsureTransport(int maxPeers)
        {
            if (IsActive)
            {
                return true;
            }
            if (NativeUnavailable || !PreloadNative())
            {
                return false;
            }
            NativeTransport transport = new(new NetTransportConfig
            {
                BindPort = BindPort,
                MaxPeers = Math.Clamp(Math.Min(maxPeers, ConfiguredMaxPeers), 2, 8),
                EnableStun = EnableStun,
                EnableLan = true
            }, OnNativeLog);
            if (!transport.Init(out string error))
            {
                NativeError = error;
                Plugin.Logger.LogError($"multiplayer disabled: {error}");
                return false;
            }
            transport.ContinuePolling = () => !deferredPaused;
            transport.PeerConnected += OnPeerConnected;
            transport.PeerDisconnected += OnPeerDisconnected;
            transport.Message += OnMessage;
            transport.RoomCodeReady += _ => RoomCodeChanged?.Invoke();
            transport.NatInfo += json =>
            {
                // NAT_STATE contains the host room secret; only log safe diagnostics.
                try { NatType = (string)Newtonsoft.Json.Linq.JObject.Parse(json)["nat"]; }
                catch { NatType = "unknown"; }
                Plugin.Logger.LogInfo($"nat type: {NatType}");
            };
            transport.Log += OnTransportLog;
            Transport = transport;
            return true;
        }

        /// <summary>标题界面主机流程：启动监听并生成房间码。</summary>
        public static bool StartHost(PolarisNoelsTools.NetworkConfig config)
        {
            if (!EnsureTransport(DB.MaxPlayerCount))
            {
                return false;
            }
            Host?.Shutdown();
            Client?.Shutdown();
            Client = null;
            Host = new HostSession(Transport, config);
            Host.Start();
            return true;
        }

        /// <summary>标题界面客户端流程：房间码入房（directHost 非空时走 pn_join_direct 高级直连）。</summary>
        public static bool StartClient(PolarisNoelsTools.NetworkConfig config, string roomCode, string directHost = null, int directPort = 0)
        {
            if (!EnsureTransport(DB.MaxPlayerCount))
            {
                return false;
            }
            Host?.Shutdown();
            Client?.Shutdown();
            Host = null;
            Client = new ClientSession(Transport);
            Client.Updated += () => ClientUpdated?.Invoke();
            if (!string.IsNullOrWhiteSpace(directHost))
            {
                Plugin.Logger.LogInfo($"joining direct {directHost}:{directPort}");
                Transport.JoinDirect(directHost, directPort);
            }
            else
            {
                Transport.Join(roomCode);
            }
            return true;
        }

        public static void RegisterGameHandler(Action<int, NetChannel, byte[]> handler)
        {
            gameHandler = handler;
        }

        public static void UnregisterGameHandler(Action<int, NetChannel, byte[]> handler)
        {
            if (gameHandler == handler)
            {
                gameHandler = null;
            }
        }

        /// <summary>Plugin.Update 每帧调用，标题/游戏/加载三种状态都执行。</summary>
        public static void Poll()
        {
            // 延迟队列硬溢出时暂停读取（原生事件留在队列里 = QUIC 背压），排空后恢复。
            if (!deferredPaused)
            {
                Transport?.Poll();
            }
            if (deferredPaused && deferred.Count <= MaxDeferredMessages / 2)
            {
                deferredPaused = false;
                deferredOverflowLogged = false;
            }
            // 只有业务分发器可用且不在加载中才排空；标题期 gameHandler 为空时保留到进游戏。
            if (gameHandler != null && !DeferGameDispatch)
            {
                FlushDeferred();
            }
        }

        public static void Shutdown()
        {
            Host?.Shutdown();
            Client?.Shutdown();
            Host = null;
            Client = null;
            Transport?.Shutdown();
            Transport?.Dispose();
            Transport = null;
            gameHandler = null;
            connectedPeers.Clear();
            NatType = null;
            deferred.Clear();
            deferredBytes = 0;
            deferredPaused = false;
            deferredOverflowLogged = false;
        }

        static void OnMessage(int peer, NetChannel channel, byte[] data)
        {
            if (channel == NetChannel.Reliable && HandshakeCodec.HasMagic(data))
            {
                // 握手消息由 Host/ClientSession 自己的订阅处理。
                return;
            }
            if (channel == NetChannel.Bulk)
            {
                if (!BulkCodec.TryUnwrap(data, out byte kind, out byte[] payload))
                {
                    return;
                }
                if (kind == BulkCodec.KindSaveArchive)
                {
                    if (peer == 0) Client?.OnSaveArchive(payload);
                    return;
                }
                if (kind == BulkCodec.KindGameMessage)
                {
                    Deliver(peer, NetChannel.Reliable, payload);
                    return;
                }
                Plugin.Logger.LogWarning($"unknown bulk kind {kind} from peer {peer}, dropped");
                return;
            }
            Deliver(peer, channel, data);
        }

        static void Deliver(int peer, NetChannel channel, byte[] data)
        {
            // gameHandler 未就绪（标题期）、加载中、或已有积压时都必须排队：
            // 排队保证同一通道的消息不会越过更早的消息先分发。
            if (gameHandler == null || DeferGameDispatch || deferred.Count > 0)
            {
                EnqueueDeferred(peer, channel, data);
                return;
            }
            DispatchNow(peer, channel, data);
        }

        static void EnqueueDeferred(int peer, NetChannel channel, byte[] data)
        {
            int length = data?.Length ?? 0;
            bool overCount = deferred.Count >= MaxDeferredMessages;
            bool overBytes = deferredBytes + length > MaxDeferredBytes;
            if (overCount || overBytes)
            {
                // 优先丢 Sequenced（本身允许丢），可靠消息不丢。
                if (channel == NetChannel.Sequenced)
                {
                    Plugin.Logger.LogWarning($"deferred queue full, dropped sequenced message from peer {peer}");
                    return;
                }
                if (TryEvictOldestSequenced())
                {
                    overCount = deferred.Count >= MaxDeferredMessages;
                    overBytes = deferredBytes + length > MaxDeferredBytes;
                }
                if (overCount || overBytes)
                {
                    // 可靠消息不能静默丢：暂停 pn_poll 做背压（原生队列会触发 QUIC 流控），
                    // 这一条仍然入队，等业务 handler 排空后再恢复读取。
                    deferredPaused = true;
                    if (!deferredOverflowLogged)
                    {
                        deferredOverflowLogged = true;
                        Plugin.Logger.LogError(
                            $"deferred reliable queue overflow ({deferred.Count} msgs / {deferredBytes} bytes); pausing transport poll for backpressure");
                    }
                }
            }
            deferred.AddLast(new DeferredMessage(peer, channel, data));
            deferredBytes += length;
        }

        static bool TryEvictOldestSequenced()
        {
            for (LinkedListNode<DeferredMessage> node = deferred.First; node != null; node = node.Next)
            {
                if (node.Value.Channel != NetChannel.Sequenced)
                {
                    continue;
                }
                deferredBytes -= node.Value.Data?.Length ?? 0;
                deferred.Remove(node);
                Plugin.Logger.LogWarning("deferred queue full, evicted oldest sequenced message");
                return true;
            }
            return false;
        }

        static void DispatchNow(int peer, NetChannel channel, byte[] data)
        {
            try
            {
                gameHandler?.Invoke(peer, channel, data);
            }
            catch (Exception e)
            {
                // 单条消息失败不能中断后续分发。
                Plugin.Logger.LogError($"game message dispatch failed (peer={peer}, channel={channel}): {e}");
            }
        }

        static void FlushDeferred()
        {
            int budget = MaxFlushPerFrame;
            while (budget-- > 0 && deferred.First != null)
            {
                DeferredMessage message = deferred.First.Value;
                deferred.RemoveFirst();
                deferredBytes -= message.Data?.Length ?? 0;
                DispatchNow(message.Peer, message.Channel, message.Data);
            }
        }

        static void OnPeerConnected(int peer)
        {
            connectedPeers.Add(peer);
            PeerConnected?.Invoke(peer);
        }

        static void OnPeerDisconnected(int peer, DisconnectReason reason)
        {
            connectedPeers.Remove(peer);
            for (var node = deferred.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.Peer == peer) { deferredBytes -= node.Value.Data?.Length ?? 0; deferred.Remove(node); }
                node = next;
            }
            PeerDisconnected?.Invoke(peer, reason);
        }

        static void OnNativeLog(int level, string text)
        {
            if (level <= 1)
            {
                Plugin.Logger.LogError($"[polaris_net] {text}");
            }
            else if (level == 2)
            {
                Plugin.Logger.LogWarning($"[polaris_net] {text}");
            }
            else if (level == 4)
            {
                Plugin.Logger.LogDebug($"[polaris_net] {text}");
            }
            else
            {
                Plugin.Logger.LogInfo($"[polaris_net] {text}");
            }
        }

        static void OnTransportLog(int level, string text)
        {
            // 原生事件日志已经在 OnNativeLog 落盘，这里避免重复；仅保留顺序事件的调试信息。
            if (level == 4)
            {
                Plugin.Logger.LogDebug($"[transport] {text}");
            }
        }

        readonly struct DeferredMessage
        {
            public readonly int Peer;
            public readonly NetChannel Channel;
            public readonly byte[] Data;

            public DeferredMessage(int peer, NetChannel channel, byte[] data)
            {
                Peer = peer;
                Channel = channel;
                Data = data;
            }
        }
    }
}
