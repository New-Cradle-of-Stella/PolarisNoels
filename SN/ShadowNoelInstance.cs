using PolarisNoels.DataStruct;

namespace PolarisNoels.SN
{
    public class ShadowNoelInstance
    {
        public ShadowNoel Noel;
        public bool Enabled;
        public string Nickname;
        public string MpKey;
        public int ID;
        public ClientConfig NoelInitConfig;
        public UpdateNoelInfo NoelInfo;
        public ShadowNoelNickname NicknameIns;

        public string NickNameStr
        {
            get
            {
                return Nickname == "" ? $"Nickname#{ID}" : Nickname;
            }
        }
    }
}
