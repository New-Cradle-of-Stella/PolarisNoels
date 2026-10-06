using System;
using System.Collections.Generic;
using System.Diagnostics;
using PolarisNoels.Networking.Native;

namespace PolarisNoels.Networking
{
    /// <summary>
    /// 可靠/大块发送的托管重试队列（方案 §4.6 / §8.2）。职责：
    ///   * 每个 (peer, channel) 严格 FIFO：同一目标同一通道的旧消息没发出去之前，新消息不能越过它。
    ///   * Reliable 与 Bulk 互不影响（各自一条 FIFO）。
    ///   * 收到 PN_ERR_QUEUE_FULL 时停止继续尝试同一目标/通道，把机会让给其它目标，下一轮再试。
    ///   * 有界（条目数 + 总字节数）；溢出/超时不静默丢弃，而是通过 onTargetFailed 明确断开该目标
    ///     （broadcast 目标 = 断开全部连接，绝不部分重发）。
    ///   * 数据入队时拷贝一份，调用方之后复用/释放原数组不受影响。
    /// 不依赖 Unity/游戏程序集，测试可注入 send 桩。
    /// </summary>
    public sealed class ReliableSendQueue
    {
        public const long DefaultMaxBytes = 32L * 1024 * 1024;
        public const int DefaultMaxEntries = 8192;

        readonly Func<int, int, byte[], int> send;
        readonly Action<int, string> onTargetFailed;
        readonly Action<int, string> log;
        readonly long maxBytes;
        readonly int maxEntries;
        readonly int retryTimeoutMs;
        readonly Func<long> clock;

        readonly List<Entry> entries = new();
        readonly Dictionary<(int Peer, int Channel), int> counts = new();
        readonly HashSet<(int Peer, int Channel)> blocked = new();

        long bytes;

        public ReliableSendQueue(
            Func<int, int, byte[], int> send,
            Action<int, string> onTargetFailed,
            Action<int, string> log = null,
            long maxBytes = DefaultMaxBytes,
            int maxEntries = DefaultMaxEntries,
            int retryTimeoutMs = 5000,
            Func<long> clock = null)
        {
            this.send = send ?? throw new ArgumentNullException(nameof(send));
            this.onTargetFailed = onTargetFailed;
            this.log = log;
            this.maxBytes = Math.Max(1024, maxBytes);
            this.maxEntries = Math.Max(1, maxEntries);
            this.retryTimeoutMs = Math.Max(1, retryTimeoutMs);
            this.clock = clock ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
        }

        public int Count => entries.Count;

        public long Bytes => bytes;

        /// <summary>该目标/通道是否还有未发出去的消息（用于判定新消息必须排队而不是直发）。</summary>
        public bool HasPending(int peer, NetChannel channel)
        {
            foreach (var key in counts.Keys)
                if (key.Channel == (int)channel && (peer == -1 || key.Peer == -1 || key.Peer == peer)) return true;
            return false;
        }

        /// <summary>
        /// 发送一条可靠/大块消息。返回 true = 已交给原生层或已排队；false = 已按“明确失败”处理。
        /// </summary>
        public bool Send(int peer, NetChannel channel, byte[] data)
        {
            if (data == null)
            {
                return false;
            }
            if (HasPending(peer, channel))
            {
                // 已有积压消息：必须排队，避免越过老消息。
                return Enqueue(peer, channel, data);
            }
            int rc = send(peer, (int)channel, data);
            if (rc == PnAbi.Ok)
            {
                return true;
            }
            if (rc == PnAbi.ErrQueueFull)
            {
                return Enqueue(peer, channel, data);
            }
            FailTarget(peer, $"pn_send failed rc={rc} ({NativeTransport.Describe(rc)})");
            return false;
        }

        /// <summary>每个 Poll 周期调用一次：按 FIFO 重试积压消息，遇到 QueueFull 停止该目标/通道。</summary>
        public void Retry()
        {
            blocked.Clear();
            int i = 0;
            while (i < entries.Count)
            {
                Entry entry = entries[i];
                (int Peer, int Channel) key = (entry.Peer, (int)entry.Channel);
                bool overlaps = false;
                foreach (var previous in blocked)
                    if (previous.Channel == key.Channel && (previous.Peer == -1 || key.Peer == -1 || previous.Peer == key.Peer)) { overlaps = true; break; }
                if (overlaps)
                {
                    blocked.Add(key);
                    i++;
                    continue;
                }
                if (clock() - entry.CreatedAt > retryTimeoutMs)
                {
                    FailTarget(entry.Peer, $"reliable send stalled for {retryTimeoutMs}ms");
                    i = 0;
                    continue;
                }
                int rc = send(entry.Peer, (int)entry.Channel, entry.Data);
                if (rc == PnAbi.Ok)
                {
                    DropAt(i);
                    continue;
                }
                if (rc == PnAbi.ErrQueueFull)
                {
                    // 该目标/通道的后续消息保持顺序，本轮不再尝试；其它目标不受影响。
                    blocked.Add(key);
                    i++;
                    continue;
                }
                FailTarget(entry.Peer, $"deferred pn_send failed rc={rc} ({NativeTransport.Describe(rc)})");
                i = 0;
            }
        }

        public void Clear()
        {
            entries.Clear();
            counts.Clear();
            blocked.Clear();
            bytes = 0;
        }

        bool Enqueue(int peer, NetChannel channel, byte[] data)
        {
            long projected = bytes + data.Length;
            if (entries.Count >= maxEntries || projected > maxBytes)
            {
                // 溢出必须明确失败：断开该目标，绝不静默丢消息。
                FailTarget(peer, $"reliable send queue overflow (entries={entries.Count}/{maxEntries}, bytes={bytes}+{data.Length}/{maxBytes})");
                return false;
            }
            byte[] copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            entries.Add(new Entry(peer, channel, copy, clock()));
            counts[(peer, (int)channel)] = counts.TryGetValue((peer, (int)channel), out int n) ? n + 1 : 1;
            bytes = projected;
            log?.Invoke(2, $"reliable send deferred peer={peer} channel={channel} len={copy.Length} pending={entries.Count} bytes={bytes}");
            return true;
        }

        void DropAt(int index)
        {
            Entry entry = entries[index];
            entries.RemoveAt(index);
            Forget(entry);
        }

        void Forget(Entry entry)
        {
            (int Peer, int Channel) key = (entry.Peer, (int)entry.Channel);
            if (counts.TryGetValue(key, out int n))
            {
                if (n <= 1)
                {
                    counts.Remove(key);
                }
                else
                {
                    counts[key] = n - 1;
                }
            }
            bytes -= entry.Data.Length;
            if (bytes < 0)
            {
                bytes = 0;
            }
        }

        void RemoveAllFor(int peer)
        {
            if (peer < 0)
            {
                entries.Clear();
                counts.Clear();
                bytes = 0;
                return;
            }
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Peer == peer)
                {
                    Forget(entries[i]);
                    entries.RemoveAt(i);
                }
            }
            counts.Remove((peer, (int)NetChannel.Reliable));
            counts.Remove((peer, (int)NetChannel.Bulk));
            if (entries.Count == 0)
            {
                bytes = 0;
            }
        }

        void FailTarget(int peer, string why)
        {
            RemoveAllFor(peer);
            try
            {
                onTargetFailed?.Invoke(peer, why);
            }
            catch (Exception e)
            {
                log?.Invoke(1, $"onTargetFailed threw: {e.Message}");
            }
        }

        readonly struct Entry
        {
            public readonly int Peer;
            public readonly NetChannel Channel;
            public readonly byte[] Data;
            public readonly long CreatedAt;

            public Entry(int peer, NetChannel channel, byte[] data, long createdAt)
            {
                Peer = peer;
                Channel = channel;
                Data = data;
                CreatedAt = createdAt;
            }
        }
    }
}
