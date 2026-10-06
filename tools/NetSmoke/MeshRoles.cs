using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PolarisNoels.Networking;

namespace PolarisNoels.NetSmoke
{
    /// <summary>
    /// 三进程冒烟：host + 2 成员，房间码入房、mesh 直连、三通道双向、5MB BULK、踢出、主机关闭。
    /// 原生库是进程级单例，所以每个角色各起一个进程，由 run-smoke.ps1 编排。
    /// 断言覆盖：每条 ack 的 ok 属性、Sequenced 单调递增、双向 BULK 完整性。
    /// </summary>
    public static class MeshRoles
    {
        const int ReliableLen = 4096;
        const int BulkLen = 5 * 1024 * 1024;
        const int ClientBulkLen = 1024 * 1024;
        const int SequencedCount = 20;

        public static int RunHost(string[] args)
        {
            string codeFile = args.Length > 1 ? args[1] : "roomcode.txt";
            int clientCount = args.Length > 2 ? int.Parse(args[2]) : 2;
            using NativeTransport transport = new(new NetTransportConfig
            {
                MaxPeers = clientCount + 1,
                EnableStun = false,
                EnableLan = true
            }, Log);

            HashSet<int> peers = [];
            HashSet<string> hellos = [];
            HashSet<int> reliableAck = [];
            HashSet<int> bulkAck = [];
            HashSet<int> seqAck = [];
            HashSet<int> clientBulkOk = [];
            HashSet<int> protocolErrors = [];
            Dictionary<int, int> hostSeqLast = [];
            bool meshOk = false;
            int hostSeqRx = 0;
            int phase = 0;
            int kickedPeer = -1;

            transport.PeerConnected += peer =>
            {
                lock (peers) peers.Add(peer);
                Console.WriteLine($"host: peer {peer} connected");
            };
            transport.PeerDisconnected += (peer, reason) =>
            {
                lock (peers) peers.Remove(peer);
                Console.WriteLine($"host: peer {peer} disconnected ({reason})");
            };
            transport.Message += (peer, channel, data) =>
            {
                lock (peers)
                {
                    if (channel == NetChannel.Sequenced)
                    {
                        int n = Wire.GetInt(data, "n");
                        if (n < 0 || (hostSeqLast.TryGetValue(peer, out int last) && n <= last))
                        {
                            protocolErrors.Add(peer);
                            Console.Error.WriteLine($"host: non-monotonic sequenced from {peer}: n={n} last={(hostSeqLast.TryGetValue(peer, out int l) ? l : -1)}");
                            return;
                        }
                        hostSeqLast[peer] = n;
                        hostSeqRx++;
                        return;
                    }
                    if (channel == NetChannel.Bulk)
                    {
                        bool ok = Wire.VerifyPattern(data, ClientBulkLen);
                        if (ok) clientBulkOk.Add(peer); else protocolErrors.Add(peer);
                        Console.WriteLine($"host: client bulk from {peer} len={data.Length} ok={ok}");
                        transport.Send(peer, NetChannel.Reliable, Wire.Json($"{{\"t\":\"peer_bulk_ok\",\"ok\":{(ok ? "true" : "false")}}}"));
                        return;
                    }
                    string type = Wire.Type(data);
                    switch (type)
                    {
                        case "hello":
                            hellos.Add(Wire.GetString(data, "name"));
                            Console.WriteLine($"host: hello from {peer} ({Wire.GetString(data, "name")})");
                            break;
                        case "reliable_ack":
                            if (Wire.GetBool(data, "ok")) reliableAck.Add(peer); else protocolErrors.Add(peer);
                            break;
                        case "bulk_ok":
                            if (Wire.GetBool(data, "ok")) bulkAck.Add(peer); else protocolErrors.Add(peer);
                            break;
                        case "seq_ok":
                            if (Wire.GetBool(data, "ok")) seqAck.Add(peer); else protocolErrors.Add(peer);
                            break;
                        case "mesh_ok":
                            meshOk = true;
                            Console.WriteLine("host: mesh check passed");
                            break;
                        default:
                            protocolErrors.Add(peer);
                            Console.Error.WriteLine($"host: unexpected reliable message from {peer}: t={type}");
                            break;
                    }
                }
            };

            if (!transport.Init(out string error))
            {
                Console.Error.WriteLine($"host: init failed: {error}");
                return 1;
            }
            transport.StartHost();
            if (!WaitFor(() => transport.RoomCode, 20000, transport.Poll, out string roomCode))
            {
                Console.Error.WriteLine("host: room code never became available");
                return 1;
            }
            File.WriteAllText(codeFile, roomCode);
            Console.WriteLine($"host: room code ready ({roomCode.Length} chars) -> {codeFile}");

            int deadline = Environment.TickCount + 120000;
            while (!TimedOut(deadline))
            {
                transport.Poll();
                lock (peers)
                {
                    if (phase == 0 && hellos.Count >= clientCount)
                    {
                        List<int> ids = [.. peers];
                        ids.Sort();
                        foreach (int peer in ids)
                        {
                            transport.Send(peer, NetChannel.Reliable, Wire.Json($"{{\"t\":\"welcome\",\"id\":{peer},\"peers\":[{string.Join(",", ids)}]}}"));
                            transport.Send(peer, NetChannel.Reliable, Wire.Json($"{{\"t\":\"reliable\",\"len\":{ReliableLen}}}"));
                            transport.Send(peer, NetChannel.Reliable, Wire.Pattern(ReliableLen));
                            transport.Send(peer, NetChannel.Bulk, Wire.Pattern(BulkLen));
                            for (int i = 0; i < SequencedCount; i++)
                            {
                                transport.Send(peer, NetChannel.Sequenced, Wire.Json($"{{\"t\":\"seq\",\"n\":{i}}}"));
                            }
                        }
                        // 让 client1 与 client2 直接互发，才算验证了 mesh 而不是星型。
                        if (ids.Count == 2)
                        {
                            transport.Send(ids[0], NetChannel.Reliable, Wire.Json($"{{\"t\":\"mesh\",\"peer\":{ids[1]}}}"));
                        }
                        phase = 1;
                    }
                    if (phase == 1
                        && protocolErrors.Count == 0
                        && reliableAck.Count >= clientCount
                        && bulkAck.Count >= clientCount
                        && seqAck.Count >= clientCount
                        && clientBulkOk.Count >= clientCount
                        && hostSeqRx >= SequencedCount * clientCount
                        && (meshOk || clientCount < 2))
                    {
                        phase = 2;
                    }
                    if (phase == 2)
                    {
                        List<int> ids = [.. peers];
                        ids.Sort();
                        kickedPeer = ids[^1];
                        Console.WriteLine($"host: kicking peer {kickedPeer}");
                        transport.Disconnect(kickedPeer, DisconnectReason.Kicked);
                        phase = 3;
                    }
                    if (phase == 3 && !peers.Contains(kickedPeer))
                    {
                        phase = 4;
                    }
                }
                if (phase == 4)
                {
                    Console.WriteLine("host: closing room (host close)");
                    transport.Shutdown();
                    Console.WriteLine("host: RESULT OK");
                    return 0;
                }
                Thread.Sleep(5);
            }
            Console.Error.WriteLine($"host: TIMEOUT at phase {phase} peers=[{string.Join(",", peers)}] reliableAck={reliableAck.Count} bulkAck={bulkAck.Count} seqAck={seqAck.Count} clientBulkOk={clientBulkOk.Count} hostSeqRx={hostSeqRx} meshOk={meshOk} hellos={hellos.Count} protocolErrors={protocolErrors.Count}");
            return 1;
        }

        public static int RunClient(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("client: usage: client <roomCode> <name> [kick|hostclose]");
                return 2;
            }
            string roomCode = args[1];
            string name = args[2];
            string role = args.Length > 3 ? args[3] : "hostclose";

            using NativeTransport transport = new(new NetTransportConfig
            {
                MaxPeers = 4,
                EnableStun = false,
                EnableLan = true
            }, Log);

            bool joined = false;
            bool hostConnected = false;
            bool reliableOk = false;
            bool bulkOk = false;
            bool bulkAcked = false;
            bool seqOk = false;
            bool helloSent = false;
            bool finished = false;
            bool protocolError = false;
            int exitCode = 1;
            int myId = -1;
            int pendingReliableLen = -1;
            int seqRx = 0;
            int lastSeq = -1;

            transport.JoinResult += code =>
            {
                Console.WriteLine($"client {name}: join result {code} ({(NetJoinError)code})");
                if (code == 0)
                {
                    joined = true;
                }
                else
                {
                    finished = true;
                }
            };
            transport.PeerConnected += peer =>
            {
                Console.WriteLine($"client {name}: peer {peer} connected");
                if (peer == 0) hostConnected = true;
            };
            transport.PeerDisconnected += (peer, reason) =>
            {
                Console.WriteLine($"client {name}: peer {peer} disconnected ({reason})");
                if (peer != 0) return;
                bool expected = role switch
                {
                    "kick" => reason == DisconnectReason.Kicked,
                    "hostclose" => reason is DisconnectReason.HostClosed or DisconnectReason.Remote,
                    _ => reason is DisconnectReason.Kicked or DisconnectReason.HostClosed or DisconnectReason.Remote
                };
                if (expected && reliableOk && bulkOk && bulkAcked && seqOk && !protocolError)
                {
                    exitCode = 0;
                }
                else
                {
                    Console.Error.WriteLine($"client {name}: unexpected host disconnect {reason} (reliableOk={reliableOk} bulkOk={bulkOk} bulkAcked={bulkAcked} seqOk={seqOk} protocolError={protocolError})");
                }
                finished = true;
            };
            transport.Message += (peer, channel, data) =>
            {
                if (channel == NetChannel.Sequenced)
                {
                    int n = Wire.GetInt(data, "n");
                    if (n < 0 || (lastSeq >= 0 && n <= lastSeq))
                    {
                        protocolError = true;
                        Console.Error.WriteLine($"client {name}: non-monotonic sequenced n={n} last={lastSeq}");
                        return;
                    }
                    lastSeq = n;
                    Interlocked.Increment(ref seqRx);
                    return;
                }
                if (channel == NetChannel.Bulk)
                {
                    bulkOk = Wire.VerifyPattern(data, BulkLen);
                    Console.WriteLine($"client {name}: bulk received len={data.Length} ok={bulkOk}");
                    transport.Send(0, NetChannel.Reliable, Wire.Json($"{{\"t\":\"bulk_ok\",\"ok\":{(bulkOk ? "true" : "false")}}}"));
                    return;
                }
                string type = Wire.Type(data);
                switch (type)
                {
                    case "welcome":
                        myId = Wire.GetInt(data, "id");
                        Console.WriteLine($"client {name}: welcome id={myId}");
                        // 反向 BULK：证明大块通道双向都能用。
                        transport.Send(0, NetChannel.Bulk, Wire.Pattern(ClientBulkLen));
                        break;
                    case "reliable":
                        pendingReliableLen = Wire.GetInt(data, "len");
                        break;
                    case "peer_bulk_ok":
                        bulkAcked = Wire.GetBool(data, "ok");
                        Console.WriteLine($"client {name}: host verified client bulk ok={bulkAcked}");
                        break;
                    case "mesh":
                        int meshPeer = Wire.GetInt(data, "peer");
                        Console.WriteLine($"client {name}: mesh test -> peer {meshPeer}");
                        transport.Send(meshPeer, NetChannel.Reliable, Wire.Json($"{{\"t\":\"mesh_ping\",\"from\":{myId}}}"));
                        break;
                    case "mesh_ping":
                        int from = Wire.GetInt(data, "from");
                        transport.Send(from, NetChannel.Reliable, Wire.Json($"{{\"t\":\"mesh_pong\",\"to\":{from}}}"));
                        break;
                    case "mesh_pong":
                        Console.WriteLine($"client {name}: mesh pong received");
                        transport.Send(0, NetChannel.Reliable, Wire.Json("{\"t\":\"mesh_ok\"}"));
                        break;
                    case null:
                        if (pendingReliableLen >= 0)
                        {
                            reliableOk = Wire.VerifyPattern(data, pendingReliableLen);
                            Console.WriteLine($"client {name}: reliable payload len={data.Length} ok={reliableOk}");
                            pendingReliableLen = -1;
                            transport.Send(0, NetChannel.Reliable, Wire.Json($"{{\"t\":\"reliable_ack\",\"ok\":{(reliableOk ? "true" : "false")}}}"));
                        }
                        break;
                    default:
                        protocolError = true;
                        Console.Error.WriteLine($"client {name}: unexpected reliable message t={type}");
                        break;
                }
            };

            if (!transport.Init(out string error))
            {
                Console.Error.WriteLine($"client {name}: init failed: {error}");
                return 1;
            }
            transport.Join(roomCode);

            int deadline = Environment.TickCount + 120000;
            while (!finished && !TimedOut(deadline))
            {
                transport.Poll();
                if (joined && hostConnected && !helloSent)
                {
                    helloSent = true;
                    transport.Send(0, NetChannel.Reliable, Wire.Json($"{{\"t\":\"hello\",\"name\":\"{name}\"}}"));
                    Console.WriteLine($"client {name}: hello sent");
                }
                if (helloSent && !seqOk && seqRx >= SequencedCount && lastSeq == SequencedCount - 1)
                {
                    seqOk = true;
                    transport.Send(0, NetChannel.Reliable, Wire.Json($"{{\"t\":\"seq_ok\",\"ok\":true}}"));
                    for (int i = 0; i < SequencedCount; i++)
                    {
                        transport.Send(0, NetChannel.Sequenced, Wire.Json($"{{\"t\":\"cli_seq\",\"n\":{i}}}"));
                    }
                }
                Thread.Sleep(5);
            }
            if (!finished)
            {
                Console.Error.WriteLine($"client {name}: TIMEOUT joined={joined} host={hostConnected} id={myId} reliable={reliableOk} bulk={bulkOk} bulkAcked={bulkAcked} seq={seqRx}/{lastSeq}");
                return 1;
            }
            Console.WriteLine($"client {name}: RESULT {(exitCode == 0 ? "OK" : "FAIL")}");
            return exitCode;
        }

        static bool WaitFor(Func<string> probe, int timeoutMs, Action tick, out string value)
        {
            int deadline = Environment.TickCount + timeoutMs;
            while (!TimedOut(deadline))
            {
                tick();
                value = probe();
                if (!string.IsNullOrEmpty(value))
                {
                    return true;
                }
                Thread.Sleep(10);
            }
            value = null;
            return false;
        }

        static bool TimedOut(int deadline) => unchecked(Environment.TickCount - deadline) >= 0;

        static void Log(int level, string text) => Console.WriteLine($"[native {level}] {text}");
    }
}
