using nel;
using System.Collections.Generic;
using System.Linq;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;
using XX;

namespace PolarisNoels
{
    /// <summary>
    /// 远程玩家（ShadowNoel）的魔法控制器：
    /// 持有该玩家的全部魔法状态，并把网络消息 / 每帧同步数据翻译成对本地影子角色的魔法操作。
    /// 与 ShadowNoel 的耦合只有 Skill 与 M2D。
    /// </summary>
    public class ShadowNoelMagic
    {
        readonly ShadowNoel owner;

        /// <summary>魔法 -> 所属控制器。供 MagicItem.calcAimPos 补丁查询瞄准角度。</summary>
        static readonly Dictionary<MagicItem, ShadowNoelMagic> owners = [];

        /// <summary>当前被追踪的魔法（由 Reawake 登记，Kill / 新 Reawake / Dispose 时取消）。</summary>
        MagicItem tracked;

        /// <summary>当前炸弹魔法（InitBomb 创建，RemoveBomb 清除）。</summary>
        MagicItem bomb;

        #region 每帧同步的状态
        public bool Chanting { get; private set; }
        public float AimAgR { get; private set; }
        public float T { get; private set; }
        public int HoldAim { get; private set; }
        #endregion

        public ShadowNoelMagic(ShadowNoel owner)
        {
            this.owner = owner;
        }

        /// <summary>该魔法是否属于某个远程玩家；是则返回其控制器。</summary>
        public static bool TryGetOwner(MagicItem item, out ShadowNoelMagic magic)
        {
            if (item == null)
            {
                magic = null;
                return false;
            }
            return owners.TryGetValue(item, out magic);
        }

        public static void ClearAll()
        {
            owners.Clear();
        }

        /// <summary>应用每帧同步过来的魔法状态。</summary>
        public void SyncFrom(UpdateNoelInfo info)
        {
            Chanting = info.ChantMagic;
            AimAgR = info.MagicAgR;
            T = info.MagicT;
            HoldAim = info.MagicHoldAim;
            owner.Skill.mp_hold = info.MagicHold;
            owner.Skill.Cursor.t_hold = info.HoldT;
        }

        /// <summary>应用一条魔法事件消息。</summary>
        public void Apply(NotifyNoelMagic msg)
        {
            switch (msg.Type)
            {
                case NotifyMagicTpe.Reawake: Reawake((MGKIND)msg.Kind, msg.T); break;
                case NotifyMagicTpe.Sleep: Sleep(); break;
                case NotifyMagicTpe.Kill: OnKillMessage(); break;
                case NotifyMagicTpe.Turn: Turn(msg.agR); break;
                case NotifyMagicTpe.WaterShoot: WaterShoot(msg.id, msg.agR); break;
                case NotifyMagicTpe.InitBomb: InitBomb(msg.Key, msg.Grade); break;
                case NotifyMagicTpe.UpdateBomb: UpdateBomb(msg); break;
                case NotifyMagicTpe.RemoveBomb: bomb = null; break;
            }
        }

        /// <summary>玩家被销毁 / 离开地图时调用，释放所有登记。</summary>
        public void Dispose()
        {
            Track(null);
            bomb = null;
        }

        void Reawake(MGKIND kind, float t)
        {
            owner.Skill.reawakeMagic(kind);
            MagicItem item = owner.Skill.CurMg;
            if (item == null)
            {
                return;
            }
            item.castedTimeResetTo(t);
            Track(item);
        }

        void Sleep()
        {
            owner.Skill.CurMg?.Sleep(false);
        }

        /// <summary>收到 Kill 消息：只有此前登记过魔法（经历过 Reawake）才会真正结束它。</summary>
        void OnKillMessage()
        {
            if (tracked == null)
            {
                return;
            }
            Track(null);
            Kill();
        }

        /// <summary>立即结束当前魔法并清理技能状态。</summary>
        public void Kill()
        {
            M2PrSkill skill = owner.Skill;
            if (skill.CurMg == null)
            {
                return;
            }
            skill.CurMg.close(true);
            skill.CurMg.kill(-1f);
            skill.OcSlots.clearMagic(skill.CurMg, false);
            skill.CurMg = null;
            skill.MagicSel.deactivate();
        }

        void Turn(float agR)
        {
            MagicItem item = owner.Skill.Cursor.getCurMg();
            if (item == null)
            {
                return;
            }
            MagicNotifiear mn = item.Mn;
            float accel_maxt = mn._2.accel_maxt;
            mn._0.time += 1f;
            mn._0.v0 = mn._2.v0;
            mn._0.maxt += mn._2.time + 1f - item.t;
            mn._0.accel_mint = accel_maxt;
            item.da = (item.sa = agR);
            item.sz = 0f;
            item.t = 1f;
            item.PtcST("mg_fireball_curve", PTCThread.StFollow.NO_FOLLOW, false);
        }

        void WaterShoot(int shardId, float agR)
        {
            var mgc = ((NelM2DBase)owner.M2D).MGC;
            mgc.countMg((Mg, caster) =>
            {
                MgWaterShard.IdAndPhase(Mg, out int id, out int phase);
                if (id == shardId && phase != 500)
                {
                    ((MgWaterShard)Mg.MGC.OHoldFD[MGKIND.WATERSHARD]).forceShotInit(Mg, 1, agR);
                }
                return true;
            }, owner);
            if (mgc.AItems.All(x => x.phase >> 2 == 5))
            {
                Kill();
            }
        }

        void InitBomb(string itemKey, int grade)
        {
            owner.Skill.initItemBomb(NelItem.GetById(itemKey), grade, null);
            bomb = owner.Skill.MhCurSkill.Mg;
        }

        void UpdateBomb(NotifyNoelMagic msg)
        {
            if (bomb == null || bomb.Dro == null)
            {
                return;
            }
            bomb.phase = msg.Phase;
            bomb.t = msg.T;
            bomb.Dro.x = msg.BombX;
            bomb.Dro.y = msg.BombY;
        }

        void Track(MagicItem item)
        {
            if (tracked == item)
            {
                return;
            }
            if (tracked != null)
            {
                owners.Remove(tracked);
            }
            tracked = item;
            if (item != null)
            {
                owners[item] = this;
            }
        }
    }
}
