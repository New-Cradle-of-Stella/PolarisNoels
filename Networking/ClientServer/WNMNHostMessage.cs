using System.Collections.Generic;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.CSNetworking
{
    public class WNMNHostMessage
    {
        public string ClientIP;

        public int HostPort;

        public int InitID;

        public bool MutePlayer;

        public int PlayerID;

        public int SyncHost;

        public List<int> SyncConnectedList;

        public List<KeyValuePair<int, ConnectPeerInfo>> PeerInfos;

        public List<KeyValuePair<int, ClientConfig>> PeerConfigs;

        public List<KeyValuePair<int, PartyManager.Party>> PeerParties;
    }

    public class ConnectPeerInfo
    {
        public string IP;
        public int Port;
    }
}
