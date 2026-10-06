using System.Collections.Generic;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels.CSNetworking
{
    /// <summary>
    /// 主机→入房者的业务握手消息（JSON，包在 HandshakeCodec 信封里）。
    /// 网格地址（旧 ClientIP/HostPort/PeerInfos）由原生层负责，这里只留业务字段。
    /// InitID 的值来自原生层分配的 peer id。
    /// </summary>
    public class PolarisNoelsHostMessage
    {
        public int InitID;

        public bool MutePlayer;

        public int PlayerID;

        public int SyncHost;

        public List<int> SyncConnectedList;

        public List<KeyValuePair<int, ClientConfig>> PeerConfigs;

        public List<KeyValuePair<int, PartyManager.Party>> PeerParties;
    }
}
