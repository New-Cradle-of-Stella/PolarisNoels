using m2d;
using nel;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels
{
    /// <summary>按联机配置（普通 / 反转 / 彩色 Noel）创建玩家动画器。</summary>
    public static class NoelAnimatorFactory
    {
        /// <summary>创建动画器。返回 false 表示非联机，应交给原版处理。</summary>
        public static bool TryCreate(PRNoel noel, ref PrAnimator Anm)
        {
            WNMNTools.NetworkConfig config = DB.InitConfig;
            if (config is null)
            {
                return false;
            }
            M2PxlAnimatorRT animatorRT;
            noel.SfPose = new AnimationShufflerNoel(noel);
            PrPoseContainer container;
            switch (config.NoelType)
            {
                case NoelType.Normal:
                    animatorRT = Create(noel, "noel");
                    container = MTR.PConNoelAnim;
                    container.iniPxlResourcesASync<PRNoel.OUTFIT>(MTR.Anoel_pxls, 56f, CaneManager.DefaultCane);
                    break;
                case NoelType.Inverse:
                    animatorRT = Create(noel, "noel_inverse");
                    container = MTRExtension.PConNoelIAnim;
                    container.iniPxlResourcesASync<PRNoel.OUTFIT>(MTRExtension.Anoel_inverse_pxls, 56f, CaneManager.DefaultCane);
                    break;
                case NoelType.ColorNoel:
                    animatorRT = Create(noel, MTRExtension.GetColorNoelName(config.NoelColor));
                    container = MTRExtension.GetPrPoseContainer(config.NoelColor);
                    container.iniPxlResourcesASync<PRNoel.OUTFIT>(MTRExtension.GetColorNoelPxlsFull(config.NoelColor), 56f, CaneManager.DefaultCane);
                    break;
                default:
                    return true;
            }
            noel.AnmN = new ShadowNoelAnimator(noel, animatorRT, container, false);
            Anm = noel.AnmN;
            noel.AnmN.initS(animatorRT);
            return true;
        }

        static M2PxlAnimatorRT Create(PRNoel noel, string name)
        {
            return noel.Mp.M2D.createBasicPxlAnimatorForRenderTicket(noel, name, "stand", false, M2Mover.DRAW_ORDER.PR1);
        }
    }
}
