using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels.CSNetworking
{
    /// <summary>入房者→主机的业务握手消息（JSON，包在 HandshakeCodec 信封里）。不再携带 IP/端口。</summary>
    public class PolarisNoelsClientMessage
    {
        public string NickName;
        public NoelType NoelType;
        public ColorNoelColor NoelColor;
        public int ID;
        public PartyManager.Party Party;
    }
}
