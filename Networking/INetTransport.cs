using System;

namespace PolarisNoels.Networking
{
    /// <summary>三通道。取值直接对应原生层 PN_CH_*，不要改动顺序。</summary>
    public enum NetChannel
    {
        Reliable = 0,
        Sequenced = 1,
        Bulk = 2
    }

    /// <summary>断开原因。取值对应原生层 pn_disc_reason。</summary>
    public enum DisconnectReason
    {
        Local = 0,
        Remote = 1,
        Kicked = 2,
        Timeout = 3,
        Protocol = 4,
        RoomFull = 5,
        HostClosed = 6
    }

    /// <summary>入房结果。取值对应原生层 pn_join_error。</summary>
    public enum NetJoinError
    {
        Ok = 0,
        BadCode = 1,
        PunchTimeout = 2,
        HandshakeFailed = 3,
        RoomFull = 4
    }

    /// <summary>传输层启动参数，映射到 pn_config。此类型刻意不依赖 Unity/游戏程序集，供 NetSmoke 引用。</summary>
    public sealed class NetTransportConfig
    {
        /// <summary>0 = 随机端口。</summary>
        public int BindPort;

        /// <summary>含自己，2~8。</summary>
        public int MaxPeers = 5;

        public bool EnableStun = true;

        public bool EnableLan = true;

        /// <summary>逗号分隔的 host:port，null = 原生内置默认。</summary>
        public string StunServers;

        /// <summary>0 = 原生默认 20000。</summary>
        public int IdleTimeoutMs;
    }

    /// <summary>
    /// 窄传输接口：业务层只认识字节数组 + peerId + 通道。
    /// 实现必须是纯托管/原生桥，不依赖 Unity 或游戏类型。
    /// </summary>
    public interface INetTransport : IDisposable
    {
        /// <summary>主机 = 0，入房成功前 -1。</summary>
        int LocalPeerId { get; }

        /// <summary>已连接对端数（不含自己）。</summary>
        int PeerCount { get; }

        /// <summary>是否已成功初始化原生库并完成 ABI 校验。</summary>
        bool IsReady { get; }

        /// <summary>当前房间码；主机候选收集完成后才有值。没有则为 null。</summary>
        string RoomCode { get; }

        /// <summary>异步；房间码通过 RoomCodeReady 给出。</summary>
        void StartHost();

        /// <summary>异步；结果通过 JoinResult 给出。</summary>
        void Join(string roomCode);

        void JoinDirect(string host, int port);

        /// <summary>peer = -1 为广播。</summary>
        void Send(int peer, NetChannel channel, byte[] data);

        void Disconnect(int peer, DisconnectReason reason);

        int GetRttMs(int peer);

        /// <summary>每帧在主线程调用。</summary>
        void Poll();

        event Action<string> RoomCodeReady;

        /// <summary>NAT 类型与候选的 JSON 文本。</summary>
        event Action<string> NatInfo;

        /// <summary>0 = 成功，其余同 pn_join_error。</summary>
        event Action<int> JoinResult;

        event Action<int> PeerConnected;

        event Action<int, DisconnectReason> PeerDisconnected;

        event Action<int, NetChannel, byte[]> Message;

        /// <summary>peer, bulkId, done, total。</summary>
        event Action<int, uint, uint, uint> BulkProgress;

        /// <summary>level, text；level 同原生 1=error 2=warn 3=info 4=debug。</summary>
        event Action<int, string> Log;

        void Shutdown();
    }
}
