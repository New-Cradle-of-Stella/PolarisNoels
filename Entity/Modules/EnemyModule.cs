using m2d;
using nel;
using WeNeedMoreNoels.DataStruct;
using XX;

namespace WeNeedMoreNoels
{
    /// <summary>
    /// 普通敌人模块：同步位置、姿态、朝向、血量、状态。
    /// Authority 写出快照并结算副本转来的伤害；Replica 应用快照并保持半透明。
    /// </summary>
    public class EnemyModule : IEntityModule
    {
        protected NetEntity entity;
        protected NelEnemy enemy;

        public void Attach(NetEntity entity)
        {
            this.entity = entity;
            enemy = entity.GetComponent<NelEnemy>();
        }

        public virtual void Write(EntityState state)
        {
            if (enemy == null)
            {
                return;
            }
            enemy.getPosition(out float x, out float y);
            state.Enemy = new UpdateEnemyInfo
            {
                PositionX = x,
                PositionY = y,
                Pose = GetPose(),
                Aim = (int)enemy.aim,
                Hp = enemy.hp,
                Mp = enemy.mp,
                State = (int)enemy.state,
                T = enemy.t
            };
        }

        public virtual void Read(EntityState state)
        {
            UpdateEnemyInfo info = state.Enemy;
            if (info == null || enemy == null || enemy.Phy == null || enemy.Anm == null)
            {
                return;
            }
            enemy.setTo(info.PositionX, info.PositionY);
            enemy.Phy.killSpeedForce(true, true, true, true, true);
            enemy.getAnimator().setPose(info.Pose);
            enemy.setAim((AIM)info.Aim);
            enemy.hp = info.Hp;
            enemy.mp = info.Mp;
            enemy.changeState((NelEnemy.STATE)info.State);
            enemy.t = info.T;
        }

        public void OnEvent(EntityEvent ev)
        {
            if (ev.Type == EntityEventType.Damage && entity.IsAuthority && ev.Damage != null && enemy != null)
            {
                enemy.applyDamage(new NelAttackInfo
                {
                    attr = MGATTR.NORMAL,
                    ndmg = NDMG.DEFAULT,
                    hpdmg0 = ev.Damage.Hp,
                    mpdmg0 = ev.Damage.Mp,
                    fix_damage = true,
                    parryable = false,
                    shield_break_ratio = 1f,
                    ignore_nodamage_time = true,
                    nodamage_time = 0,
                }, true);
            }
        }

        /// <summary>副本用半透明表示"这不是我这边的敌人"。</summary>
        public void Tick()
        {
            if (enemy != null && enemy.Anm != null)
            {
                enemy.Anm.alpha = 0.4f;
            }
        }

        string GetPose()
        {
            var anim = enemy.getAnimator();
            if (anim is EnemyAnimatorPxl pxl)
            {
                return pxl.Anm.pose_title;
            }
            if (anim is EnemyAnimatorSpine spine)
            {
                return spine.pose_title0_;
            }
            return string.Empty;
        }
    }

    /// <summary>Boss 模块：Boss 由游戏自己驱动，只同步血量和魔力。</summary>
    public class BossEnemyModule : EnemyModule
    {
        public override void Write(EntityState state)
        {
            if (enemy != null)
            {
                state.Enemy = new UpdateEnemyInfo { Hp = enemy.hp, Mp = enemy.mp };
            }
        }

        public override void Read(EntityState state)
        {
            if (state.Enemy != null && enemy != null)
            {
                enemy.hp = state.Enemy.Hp;
                enemy.mp = state.Enemy.Mp;
            }
        }
    }
}
