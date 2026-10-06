using nel;

namespace PolarisNoels
{
    public static class NetworkBootstrap
    {
        /// <summary>本地 Noel 进入地图时：首次启动联机组件，并刷新头顶昵称。</summary>
        public static void OnNoelAppear(PRNoel noel)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            if (!PolarisNoelsTools.PeerIngameInited)
            {
                PolarisNoelsTools.InitNetworking(DB.InitConfig);
                PolarisNoelsTools.PeerIngameInited = true;
            }
            EntityFactory.AttachLocalPlayer(noel);
            if (PolarisNoelsTools.LocalID != -1)
            {
                LocalNickname.Apply();
            }
        }
    }
}
