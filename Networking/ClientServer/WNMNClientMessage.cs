using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.CSNetworking
{
    public class WNMNClientMessage
    {
        public string NickName;
        public NoelType NoelType;
        public ColorNoelColor NoelColor;
        public int ID;
        public string HostIP;
        public int Port;
        public PartyManager.Party Party;
    }
}
