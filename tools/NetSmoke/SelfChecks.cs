using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using PolarisNoels.Networking;
using PolarisNoels.Networking.Native;

namespace PolarisNoels.NetSmoke
{
    /// <summary>
    /// 不需要原生 DLL 的托管自检：
    ///   * ReliableSendQueue：QueueFull 后按 (peer,channel) FIFO、广播失败不部分重发、字节上限明确失败、数据拷贝。
    ///   * SafeEvents：同一事件里一个订阅者抛异常不影响其它订阅者，也不影响后续事件。
    /// 任何断言失败都会让进程返回非零。
    /// </summary>
    public static class SelfChecks
    {
        static int failures;

        public static int Run(string[] args)
        {
            failures = 0;
            Section("reliable queue fifo after queue-full");
            FifoAfterQueueFull();
            Section("reliable queue blocked per target/channel");
            BlockedPerTarget();
            Section("broadcast failure is atomic (no partial resend)");
            BroadcastAtomic();
            Section("broadcast/unicast share ordered peer channels");
            BroadcastOrder();
            Section("reliable queue byte cap fails explicitly");
            ByteCap();
            Section("reliable queue keeps a data copy");
            DataCopy();
            Section("reliable/bulk channels are independent");
            ChannelIndependence();
            Section("event dispatch isolates throwing subscribers");
            DispatchIsolation();
            Section("poll buffer growth clamps to cap");
            BufferGrowth();

            Console.WriteLine(failures == 0 ? "SELF-CHECK: PASS" : $"SELF-CHECK: FAIL ({failures})");
            return failures == 0 ? 0 : 1;
        }

        static void FifoAfterQueueFull()
        {
            List<string> sent = [];
            int calls = 0;
            List<int> failed = [];
            ReliableSendQueue queue = new(
                (peer, channel, data) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        return PnAbi.ErrQueueFull;
                    }
                    sent.Add(Text(data));
                    return PnAbi.Ok;
                },
                (peer, why) => failed.Add(peer));

            queue.Send(5, NetChannel.Reliable, Bytes("A"));
            queue.Send(5, NetChannel.Reliable, Bytes("B"));
            queue.Send(5, NetChannel.Reliable, Bytes("C"));
            Check("three messages queued", queue.Count, 3);
            queue.Retry();
            Check("queue drained", queue.Count, 0);
            Check("send order A,B,C", string.Join(",", sent), "A,B,C");
            Check("no target failed", failed.Count, 0);
        }

        static void BlockedPerTarget()
        {
            List<string> attempts = [];
            ReliableSendQueue queue = new(
                (peer, channel, data) =>
                {
                    attempts.Add($"{peer}:{Text(data)}");
                    return peer == 5 ? PnAbi.ErrQueueFull : PnAbi.Ok;
                },
                null);

            queue.Send(5, NetChannel.Reliable, Bytes("A"));
            queue.Send(5, NetChannel.Reliable, Bytes("B"));
            queue.Send(6, NetChannel.Reliable, Bytes("X"));
            attempts.Clear();

            queue.Retry();
            // peer5 第一条就 QueueFull：B 不能越过 A，peer6 独立进展。
            Check("blocked peer stops at head", string.Join(",", attempts), "5:A");
            Check("both peer5 messages retained", queue.Count, 2);
        }

        static void BroadcastAtomic()
        {
            long now = 0;
            int broadcastAttempts = 0;
            List<int> failed = [];
            ReliableSendQueue queue = new(
                (peer, channel, data) =>
                {
                    if (peer < 0) broadcastAttempts++;
                    return PnAbi.ErrQueueFull;
                },
                (peer, why) => failed.Add(peer),
                null,
                ReliableSendQueue.DefaultMaxBytes,
                100,
                1, () => now);

            queue.Send(-1, NetChannel.Reliable, Bytes("BCAST"));
            queue.Send(-1, NetChannel.Reliable, Bytes("BCAST2"));
            queue.Retry();
            Check("broadcast attempted once per retry pass", broadcastAttempts, 2);
            Check("broadcast not failed before timeout", failed.Count, 0);

            now = 10;
            queue.Retry();
            Check("broadcast failed exactly once", string.Join(",", failed), "-1");
            Check("no partial resend after failure", broadcastAttempts, 2);
            Check("broadcast entries cleared", queue.Count, 0);
        }
        static void BroadcastOrder()
        {
            bool full = true;
            List<string> sent = [];
            ReliableSendQueue queue = new((p,c,d) => { if (full) return PnAbi.ErrQueueFull; sent.Add($"{p}:{Text(d)}"); return 0; }, null);
            queue.Send(5, NetChannel.Reliable, Bytes("A"));
            queue.Send(-1, NetChannel.Reliable, Bytes("B"));
            queue.Send(6, NetChannel.Reliable, Bytes("C"));
            full = false; queue.Retry();
            Check("unicast before broadcast before unicast", string.Join(",", sent), "5:A,-1:B,6:C");
        }

        static void ByteCap()
        {
            List<int> failed = [];
            ReliableSendQueue queue = new(
                (peer, channel, data) => PnAbi.ErrQueueFull,
                (peer, why) => failed.Add(peer),
                null,
                maxBytes: 1024,
                maxEntries: 100);

            bool accepted = queue.Send(7, NetChannel.Reliable, new byte[2048]);
            Check("oversized message rejected", accepted, false);
            Check("oversized target explicitly failed", string.Join(",", failed), "7");
            Check("no silent retention", queue.Count, 0);
        }

        static void DataCopy()
        {
            List<byte[]> sent = [];
            int calls = 0;
            ReliableSendQueue queue = new(
                (peer, channel, data) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        return PnAbi.ErrQueueFull;
                    }
                    sent.Add(data);
                    return PnAbi.Ok;
                },
                null);

            byte[] payload = [1, 2, 3];
            queue.Send(5, NetChannel.Reliable, payload);
            payload[0] = 99;
            queue.Retry();
            Check("queued payload is a copy", sent.Count == 1 ? sent[0][0] : -1, 1);
        }

        static void ChannelIndependence()
        {
            List<string> attempts = [];
            ReliableSendQueue queue = new(
                (peer, channel, data) =>
                {
                    attempts.Add($"{channel}:{Text(data)}");
                    return channel == (int)NetChannel.Reliable ? PnAbi.ErrQueueFull : PnAbi.Ok;
                },
                null);

            queue.Send(5, NetChannel.Reliable, Bytes("R"));
            queue.Send(5, NetChannel.Bulk, Bytes("K"));
            Check("bulk unaffected by blocked reliable", string.Join(",", attempts), "0:R,2:K");
            Check("only reliable retained", queue.Count, 1);
        }

        static void DispatchIsolation()
        {
            int good = 0;
            int errors = 0;
            Action<int> handlers = null;
            handlers += _ => throw new InvalidOperationException("boom");
            handlers += _ => good++;
            handlers += _ => good++;

            SafeEvents.Raise(handlers, 1, e => errors++);
            SafeEvents.Raise(handlers, 2, e => errors++);

            Check("good subscribers got both messages", good, 4);
            Check("throwing subscriber reported each time", errors, 2);
        }

        static void BufferGrowth()
        {
            const long cap = 64L * 1024 * 1024;
            Check("clamp when doubling exceeds cap", NativeTransport.TryComputeBufferGrowth(32L * 1024 * 1024, 48u * 1024 * 1024, cap, out long a) ? (int)a : -1, (int)cap);
            Check("exact cap accepted", NativeTransport.TryComputeBufferGrowth(cap, (uint)cap, cap, out long b) ? (int)b : -1, (int)cap);
            Check("over cap rejected", NativeTransport.TryComputeBufferGrowth(cap, (uint)cap + 1u, cap, out _), false);
            Check("normal doubling", NativeTransport.TryComputeBufferGrowth(1024, 2048, cap, out long c) ? (int)c : -1, 2048);
        }

        static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        static string Text(byte[] data) => Encoding.UTF8.GetString(data);

        static void Section(string name) => Console.WriteLine($"[{name}]");

        static void Check(string name, int actual, int expected)
        {
            if (actual == expected)
            {
                Console.WriteLine($"  OK    {name} = {actual}");
            }
            else
            {
                failures++;
                Console.WriteLine($"  FAIL  {name} = {actual}, expected {expected}");
            }
        }

        static void Check(string name, string actual, string expected)
        {
            if (actual == expected)
            {
                Console.WriteLine($"  OK    {name} = {actual}");
            }
            else
            {
                failures++;
                Console.WriteLine($"  FAIL  {name} = '{actual}', expected '{expected}'");
            }
        }

        static void Check(string name, bool actual, bool expected)
        {
            if (actual == expected)
            {
                Console.WriteLine($"  OK    {name} = {actual}");
            }
            else
            {
                failures++;
                Console.WriteLine($"  FAIL  {name} = {actual}, expected {expected}");
            }
        }
    }
}
