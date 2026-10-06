using System;
using System.Collections.Generic;
using PolarisNoels.Networking;

namespace PolarisNoels.SessionSmoke
{
    /// <summary>
    /// INetTransport 的测试替身：只记录调用并允许测试主动触发真实事件，
    /// 不实现任何会话/延迟分发逻辑（那些来自源链接的真实 NetworkRuntime / 会话类）。
    /// </summary>
    public sealed class FakeTransport : INetTransport
    {
        public sealed class SentMessage
        {
            public int Peer;
            public NetChannel Channel;
            public byte[] Data;

            public SentMessage(int peer, NetChannel channel, byte[] data)
            {
                Peer = peer;
                Channel = channel;
                Data = data;
            }
        }

        public int LocalPeerId { get; set; } = 0;
        public int PeerCount { get; set; }
        public bool IsReady { get; set; } = true;
        public string RoomCode { get; set; }

        public readonly List<SentMessage> Sent = new List<SentMessage>();
        public readonly List<(int Peer, DisconnectReason Reason)> Disconnects = new List<(int, DisconnectReason)>();

        public bool StartHostCalled;
        public bool ShutdownCalled;
        public bool Disposed;
        public string JoinedRoomCode;
        public string JoinedDirectHost;
        public int JoinedDirectPort;
        public int PollCalls;

        public event Action<string> RoomCodeReady;
        public event Action<string> NatInfo;
        public event Action<int> JoinResult;
        public event Action<int> PeerConnected;
        public event Action<int, DisconnectReason> PeerDisconnected;
        public event Action<int, NetChannel, byte[]> Message;
        public event Action<int, uint, uint, uint> BulkProgress;
        public event Action<int, string> Log;

        public void StartHost() => StartHostCalled = true;

        public void Join(string roomCode) => JoinedRoomCode = roomCode;

        public void JoinDirect(string host, int port)
        {
            JoinedDirectHost = host;
            JoinedDirectPort = port;
        }

        public void Send(int peer, NetChannel channel, byte[] data) => Sent.Add(new SentMessage(peer, channel, data));

        public void Disconnect(int peer, DisconnectReason reason) => Disconnects.Add((peer, reason));

        public int GetRttMs(int peer) => 0;

        public void Poll() => PollCalls++;

        public void Shutdown() => ShutdownCalled = true;

        public void Dispose() => Disposed = true;

        public void RaiseJoinResult(int code) => JoinResult?.Invoke(code);

        public void RaisePeerConnected(int peer) => PeerConnected?.Invoke(peer);

        public void RaisePeerDisconnected(int peer, DisconnectReason reason) => PeerDisconnected?.Invoke(peer, reason);

        public void RaiseMessage(int peer, NetChannel channel, byte[] data) => Message?.Invoke(peer, channel, data);

        public void RaiseBulkProgress(int peer, uint bulkId, uint done, uint total) => BulkProgress?.Invoke(peer, bulkId, done, total);

        public void RaiseNatInfo(string json) => NatInfo?.Invoke(json);

        public void RaiseRoomCodeReady(string code) => RoomCodeReady?.Invoke(code);

        public void RaiseLog(int level, string text) => Log?.Invoke(level, text);
    }
}
