using m2d;
using nel;
using UnityEngine;
using PolarisNoels.DataStruct;

namespace PolarisNoels.SN
{
    public class ShadowNoel : PRMain
    {
        public ClientConfig InitConfig;
        public int ID;
        public int PartyID;

        /// <summary>该玩家的全部魔法状态与操作。</summary>
        public readonly ShadowNoelMagic Magic;

        public STATE CurState;

        public bool IsEvadeO;
        public bool IsAtkO;

        public M2Shield.STATE CurShieldState;

        public ShadowNoel()
        {
            Magic = new ShadowNoelMagic(this);
        }

        public ShadowNoelNickname NicknameIns;
        public ShadowNoelNickname MsgIns;

        public void CreateNicknameWithNoel(string nickname)
        {
            NicknameIns = ShadowNoelNickname.CreateNickname(this, nickname);
            MsgIns = ShadowNoelNickname.CreateMessageBubble(this);
        }

        public override void Awake()
        {
            base.Awake();
        }

        public override void newGame()
        {
            this.hp = (this.maxhp = 150);
            this.mp = (this.maxmp = 200);
            this.EpCon ??= new EpManager(this);
            if (base.VO == null)
            {
                base.VO = new PrVoiceController(this, MTR.VcNoelSource, this.snd_key + ".voice");
                this.BetoMng = BetobetoManager.GetManager("noel");
            }
            base.newGame();
            this.Ser.clear();
            this.EpCon.newGame();
            this.EggCon.newGame(false);
            this.GaugeBrk.reset();
            base.key = "shadow_noel";
            this.AbsorbCon = new AbsorbManagerContainer(5, this);
        }

        public override void changeState(STATE state, STATE prestate) { }

        public override void createAnimator(ref PrAnimator Anm)
        {
            if (InitConfig is not null)
            {
                M2PxlAnimatorRT m2PxlAnimatorRT;
                if (Anm == null)
                {
                    SfPose = new AnimationShuffler(this);
                    PrPoseContainer container;
                    switch (InitConfig.NoelType)
                    {
                        case NoelType.Normal:
                            m2PxlAnimatorRT = this.Mp.M2D.createBasicPxlAnimatorForRenderTicket(this, "noel_magic", "stand", false, M2Mover.DRAW_ORDER.PR1);
                            container = MTR.PConNoelAnim;
                            container.iniPxlResourcesASync<PRNoel.OUTFIT>(MTR.Anoel_pxls, 56f, CaneManager.DefaultCane);
                            break;
                        case NoelType.Inverse:
                            m2PxlAnimatorRT = this.Mp.M2D.createBasicPxlAnimatorForRenderTicket(this, "noel_inverse_magic", "stand", false, M2Mover.DRAW_ORDER.PR1);
                            container = MTRExtension.PConNoelIAnim;
                            container.iniPxlResourcesASync<PRNoel.OUTFIT>(MTRExtension.Anoel_inverse_pxls, 56f, CaneManager.DefaultCane);
                            break;
                        case NoelType.ColorNoel:
                            m2PxlAnimatorRT = this.Mp.M2D.createBasicPxlAnimatorForRenderTicket(this, MTRExtension.GetColorNoelName(InitConfig.NoelColor), "stand", false, M2Mover.DRAW_ORDER.PR1);
                            container = MTRExtension.GetPrPoseContainer(InitConfig.NoelColor);
                            container.iniPxlResourcesASync<PRNoel.OUTFIT>(MTRExtension.GetColorNoelPxlsFull(InitConfig.NoelColor), 56f, CaneManager.DefaultCane);
                            break;
                        default:
                            return;
                    }
                    AnmN = new ShadowNoelAnimator(this, m2PxlAnimatorRT, container, false);
                    Anm = AnmN;
                    AnmN.initS(m2PxlAnimatorRT);
                    return;
                }
            }
        }

        public override void appear(Map2d Mp)
        {
            DB.ShadowAppear = true;
            base.appear(Mp);
            DB.ShadowAppear = false;

            this.UP?.destruct();
            this.UP = null;
        }

        public override void refineMoveKey(bool ignore_keypushdown = false) { }

        public override bool runUi() {
            var tg = this.Mp.TalkTarget_;
            bool rt = base.runUi();
            if (tg != this.Mp.TalkTarget_) {
                this.Mp.setTalkTarget(tg);
            }
            return rt;
        }

        public override void runPre()
        {
            Skill.magic_t = Magic.T;
            if (state != CurState)
            {
                base.changeState(CurState, state);
            }
            Skill.ShE.Shield.stt = CurShieldState;
            if (IsEvadeO && state != STATE.SHIELD_BUSH && state != STATE.EVADE)
            {
                if (state != STATE.SHIELD_LARIAT)
                {
                    Skill.ShE.Shield.activate(false, false);
                }
            }
            else if (state != STATE.SHIELD_LARIAT)
            {
                Skill.ShE.Shield.deactivate(false, false);
            }
            Skill.ShE.Shield.run(TS);
            try
            {
                base.runPre();
            }
            catch { }
        }

        public override void runPost()
        {
            try
            {
                base.runPost();
            }
            catch { }
            Phy.killSpeedForce(true, true, true, true, true);
        }

        public override HITTYPE getHitType(M2Ray Ray)
        {
            return HITTYPE.EN;
        }

        public override void deactivateFromMap()
        {
            base.deactivateFromMap();
            Mp.removeMover(NicknameIns);
            NicknameIns.destruct();
            DB.noelIns[ID].NicknameIns = null;
        }

        private PrAnimator AnmN;
    }
}
