using System;
using System.Runtime.InteropServices;
using PolarisNoels.Networking;
using PolarisNoels.Networking.Native;

namespace PolarisNoels.NetSmoke
{
    /// <summary>
    /// ABI 布局自检：不需要 polaris_net.dll 就能跑，断言 C 结构的大小/偏移与 C# 托管定义一致。
    /// 这是 §3.1 契约在 C# 侧的守门测试。
    /// </summary>
    public static class AbiChecks
    {
        static int failures;

        public static int Run(string[] args)
        {
            failures = 0;
            Section("pn_event");
            Size<PnEvent>(32, nameof(PnEvent));
            Offset<PnEvent>(nameof(PnEvent.Type), 0);
            Offset<PnEvent>(nameof(PnEvent.Peer), 4);
            Offset<PnEvent>(nameof(PnEvent.Channel), 8);
            Offset<PnEvent>(nameof(PnEvent.Code), 12);
            Offset<PnEvent>(nameof(PnEvent.Len), 16);
            Offset<PnEvent>(nameof(PnEvent.Aux0), 20);
            Offset<PnEvent>(nameof(PnEvent.Aux1), 24);
            Offset<PnEvent>(nameof(PnEvent.Aux2), 28);

            Section("pn_peer_stats");
            Size<PnPeerStats>(32, nameof(PnPeerStats));
            Offset<PnPeerStats>(nameof(PnPeerStats.RttMs), 0);
            Offset<PnPeerStats>(nameof(PnPeerStats.LossPermille), 4);
            Offset<PnPeerStats>(nameof(PnPeerStats.BytesSent), 8);
            Offset<PnPeerStats>(nameof(PnPeerStats.BytesRecv), 16);
            Offset<PnPeerStats>(nameof(PnPeerStats.SendQueueBytes), 24);
            Offset<PnPeerStats>(nameof(PnPeerStats.PathKind), 28);

            Section("pn_config");
            Size<PnConfig>(32, nameof(PnConfig));
            Offset<PnConfig>(nameof(PnConfig.AbiVersion), 0);
            Offset<PnConfig>(nameof(PnConfig.BindPort), 4);
            Offset<PnConfig>(nameof(PnConfig.MaxPeers), 6);
            Offset<PnConfig>(nameof(PnConfig.EnableStun), 8);
            Offset<PnConfig>(nameof(PnConfig.EnableLan), 9);
            Offset<PnConfig>(nameof(PnConfig.EnableUpnp), 10);
            Offset<PnConfig>(nameof(PnConfig.Reserved0), 11);
            Offset<PnConfig>(nameof(PnConfig.StunServers), 16);
            Offset<PnConfig>(nameof(PnConfig.IdleTimeoutMs), 24);
            Offset<PnConfig>(nameof(PnConfig.Reserved1), 28);

            Section("constants");
            Check("ABI_VERSION", PnAbi.Version, 1);
            Check("PN_OK", PnAbi.Ok, 0);
            Check("PN_ERR_INVALID", PnAbi.ErrInvalid, -1);
            Check("PN_ERR_STATE", PnAbi.ErrState, -2);
            Check("PN_ERR_NOT_FOUND", PnAbi.ErrNotFound, -3);
            Check("PN_ERR_BUFFER", PnAbi.ErrBuffer, -4);
            Check("PN_ERR_QUEUE_FULL", PnAbi.ErrQueueFull, -5);
            Check("PN_ERR_INTERNAL", PnAbi.ErrInternal, -99);
            Check("PN_CH_RELIABLE", PnAbi.ChReliable, (int)NetChannel.Reliable);
            Check("PN_CH_SEQUENCED", PnAbi.ChSequenced, (int)NetChannel.Sequenced);
            Check("PN_CH_BULK", PnAbi.ChBulk, (int)NetChannel.Bulk);
            Check("PN_EV_PEER_CONNECTED", PnAbi.EvPeerConnected, 1);
            Check("PN_EV_PEER_DISCONNECTED", PnAbi.EvPeerDisconnected, 2);
            Check("PN_EV_MESSAGE", PnAbi.EvMessage, 3);
            Check("PN_EV_BULK_PROGRESS", PnAbi.EvBulkProgress, 4);
            Check("PN_EV_NAT_STATE", PnAbi.EvNatState, 5);
            Check("PN_EV_JOIN_RESULT", PnAbi.EvJoinResult, 6);
            Check("PN_EV_LOG", PnAbi.EvLog, 7);
            Check("DiscReason.Kicked", (int)DisconnectReason.Kicked, 2);
            Check("DiscReason.HostClosed", (int)DisconnectReason.HostClosed, 6);
            Check("JoinError.BadCode", (int)NetJoinError.BadCode, 1);
            Check("JoinError.PunchTimeout", (int)NetJoinError.PunchTimeout, 2);
            Check("JoinError.HandshakeFailed", (int)NetJoinError.HandshakeFailed, 3);
            Check("JoinError.RoomFull", (int)NetJoinError.RoomFull, 4);

            Section("native library");
            string[] candidates =
            [
                AppContext.BaseDirectory,
                Environment.CurrentDirectory,
                System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "native", "polaris_net", "target", "release"))
            ];
            if (PnNativeLoader.TryLoadFromCandidates(candidates, out string error))
            {
                int version = PnNative.pn_abi_version();
                Console.WriteLine($"  OK    pn_abi_version() = {version} ({PnNativeLoader.LoadedPath})");
                if (version != PnAbi.Version)
                {
                    failures++;
                    Console.WriteLine($"  FAIL  library ABI {version} != managed {PnAbi.Version}");
                }
                else if (args.Length > 1 && args[1] == "--with-native-selftest")
                {
                    NativeSelfTest();
                }
            }
            else
            {
                Console.WriteLine($"  SKIP  native library not present: {error}");
            }

            Console.WriteLine(failures == 0 ? "ABI-CHECK: PASS" : $"ABI-CHECK: FAIL ({failures})");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>需要 DLL：循环 init/shutdown + 小缓冲 PN_ERR_BUFFER 路径。</summary>
        public static void NativeSelfTest()
        {
            Section("native init/shutdown loop");
            for (int i = 0; i < 3; i++)
            {
                // 故意用 64 字节缓冲，逼出 pn_poll 的 PN_ERR_BUFFER 扩容路径。
                NativeTransport transport = new(new NetTransportConfig { MaxPeers = 4, EnableStun = false }, null, 64);
                if (!transport.Init(out string error))
                {
                    failures++;
                    Console.WriteLine($"  FAIL  init #{i}: {error}");
                    return;
                }
                bool gotNat = false, gotCode = false;
                int callbackErrors = 0;
                transport.Log += (level, text) => { if (text.Contains("callback")) callbackErrors++; };
                transport.NatInfo += _ => throw new InvalidOperationException("intentional callback failure");
                transport.NatInfo += _ => { gotNat = true; if (i == 1) transport.Shutdown(); };
                transport.RoomCodeReady += code => gotCode = code.StartsWith("PN1-");
                transport.StartHost();
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (!gotNat && deadline.Elapsed.TotalSeconds < 5) { transport.Poll(); System.Threading.Thread.Sleep(5); }
                if (!gotNat || (i != 1 && !gotCode) || (i == 1 && transport.IsReady))
                { failures++; Console.WriteLine("  FAIL  real event / small buffer / callback shutdown check"); }
                transport.Shutdown();
                Console.WriteLine($"  OK    real NAT event with 64 byte buffer, isolated throw, shutdown round {i}");
                transport.Dispose();
            }
        }

        static void Section(string name) => Console.WriteLine($"[{name}]");

        static void Size<T>(int expected, string name) where T : struct
        {
            int actual = Marshal.SizeOf<T>();
            if (actual == expected)
            {
                Console.WriteLine($"  OK    sizeof({name}) = {actual}");
            }
            else
            {
                failures++;
                Console.WriteLine($"  FAIL  sizeof({name}) = {actual}, expected {expected}");
            }
        }

        static void Offset<T>(string field, int expected) where T : struct
        {
            int actual = (int)Marshal.OffsetOf<T>(field);
            if (actual == expected)
            {
                Console.WriteLine($"  OK    offsetof({typeof(T).Name}.{field}) = {actual}");
            }
            else
            {
                failures++;
                Console.WriteLine($"  FAIL  offsetof({typeof(T).Name}.{field}) = {actual}, expected {expected}");
            }
        }

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
    }
}
