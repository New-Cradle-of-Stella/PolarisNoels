using PolarisNoels.SN;
using XX;

namespace PolarisNoels
{
    public static class LocalNickname
    {
        /// <summary>按当前联机配置生成本机玩家头顶的昵称（隐藏昵称时显示为编号）。</summary>
        public static void Apply()
        {
            string nickname;
            if (DB.InitConfig.InvisibleNickname)
            {
                nickname = TX.Get("multiplayer_noel_nickname") + PolarisNoelsTools.LocalID.ToString();
            }
            else
            {
                nickname = DB.InitConfig.nickName == "" ? $"Nickname#{PolarisNoelsTools.LocalID}" : DB.InitConfig.nickName;
            }
            ShadowNoelExtensions.GenerateMainPRNickname(nickname);
        }
    }
}
