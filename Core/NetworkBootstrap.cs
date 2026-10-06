using nel;

namespace WeNeedMoreNoels
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
            if (!WNMNTools.PeerIngameInited)
            {
                WNMNTools.InitNetworking(DB.InitConfig);
                WNMNTools.PeerIngameInited = true;
            }
            EntityFactory.AttachLocalPlayer(noel);
            if (WNMNTools.LocalID != -1)
            {
                LocalNickname.Apply();
            }
        }
    }
}
