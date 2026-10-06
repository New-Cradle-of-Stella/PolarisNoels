using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PolarisNoels.Networking.Native;

namespace PolarisNoels.Networking
{
    /// <summary>
    /// INetTransport 的原生实现（P/Invoke + 轮询）。此类型不依赖 Unity/游戏程序集，NetSmoke 直接链接源文件使用。
    /// 所有 pn_* 调用都发生在调用方线程（游戏内 = Unity 主线程），原生库内部自己保证线程安全。
    /// </summary>
    public sealed class NativeTransport : INetTransport
    {
        /// <summary>推荐的事件载荷缓冲，PN_ERR_BUFFER 时按需翻倍（上限 64MB）。</summary>
        public const int DefaultBufferSize = 64 * 1024;

        const int MaxEventsPerPoll = 256;

        const int MaxBufferSize = 64 * 1024 * 1024;

        readonly NetTransportConfig config;
        readonly Action<int, string> logSink;
        readonly ReliableSendQueue reliableQueue;
        readonly HashSet<int> connectedPeers = new();

        byte[] buffer;
        bool initialized;
        bool disposed;
        string roomCode;
        string lastErrorText;

        public NativeTransport(NetTransportConfig config, Action<int, string> logSink = null, int bufferSize = DefaultBufferSize)
        {
            this.config = config ?? new NetTransportConfig();
            this.logSink = logSink;
            buffer = new byte[Math.Max(64, bufferSize)];
            reliableQueue = new ReliableSendQueue(TrySendRaw, OnSendTargetFailed);
        }

        public bool IsReady => initialized;
        public Func<bool> ContinuePolling { get; set; }

        public string LastError => lastErrorText;

        public int LocalPeerId => initialized ? SafeCall(PnNative.pn_local_peer_id, -1) : -1;

        public int PeerCount => initialized ? SafeCall(PnNative.pn_peer_count, 0) : 0;

        public string RoomCode => roomCode;

        public event Action<string> RoomCodeReady;
        public event Action<string> NatInfo;
        public event Action<int> JoinResult;
        public event Action<int> PeerConnected;
        public event Action<int, DisconnectReason> PeerDisconnected;
        public event Action<int, NetChannel, byte[]> Message;
        public event Action<int, uint, uint, uint> BulkProgress;
        public event Action<int, string> Log;

        /// <summary>初始化原生运行时。失败返回 false 并把原因写到 error（调用方据此禁用联机）。</summary>
        public bool Init(out string error)
        {
            error = null;
            if (initialized)
            {
                return true;
            }
            if (disposed)
            {
                error = "transport already disposed";
                return false;
            }
            IntPtr stunPtr = IntPtr.Zero;
            try
            {
                if (!string.IsNullOrEmpty(config.StunServers))
                {
                    stunPtr = Marshal.StringToHGlobalAnsi(config.StunServers);
                }
                PnConfig native = new()
                {
                    AbiVersion = PnAbi.Version,
                    BindPort = (ushort)Math.Clamp(config.BindPort, 0, 65535),
                    MaxPeers = (ushort)Math.Clamp(config.MaxPeers, 2, 8),
                    EnableStun = (byte)(config.EnableStun ? 1 : 0),
                    EnableLan = (byte)(config.EnableLan ? 1 : 0),
                    EnableUpnp = 0,
                    StunServers = stunPtr,
                    IdleTimeoutMs = (uint)Math.Max(0, config.IdleTimeoutMs)
                };
                int rc = PnNative.pn_init(ref native);
                if (rc != PnAbi.Ok)
                {
                    error = $"pn_init failed: {rc} ({Describe(rc)}) {ReadLastError()}";
                    return false;
                }
            }
            catch (Exception e)
            {
                error = $"pn_init threw: {e.Message}";
                return false;
            }
            finally
            {
                if (stunPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(stunPtr);
                }
            }
            initialized = true;
            EmitLog(3, "native transport initialized");
            return true;
        }

        public void StartHost()
        {
            if (!initialized) return;
            int rc = SafeCall(PnNative.pn_host_start, PnAbi.ErrState);
            if (rc != PnAbi.Ok)
            {
                EmitLog(1, $"pn_host_start failed: {rc} ({Describe(rc)}) {ReadLastError()}");
                return;
            }
            EmitLog(3, "pn_host_start accepted, collecting candidates");
        }

        public void Join(string roomCode)
        {
            if (!initialized) return;
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                EmitLog(1, "pn_join called with an empty room code");
                RaiseJoinResult((int)NetJoinError.BadCode);
                return;
            }
            // 房间码里可能有复制粘贴带来的空白/换行。
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(roomCode.Trim());
            int rc;
            unsafe
            {
                fixed (byte* p = raw)
                {
                    rc = PnNative.pn_join(p, (uint)raw.Length);
                }
            }
            if (rc != PnAbi.Ok)
            {
                EmitLog(1, $"pn_join failed: {rc} ({Describe(rc)}) {ReadLastError()}");
                RaiseJoinResult((int)NetJoinError.BadCode);
            }
        }

        public void JoinDirect(string host, int port)
        {
            if (!initialized) return;
            int rc = PnNative.pn_join_direct(host, (ushort)Math.Clamp(port, 0, 65535), IntPtr.Zero);
            if (rc != PnAbi.Ok)
            {
                EmitLog(1, $"pn_join_direct({host}:{port}) failed: {rc} ({Describe(rc)}) {ReadLastError()}");
                RaiseJoinResult((int)NetJoinError.HandshakeFailed);
            }
        }

        public void Send(int peer, NetChannel channel, byte[] data)
        {
            if (!initialized || data == null) return;
            if (channel == NetChannel.Sequenced)
            {
                // 序列通道本身允许丢包：原生层队列满时丢最旧的，这里拿到 -5 说明连发送资格都没有。
                int rc = TrySend(peer, channel, data);
                if (rc == PnAbi.Ok)
                {
                    return;
                }
                if (rc == PnAbi.ErrQueueFull)
                {
                    EmitLog(2, $"sequenced send dropped (queue full) peer={peer} len={data.Length}");
                    return;
                }
                EmitLog(1, $"pn_send failed peer={peer} channel={channel} rc={rc} ({Describe(rc)}) {ReadLastError()}");
                return;
            }
            // 可靠/大块走托管重试队列：FIFO、有限字节、溢出明确断开，绝不静默丢弃。
            reliableQueue.Send(peer, channel, data);
        }

        public void Disconnect(int peer, DisconnectReason reason)
        {
            if (!initialized) return;
            int rc = PnNative.pn_disconnect(peer, (int)reason);
            if (rc != PnAbi.Ok && rc != PnAbi.ErrNotFound)
            {
                EmitLog(2, $"pn_disconnect peer={peer} reason={reason} rc={rc} ({Describe(rc)})");
            }
        }

        public int GetRttMs(int peer)
        {
            if (!initialized) return 0;
            if (PnNative.pn_peer_stats(peer, out PnPeerStats stats) != PnAbi.Ok)
            {
                return 0;
            }
            return (int)stats.RttMs;
        }

        public void Poll()
        {
            if (!initialized || disposed) return;
            reliableQueue.Retry();
            // Retry 会通过日志回调间接调用上层代码，可能已经触发 Shutdown。
            if (!initialized || disposed) return;
            int processed = 0;
            while (processed < MaxEventsPerPoll)
            {
                if (ContinuePolling != null && !ContinuePolling()) break;
                PnEvent ev;
                int rc;
                unsafe
                {
                    fixed (byte* p = buffer)
                    {
                        rc = PnNative.pn_poll(&ev, p, (uint)buffer.Length);
                    }
                }
                if (rc == PnAbi.ErrBuffer)
                {
                    // 事件没丢：ev.Len 是真实长度，扩容后重试同一条事件。
                    if (ev.Len <= buffer.Length || !GrowBuffer(ev.Len))
                    {
                        EmitLog(1, $"pn_poll buffer too small (need {ev.Len}, cap {buffer.Length})");
                        break;
                    }
                    continue;
                }
                if (rc == 0)
                {
                    break;
                }
                if (rc < 0)
                {
                    EmitLog(1, $"pn_poll failed: {rc} ({Describe(rc)}) {ReadLastError()}");
                    break;
                }
                processed++;
                Dispatch(ref ev);
                // 订阅者在回调里 Shutdown 之后绝不能再调 pn_poll。
                if (!initialized || disposed) return;
            }
        }

        /// <summary>
        /// 计算 pn_poll 缓冲扩容目标：翻倍，但 clamp 到 cap；只要 needed &lt;= cap 就必须能扩容成功
        /// （否则合法的 5MB BULK 事件会被 64MB 上限之外的翻倍逻辑卡死）。
        /// </summary>
        public static bool TryComputeBufferGrowth(long current, uint needed, long cap, out long target)
        {
            target = 0;
            if (needed > cap)
            {
                return false;
            }
            long want = Math.Max(current * 2, needed);
            if (want > cap)
            {
                // 翻倍会超过上限时 clamp 到上限，只要还能满足 needed 就不算失败。
                want = cap;
            }
            if (want < needed)
            {
                return false;
            }
            target = want;
            return true;
        }

        bool GrowBuffer(uint needed)
        {
            if (!TryComputeBufferGrowth(buffer.Length, needed, MaxBufferSize, out long target))
            {
                return false;
            }
            buffer = new byte[target];
            return true;
        }

        void Dispatch(ref PnEvent ev)
        {
            switch (ev.Type)
            {
                case PnAbi.EvPeerConnected:
                    connectedPeers.Add(ev.Peer);
                    SafeEvents.Raise(PeerConnected, ev.Peer, e => ReportCallbackError("PeerConnected", e));
                    break;
                case PnAbi.EvPeerDisconnected:
                    connectedPeers.Remove(ev.Peer);
                    SafeEvents.Raise(PeerDisconnected, ev.Peer, (DisconnectReason)ev.Code, e => ReportCallbackError("PeerDisconnected", e));
                    break;
                case PnAbi.EvMessage:
                    SafeEvents.Raise(Message, ev.Peer, (NetChannel)ev.Channel, CopyPayload(ev.Len), e => ReportCallbackError("Message", e));
                    break;
                case PnAbi.EvBulkProgress:
                    SafeEvents.Raise(BulkProgress, ev.Peer, ev.Aux2, ev.Aux0, ev.Aux1, e => ReportCallbackError("BulkProgress", e));
                    break;
                case PnAbi.EvNatState:
                    string nat = PnText.Utf8(buffer, 0, (int)Math.Min(ev.Len, (uint)buffer.Length));
                    SafeEvents.Raise(NatInfo, nat, e => ReportCallbackError("NatInfo", e));
                    RefreshRoomCode();
                    break;
                case PnAbi.EvJoinResult:
                    RaiseJoinResult(ev.Code);
                    break;
                case PnAbi.EvLog:
                    EmitLog(ev.Code, PnText.Utf8(buffer, 0, (int)Math.Min(ev.Len, (uint)buffer.Length)));
                    break;
                case PnAbi.EvNone:
                    break;
                default:
                    EmitLog(4, $"unknown pn_event type {ev.Type} ignored");
                    break;
            }
        }

        void RaiseJoinResult(int code)
        {
            SafeEvents.Raise(JoinResult, code, e => ReportCallbackError("JoinResult", e));
        }

        byte[] CopyPayload(uint len)
        {
            int count = (int)Math.Min(len, (uint)buffer.Length);
            byte[] copy = new byte[count];
            Buffer.BlockCopy(buffer, 0, copy, 0, count);
            return copy;
        }

        /// <summary>候选收集完成后房间码才有效，NAT_STATE 是唯一的“可以取了”的信号。</summary>
        void RefreshRoomCode()
        {
            if (!initialized) return;
            string code = QueryRoomCode();
            if (string.IsNullOrEmpty(code) || code == roomCode)
            {
                return;
            }
            roomCode = code;
            SafeEvents.Raise(RoomCodeReady, code, e => ReportCallbackError("RoomCodeReady", e));
        }

        public string QueryRoomCode()
        {
            if (!initialized) return null;
            byte[] tmp = new byte[512];
            uint len = 0;
            int rc;
            unsafe
            {
                fixed (byte* p = tmp)
                {
                    rc = PnNative.pn_room_code(p, (uint)tmp.Length, &len);
                }
            }
            if (rc == PnAbi.ErrBuffer)
            {
                tmp = new byte[Math.Max(len, 1024u)];
                unsafe
                {
                    fixed (byte* p = tmp)
                    {
                        rc = PnNative.pn_room_code(p, (uint)tmp.Length, &len);
                    }
                }
            }
            if (rc != PnAbi.Ok || len == 0)
            {
                return null;
            }
            return PnText.Utf8(tmp, 0, (int)Math.Min(len, (uint)tmp.Length));
        }

        public string ReadLastError()
        {
            try
            {
                byte[] tmp = new byte[512];
                uint len = 0;
                int rc;
                unsafe
                {
                    fixed (byte* p = tmp)
                    {
                        rc = PnNative.pn_last_error(p, (uint)tmp.Length, &len);
                    }
                }
                if (rc != PnAbi.Ok || len == 0)
                {
                    return string.Empty;
                }
                return PnText.Utf8(tmp, 0, (int)Math.Min(len, (uint)tmp.Length));
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Shutdown()
        {
            if (!initialized) return;
            reliableQueue.Clear();
            connectedPeers.Clear();
            try
            {
                PnNative.pn_shutdown();
            }
            catch (Exception e)
            {
                EmitLog(1, $"pn_shutdown threw: {e.Message}");
            }
            initialized = false;
            roomCode = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Shutdown();
            disposed = true;
        }

        int TrySendRaw(int peer, int channel, byte[] data) => TrySend(peer, (NetChannel)channel, data);

        int TrySend(int peer, NetChannel channel, byte[] data)
        {
            unsafe
            {
                fixed (byte* p = data)
                {
                    return PnNative.pn_send(peer, (int)channel, p, (uint)data.Length);
                }
            }
        }

        /// <summary>
        /// 可靠/大块消息在托管队列里彻底失败（超时或溢出）：明确断开该目标。
        /// broadcast(peer=-1) 失败时断开全部连接，绝不部分重发（原生 pn_send 广播是原子预留的）。
        /// </summary>
        void OnSendTargetFailed(int peer, string why)
        {
            EmitLog(1, why);
            try
            {
                if (peer < 0)
                {
                    int[] targets = [.. connectedPeers];
                    EmitLog(1, $"broadcast reliable send failed; disconnecting {targets.Length} peer(s)");
                    foreach (int target in targets)
                    {
                        PnNative.pn_disconnect(target, (int)DisconnectReason.Timeout);
                    }
                    connectedPeers.Clear();
                }
                else
                {
                    EmitLog(1, $"disconnecting peer {peer} because reliable send failed");
                    PnNative.pn_disconnect(peer, (int)DisconnectReason.Timeout);
                    connectedPeers.Remove(peer);
                }
            }
            catch (Exception e)
            {
                EmitLog(1, $"disconnect after send failure threw: {e.Message}");
            }
        }

        void EmitLog(int level, string text)
        {
            lastErrorText = text;
            try
            {
                logSink?.Invoke(level, text);
            }
            catch
            {
                // 日志失败不能影响传输层。
            }
            // 逐个订阅者隔离；日志回调里再抛异常也只记录，绝不再递归 EmitLog。
            SafeEvents.Raise(Log, level, text, e => lastErrorText = $"Log callback threw: {e.Message}");
        }

        void ReportCallbackError(string name, Exception e)
        {
            EmitLog(1, $"{name} callback threw: {e.Message}");
        }

        static int SafeCall(Func<int> call, int fallback)
        {
            try
            {
                return call();
            }
            catch
            {
                return fallback;
            }
        }

        public static string Describe(int code) => code switch
        {
            PnAbi.Ok => "OK",
            PnAbi.ErrInvalid => "INVALID",
            PnAbi.ErrState => "STATE",
            PnAbi.ErrNotFound => "NOT_FOUND",
            PnAbi.ErrBuffer => "BUFFER",
            PnAbi.ErrQueueFull => "QUEUE_FULL",
            PnAbi.ErrInternal => "INTERNAL",
            _ => "UNKNOWN"
        };
    }
}
