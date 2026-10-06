using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PolarisNoels.CSNetworking;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using PolarisNoels.SN;

namespace PolarisNoels.SessionSmoke
{
    /// <summary>
    /// 独立 managed 会话集成测试。
    ///
    /// 关键点：
    ///   * HostSession / ClientSession / NetworkRuntime / HandshakeCodec / BulkCodec / SaveTransfer
    ///     都是 <b>源链接的真实实现</b>，不是等价假逻辑。
    ///   * 事件来自 FakeTransport；NetworkRuntime 是静态单例，因此反射注入其 Transport，
    ///     并把它的私有静态事件处理器挂到 FakeTransport 上（生产里由 EnsureTransport 完成）。
    ///   * DB / Plugin / PolarisNoelsTools / PartyManager / SVD / nel / DataStruct 用 stub，
    ///     不触发任何游戏/Unity/BepInEx 行为。
    /// </summary>
    public static class SessionChecks
    {
        static int assertions;
        static int failures;
        static string saveDir;

        static readonly PropertyInfo TransportProperty = typeof(NetworkRuntime).GetProperty(
            "Transport", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo OnMessageMethod = typeof(NetworkRuntime).GetMethod(
            "OnMessage", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo OnPeerConnectedMethod = typeof(NetworkRuntime).GetMethod(
            "OnPeerConnected", BindingFlags.NonPublic | BindingFlags.Static);
        static readonly MethodInfo OnPeerDisconnectedMethod = typeof(NetworkRuntime).GetMethod(
            "OnPeerDisconnected", BindingFlags.NonPublic | BindingFlags.Static);

        public static int Run(string[] args)
        {
            saveDir = Path.Combine(AppContext.BaseDirectory, "session-smoke-save");
            Directory.CreateDirectory(saveDir);

            Console.WriteLine("SessionSmoke: managed session integration checks (no game, no native DLL)");
            Console.WriteLine("  source-linked: NetworkRuntime / HostSession / ClientSession / HandshakeCodec / BulkCodec / SaveTransfer");
            Console.WriteLine("  stubbed:       DB / Plugin / PolarisNoelsTools / PartyManager / SVD / nel / DataStruct");

            Scenario("client ready requires JoinResult(0) + HostMessage(peer0) + Bulk save, in any order", ClientReadyOrderIndependent);
            Scenario("client ignores HostMessage / save / progress from non-host peers", OtherPeerRejected);
            Scenario("invalid save archive fails diagnosably", BadSaveDiagnosable);
            Scenario("host peer-connected sends real HandshakeCodec config + BulkCodec archive", HostHandshakeAndSave);
            Scenario("deferred dispatch: title/loading queue, FIFO, <=256/frame, disconnect purge, exception isolation", DeferredDispatch);
            Scenario("shutdown unbinds / cleans up and the runtime can restart", ShutdownAndRestart);

            Console.WriteLine();
            Console.WriteLine($"SessionSmoke: assertions={assertions} failures={failures}");
            return failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 用例

        static void ClientReadyOrderIndependent()
        {
            byte[] hostMessage = HostMessageBytes(4, "guest");
            byte[] rawSave = MakeRawSave(4096);
            byte[] saveBulk = PackedSaveBulk(rawSave);
            PolarisNoelsTools.NetworkConfig config = ClientConfig("guest");

            // 三个就绪条件以三种到达顺序，每个事件恰好触发一次。
            for (int order = 0; order < 3; order++)
            {
                ResetRuntime();
                DB.InitConfig = config;
                FakeTransport fake = new FakeTransport();
                Inject(fake);
                Check($"order{order}: StartClient accepted", NetworkRuntime.StartClient(config, "PN1-TEST"), true);
                ClientSession client = NetworkRuntime.Client;
                Check($"order{order}: session created", client != null, true);
                Check($"order{order}: Join called with room code", fake.JoinedRoomCode, "PN1-TEST");
                Check($"order{order}: not Ready before any event", client.Ready, false);

                Action[] steps = order switch
                {
                    0 => new Action[]
                    {
                        () => fake.RaiseJoinResult(0),
                        () => fake.RaiseMessage(0, NetChannel.Reliable, hostMessage),
                        () => fake.RaiseMessage(0, NetChannel.Bulk, saveBulk)
                    },
                    1 => new Action[]
                    {
                        () => fake.RaiseMessage(0, NetChannel.Bulk, saveBulk),
                        () => fake.RaiseMessage(0, NetChannel.Reliable, hostMessage),
                        () => fake.RaiseJoinResult(0)
                    },
                    _ => new Action[]
                    {
                        () => fake.RaiseMessage(0, NetChannel.Reliable, hostMessage),
                        () => fake.RaiseMessage(0, NetChannel.Bulk, saveBulk),
                        () => fake.RaiseJoinResult(0)
                    }
                };
                for (int step = 0; step < steps.Length; step++)
                {
                    steps[step]();
                    Check($"order{order}: Ready only after all three (step {step})", client.Ready, step == steps.Length - 1);
                }

                fake.RaiseBulkProgress(0, 1, 50, 100);
                Check($"order{order}: bulk progress tracked", client.SaveDone, (uint)50);
                Check($"order{order}: bulk total tracked", client.SaveTotal, (uint)100);

                Check($"order{order}: Ready", client.Ready, true);
                Check($"order{order}: JoinAccepted", client.JoinAccepted, true);
                Check($"order{order}: HostHandshakeDone", client.HostHandshakeDone, true);
                Check($"order{order}: SaveReceived", client.SaveReceived, true);
                Check($"order{order}: LastJoinError == 0", client.LastJoinError, 0);
                Check($"order{order}: LocalId from host message", client.LocalId, 4);
                Check($"order{order}: LocalID propagated", PolarisNoelsTools.LocalID, 4);
                Check($"order{order}: SimBattleSyncHost propagated", PolarisNoelsTools.SimBattleSyncHost, 2);
                Check($"order{order}: SimBattleSyncList propagated", PolarisNoelsTools.SimBattleSyncList.Count, 1);
                Check($"order{order}: pending config count", client.PendingConfigs.Count, 2);
                Check($"order{order}: archive byte count", client.ReceivedArchiveBytes, (long)rawSave.Length);
                Check($"order{order}: DB save buffer length", DB.SyncSaveContentBuffer == null ? -1 : DB.SyncSaveContentBuffer.Length, rawSave.Length);
                Check($"order{order}: DB save buffer bytes", BytesEqual(DB.SyncSaveContentBuffer, rawSave), true);
                Check($"order{order}: archive file written", File.Exists(Path.Combine(SVD.Dir, DB.SYNC_FILE_NAME)), true);

                // 客户端必须回一条真实的 HandshakeCodec ClientMessage。
                Check($"order{order}: exactly one client reply", fake.Sent.Count, 1);
                Check($"order{order}: reply channel Reliable", fake.Sent[0].Channel, NetChannel.Reliable);
                Check($"order{order}: reply target host", fake.Sent[0].Peer, 0);
                Check($"order{order}: reply magic", HandshakeCodec.HasMagic(fake.Sent[0].Data), true);
                bool decoded = HandshakeCodec.TryDecode(fake.Sent[0].Data, HandshakeCodec.KindClientMessage, out PolarisNoelsClientMessage reply);
                Check($"order{order}: reply decodes as ClientMessage", decoded, true);
                Check($"order{order}: reply ID", decoded ? reply.ID : -1, 4);
                Check($"order{order}: reply nickname", decoded ? reply.NickName : null, "guest");
                Check($"order{order}: reply party id", decoded && reply.Party != null ? reply.Party.ID : -1, 4);
            }
        }

        static void OtherPeerRejected()
        {
            ResetRuntime();
            PolarisNoelsTools.NetworkConfig config = ClientConfig("guest");
            DB.InitConfig = config;
            FakeTransport fake = new FakeTransport();
            Inject(fake);
            NetworkRuntime.StartClient(config, "PN1-X");
            ClientSession client = NetworkRuntime.Client;

            byte[] hostMessage = HostMessageBytes(5, "guest");
            byte[] saveBulk = PackedSaveBulk(MakeRawSave(1024));

            fake.RaiseJoinResult(0);
            fake.RaiseMessage(3, NetChannel.Reliable, hostMessage);
            Check("peer3: JoinResult accepted", client.JoinAccepted, true);
            Check("peer3: HostMessage ignored (host handshake not done)", client.HostHandshakeDone, false);
            Check("peer3: LocalId untouched", client.LocalId, -1);
            Check("peer3: no client reply sent", fake.Sent.Count, 0);

            fake.RaiseMessage(3, NetChannel.Bulk, saveBulk);
            Check("peer3: Bulk save ignored", client.SaveReceived, false);
            Check("peer3: DB save buffer untouched", DB.SyncSaveContentBuffer == null, true);
            Check("peer3: ReceivedArchiveBytes 0", client.ReceivedArchiveBytes, 0L);

            fake.RaiseBulkProgress(3, 1, 50, 100);
            Check("peer3: BulkProgress ignored", client.SaveDone, (uint)0);
            Check("peer3: still not Ready", client.Ready, false);

            // peer 0 再发一次证明是过滤而非整体损坏。
            fake.RaiseMessage(0, NetChannel.Reliable, hostMessage);
            fake.RaiseMessage(0, NetChannel.Bulk, saveBulk);
            Check("peer0: handshake accepted", client.HostHandshakeDone, true);
            Check("peer0: save accepted", client.SaveReceived, true);
            Check("peer0: Ready", client.Ready, true);
        }

        static void BadSaveDiagnosable()
        {
            (string Name, byte[] Payload)[] cases =
            {
                ("garbage format", new byte[] { 9, 1, 2, 3 }),
                ("empty payload", Array.Empty<byte>())
            };

            foreach ((string name, byte[] payload) in cases)
            {
                ResetRuntime();
                PolarisNoelsTools.NetworkConfig config = ClientConfig("guest");
                DB.InitConfig = config;
                FakeTransport fake = new FakeTransport();
                Inject(fake);
                NetworkRuntime.StartClient(config, "PN1-BAD");
                ClientSession client = NetworkRuntime.Client;

                fake.RaiseJoinResult(0);
                fake.RaiseMessage(0, NetChannel.Reliable, HostMessageBytes(4, "guest"));
                Check($"{name}: handshake applied first", client.HostHandshakeDone, true);

                fake.RaiseMessage(0, NetChannel.Bulk, BulkCodec.Wrap(BulkCodec.KindSaveArchive, payload));

                Check($"{name}: SaveReceived stays false", client.SaveReceived, false);
                Check($"{name}: diagnosable LastJoinError", client.LastJoinError, (int)NetJoinError.HandshakeFailed);
                Check($"{name}: disconnects host with Protocol", fake.Disconnects.Contains((0, DisconnectReason.Protocol)), true);
                Check($"{name}: error logged", Plugin.Logger.Error.Exists(m => m.Contains("invalid save archive")), true);
                Check($"{name}: not Ready", client.Ready, false);
            }
        }

        static void HostHandshakeAndSave()
        {
            ResetRuntime();
            PolarisNoelsTools.NetworkConfig config = new PolarisNoelsTools.NetworkConfig
            {
                Type = NetWorkType.Host,
                nickName = "hosty",
                NoelType = NoelType.Inverse,
                NoelColor = ColorNoelColor.Blue
            };
            DB.InitConfig = config;
            byte[] rawSave = MakeRawSave(200000);
            DB.SyncSaveContentBuffer = rawSave;
            PolarisNoelsTools.SimBattleSyncHost = 2;
            PolarisNoelsTools.SimBattleSyncList = new List<int> { 1, 2 };

            FakeTransport fake = new FakeTransport();
            Inject(fake);
            Check("host: StartHost accepted", NetworkRuntime.StartHost(config), true);
            Check("host: LocalID == 0", PolarisNoelsTools.LocalID, 0);
            Check("host: type Host", PolarisNoelsTools.Type, NetWorkType.Host);
            Check("host: transport.StartHost called", fake.StartHostCalled, true);
            Check("host: no send before peer connects", fake.Sent.Count, 0);

            fake.RaisePeerConnected(7);
            Check("host: two sends on peer-connected", fake.Sent.Count, 2);
            Check("host: first send Reliable", fake.Sent[0].Channel, NetChannel.Reliable);
            Check("host: first send to peer 7", fake.Sent[0].Peer, 7);
            Check("host: handshake envelope magic", HandshakeCodec.HasMagic(fake.Sent[0].Data), true);

            bool decoded = HandshakeCodec.TryDecode(fake.Sent[0].Data, HandshakeCodec.KindHostMessage, out PolarisNoelsHostMessage hostMessage);
            Check("host: handshake decodes as HostMessage", decoded, true);
            Check("host: InitID == assigned peer id", decoded ? hostMessage.InitID : -1, 7);
            Check("host: SyncHost propagated", decoded ? hostMessage.SyncHost : -1, 2);
            Check("host: SyncConnectedList propagated", decoded && hostMessage.SyncConnectedList != null ? hostMessage.SyncConnectedList.Count : -1, 2);
            Check("host: own config present", decoded && hostMessage.PeerConfigs != null && hostMessage.PeerConfigs.Exists(p => p.Key == 0 && p.Value.Nickname == "hosty"), true);
            Check("host: own config noel type", decoded && hostMessage.PeerConfigs != null && hostMessage.PeerConfigs.Exists(p => p.Key == 0 && p.Value.NoelType == NoelType.Inverse), true);
            Check("host: own party present", decoded && hostMessage.PeerParties != null && hostMessage.PeerParties.Exists(p => p.Key == 0 && p.Value.ID == 0), true);

            Check("host: second send Bulk", fake.Sent[1].Channel, NetChannel.Bulk);
            Check("host: second send to peer 7", fake.Sent[1].Peer, 7);
            bool unwrapped = BulkCodec.TryUnwrap(fake.Sent[1].Data, out byte kind, out byte[] payload);
            Check("host: bulk unwraps", unwrapped, true);
            Check("host: bulk kind is save archive", unwrapped ? kind : (byte)0, BulkCodec.KindSaveArchive);
            Check("host: archive round-trips", unwrapped && BytesEqual(SaveTransfer.Unpack(payload), rawSave), true);
            Check("host: archive is compressed", unwrapped && payload.Length < rawSave.Length, true);

            // 主机的另一侧：记录入房者回包（键用发送者 id）。
            PolarisNoelsClientMessage reply = new PolarisNoelsClientMessage
            {
                ID = 7,
                NickName = "bob",
                NoelType = NoelType.Normal,
                NoelColor = ColorNoelColor.Red,
                Party = new PartyManager.Party { ID = 7, Name = "p7" }
            };
            fake.RaiseMessage(7, NetChannel.Reliable, HandshakeCodec.Encode(HandshakeCodec.KindClientMessage, reply));
            Check("host: recorded client nickname", DB.peerConfigs.TryGetValue(7, out ClientConfig recorded) && recorded.Nickname == "bob", true);
            Check("host: recorded client party", DB.partyInfos.TryGetValue(7, out PartyManager.Party party) && party.ID == 7, true);
        }

        static void DeferredDispatch()
        {
            PolarisNoelsTools.NetworkConfig config = ClientConfig("q");
            List<string> dispatched = new List<string>();
            Action<int, NetChannel, byte[]> handler = (peer, channel, data) => dispatched.Add(Text(data));

            // (a) 标题期：gameHandler == null，消息必须排队而不是丢。
            ResetRuntime();
            DB.InitConfig = config;
            DB.MainPR = null;
            FakeTransport fake = new FakeTransport();
            Inject(fake);
            for (int i = 0; i < 5; i++)
            {
                fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("m" + i));
            }
            Check("title: 5 messages queued with null gameHandler", NetworkRuntime.DeferredCount, 5);
            Check("title: nothing dispatched", dispatched.Count, 0);

            // (b) 注册 gameHandler，但仍在标题（MainPR == null）→ 不排空。
            NetworkRuntime.RegisterGameHandler(handler);
            NetworkRuntime.Poll();
            Check("title: still queued after Poll with handler", NetworkRuntime.DeferredCount, 5);
            Check("title: nothing dispatched after Poll", dispatched.Count, 0);
            Check("title: transport still polled", fake.PollCalls > 0, true);

            // (c) 地图加载中：继续排队。
            DB.MainPR = ReadyPlayer(loading: true);
            fake.RaiseMessage(2, NetChannel.Reliable, Ascii("m5"));
            fake.RaiseMessage(2, NetChannel.Reliable, Ascii("m6"));
            NetworkRuntime.Poll();
            Check("loading: backlog grows", NetworkRuntime.DeferredCount, 7);
            Check("loading: nothing dispatched", dispatched.Count, 0);

            // (d) 加载结束但仍有积压：新消息也必须排队，保证 FIFO 不被越过。
            DB.MainPR = ReadyPlayer(loading: false);
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("m7"));
            Check("resume: new message queued behind backlog", NetworkRuntime.DeferredCount, 8);
            Check("resume: no dispatch before flush", dispatched.Count, 0);

            // (e) 恢复分发：FIFO。
            NetworkRuntime.Poll();
            Check("resume: queue drained", NetworkRuntime.DeferredCount, 0);
            Check("resume: FIFO order", string.Join(",", dispatched), "m0,m1,m2,m3,m4,m5,m6,m7");

            // (f) 每帧最多 256 条。
            dispatched.Clear();
            DB.MainPR = ReadyPlayer(loading: true);
            for (int i = 0; i < 300; i++)
            {
                fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("b" + i));
            }
            Check("budget: 300 queued", NetworkRuntime.DeferredCount, 300);
            DB.MainPR = ReadyPlayer(loading: false);
            NetworkRuntime.Poll();
            Check("budget: first frame dispatches exactly 256", dispatched.Count, 256);
            Check("budget: 44 remain", NetworkRuntime.DeferredCount, 44);
            NetworkRuntime.Poll();
            Check("budget: second frame drains", NetworkRuntime.DeferredCount, 0);
            Check("budget: all 300 dispatched", dispatched.Count, 300);
            Check("budget: order preserved", dispatched[0] == "b0" && dispatched[299] == "b299", true);

            // (g) 断开清除该 peer 的积压，其它 peer 不受影响。
            dispatched.Clear();
            fake.RaisePeerConnected(1);
            fake.RaisePeerConnected(2);
            Check("disconnect: both peers connected", NetworkRuntime.ConnectedPeers.Count, 2);
            DB.MainPR = ReadyPlayer(loading: true);
            for (int i = 0; i < 5; i++)
            {
                fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("p1-" + i));
            }
            for (int i = 0; i < 3; i++)
            {
                fake.RaiseMessage(2, NetChannel.Sequenced, Ascii("p2-" + i));
            }
            Check("disconnect: 8 queued", NetworkRuntime.DeferredCount, 8);
            fake.RaisePeerDisconnected(1, DisconnectReason.Remote);
            Check("disconnect: peer1 backlog purged", NetworkRuntime.DeferredCount, 3);
            Check("disconnect: peer1 removed from connected set", NetworkRuntime.ConnectedPeers.Contains(1), false);
            DB.MainPR = ReadyPlayer(loading: false);
            NetworkRuntime.Poll();
            Check("disconnect: only peer2 messages dispatched", string.Join(",", dispatched), "p2-0,p2-1,p2-2");

            // (h) 回调抛异常不能中断同事件后续消息（立即分发 + 排空两条路径）。
            dispatched.Clear();
            Action<int, NetChannel, byte[]> throwing = (peer, channel, data) =>
            {
                string text = Text(data);
                if (text == "boom")
                {
                    throw new InvalidOperationException("intentional test exception");
                }
                dispatched.Add(text);
            };
            NetworkRuntime.RegisterGameHandler(throwing);
            DB.MainPR = ReadyPlayer(loading: false);
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("ok1"));
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("boom"));
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("ok2"));
            Check("exception: immediate path continues", string.Join(",", dispatched), "ok1,ok2");

            dispatched.Clear();
            DB.MainPR = ReadyPlayer(loading: true);
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("q1"));
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("boom"));
            fake.RaiseMessage(1, NetChannel.Sequenced, Ascii("q2"));
            DB.MainPR = ReadyPlayer(loading: false);
            NetworkRuntime.Poll();
            Check("exception: flush path continues", string.Join(",", dispatched), "q1,q2");
            Check("exception: logged", Plugin.Logger.Error.Exists(m => m.Contains("game message dispatch failed")), true);
        }

        static void ShutdownAndRestart()
        {
            ResetRuntime();
            PolarisNoelsTools.NetworkConfig config = ClientConfig("guest");
            DB.InitConfig = config;
            FakeTransport first = new FakeTransport();
            Inject(first);
            NetworkRuntime.StartClient(config, "PN1-A");
            ClientSession client1 = NetworkRuntime.Client;
            first.RaiseJoinResult(0);
            first.RaiseMessage(0, NetChannel.Reliable, HostMessageBytes(3, "g"));
            first.RaiseMessage(0, NetChannel.Bulk, PackedSaveBulk(MakeRawSave(64)));
            first.RaiseBulkProgress(0, 1, 3, 10);
            Check("restart: client1 Ready", client1.Ready, true);
            Check("restart: transport injected", ReferenceEquals(NetworkRuntime.Transport, first), true);
            Check("restart: progress before shutdown", client1.SaveDone, (uint)3);

            NetworkRuntime.Shutdown();
            Check("shutdown: Transport null", NetworkRuntime.Transport == null, true);
            Check("shutdown: Client null", NetworkRuntime.Client == null, true);
            Check("shutdown: Host null", NetworkRuntime.Host == null, true);
            Check("shutdown: deferred cleared", NetworkRuntime.DeferredCount, 0);
            Check("shutdown: peers cleared", NetworkRuntime.ConnectedPeers.Count, 0);
            Check("shutdown: transport Shutdown+Dispose called", first.ShutdownCalled && first.Disposed, true);

            // 旧会话必须已解绑：旧 transport 再触发事件不能改到旧会话状态。
            CheckOldSessionUnbound(first, client1);

            // 重启：换一个 fake，重复走 StartClient → 事件驱动。
            FakeTransport second = new FakeTransport();
            Inject(second);
            Check("restart: StartClient accepted", NetworkRuntime.StartClient(config, "PN1-B"), true);
            ClientSession client2 = NetworkRuntime.Client;
            Check("restart: new session instance", ReferenceEquals(client2, client1), false);
            Check("restart: new transport", ReferenceEquals(NetworkRuntime.Transport, second), true);
            Check("restart: Join uses new room code", second.JoinedRoomCode, "PN1-B");

            second.RaiseMessage(0, NetChannel.Bulk, PackedSaveBulk(MakeRawSave(128)));
            second.RaiseMessage(0, NetChannel.Reliable, HostMessageBytes(6, "g2"));
            second.RaiseJoinResult(0);
            Check("restart: client2 Ready", client2.Ready, true);
            Check("restart: client2 LocalId", client2.LocalId, 6);
            Check("restart: client2 archive bytes", client2.ReceivedArchiveBytes, 128L);
        }

        static void CheckOldSessionUnbound(FakeTransport old, ClientSession client)
        {
            old.RaiseJoinResult(99);
            Check("shutdown: old client unbound from JoinResult", client.LastJoinError, 0);
            old.RaiseBulkProgress(0, 1, 7, 10);
            Check("shutdown: old client unbound from BulkProgress", client.SaveDone, (uint)3);
        }

        // ---------------------------------------------------------------- 工具

        static void Scenario(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine($"== {name} ==");
            try
            {
                body();
            }
            catch (Exception e)
            {
                Fail($"scenario threw: {e}");
            }
        }

        static void Check(string name, object actual, object expected)
        {
            assertions++;
            if (Equals(actual, expected))
            {
                Console.WriteLine($"  ok   {name}");
                return;
            }
            failures++;
            Console.WriteLine($"  FAIL {name}: expected <{Fmt(expected)}> actual <{Fmt(actual)}>");
        }

        static void Fail(string message)
        {
            assertions++;
            failures++;
            Console.WriteLine($"  FAIL {message}");
        }

        static string Fmt(object value) => value == null ? "null" : value.ToString();

        static void ResetRuntime()
        {
            NetworkRuntime.Shutdown();
            DB.InitConfig = null;
            DB.MainPR = null;
            DB.SyncSaveContentBuffer = null;
            DB.partyInfos = new Dictionary<int, PartyManager.Party>();
            DB.peerConfigs = new Dictionary<int, ClientConfig>();
            DB.LocalNoelParty = 0;
            DB.PolarisNoelsHostKicked = false;
            DB.PolarisNoelsHostClosed = false;
            DB.Mute = false;
            PolarisNoelsTools.LocalID = -1;
            PolarisNoelsTools.Type = NetWorkType.Host;
            PolarisNoelsTools.SimBattleSyncHost = -1;
            PolarisNoelsTools.SimBattleSyncList = new List<int>();
            PolarisNoelsTools.CleanedUpPeers.Clear();
            PolarisNoelsTools.LastGeneratedConfigs = null;
            Plugin.Logger.Info.Clear();
            Plugin.Logger.Warning.Clear();
            Plugin.Logger.Error.Clear();
            Plugin.Logger.Debug.Clear();
            SVD.Dir = saveDir;
            if (Directory.Exists(saveDir))
            {
                foreach (string file in Directory.GetFiles(saveDir))
                {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// 生产里 EnsureTransport 负责把 NetworkRuntime 的事件处理器订阅到 transport；
        /// 测试直接注入 fake，因此这里用反射补上同样的订阅（onMessage/onConnected/onDisconnected）。
        /// </summary>
        static void Inject(FakeTransport fake)
        {
            TransportProperty.GetSetMethod(true).Invoke(null, new object[] { fake });
            fake.Message += (Action<int, NetChannel, byte[]>)OnMessageMethod.CreateDelegate(typeof(Action<int, NetChannel, byte[]>));
            fake.PeerConnected += (Action<int>)OnPeerConnectedMethod.CreateDelegate(typeof(Action<int>));
            fake.PeerDisconnected += (Action<int, DisconnectReason>)OnPeerDisconnectedMethod.CreateDelegate(typeof(Action<int, DisconnectReason>));
        }

        static nel.PRNoel ReadyPlayer(bool loading)
        {
            nel.NelM2DBase m2d = new nel.NelM2DBase { transferring_game_stopping = loading };
            return new nel.PRNoel { Mp = new nel.MpBase(), M2D = m2d, NM2D = m2d };
        }

        static PolarisNoelsTools.NetworkConfig ClientConfig(string nickname)
        {
            return new PolarisNoelsTools.NetworkConfig
            {
                Type = NetWorkType.Client,
                nickName = nickname,
                NoelType = NoelType.ColorNoel,
                NoelColor = ColorNoelColor.Cyan
            };
        }

        static byte[] HostMessageBytes(int initId, string nickname)
        {
            PolarisNoelsHostMessage message = new PolarisNoelsHostMessage
            {
                InitID = initId,
                SyncHost = 2,
                SyncConnectedList = new List<int> { initId },
                PeerConfigs = new List<KeyValuePair<int, ClientConfig>>
                {
                    new KeyValuePair<int, ClientConfig>(0, new ClientConfig { Nickname = "host", NoelType = NoelType.Inverse, NoelColor = ColorNoelColor.Blue }),
                    new KeyValuePair<int, ClientConfig>(initId, new ClientConfig { Nickname = nickname, NoelType = NoelType.Normal, NoelColor = ColorNoelColor.Red })
                },
                PeerParties = new List<KeyValuePair<int, PartyManager.Party>>
                {
                    new KeyValuePair<int, PartyManager.Party>(0, new PartyManager.Party { ID = 0, Name = "p0" }),
                    new KeyValuePair<int, PartyManager.Party>(initId, new PartyManager.Party { ID = initId, Name = "p" + initId })
                }
            };
            return HandshakeCodec.Encode(HandshakeCodec.KindHostMessage, message);
        }

        static byte[] PackedSaveBulk(byte[] raw)
        {
            return BulkCodec.Wrap(BulkCodec.KindSaveArchive, SaveTransfer.Pack(raw));
        }

        static byte[] MakeRawSave(int size)
        {
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)((i * 37 + 11) & 0xFF);
            }
            return data;
        }

        static byte[] Ascii(string text) => Encoding.UTF8.GetBytes(text);

        static string Text(byte[] data) => data == null ? null : Encoding.UTF8.GetString(data);

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
