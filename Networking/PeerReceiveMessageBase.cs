using PolarisNoels.DataStruct;

namespace PolarisNoels.Networking
{
    public abstract class PeerReceiveMessageBase
    {
        public abstract bool CheckMessage(PolarisNoelsPeerMessage message);

        public abstract void ReceiveMessage(PolarisNoelsPeerMessage message);

        public abstract string ToMessageString(PolarisNoelsPeerMessage message);
    }
}
