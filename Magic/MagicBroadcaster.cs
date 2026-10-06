using nel;
using PolarisNoels.DataStruct;

namespace PolarisNoels
{
    /// <summary>本地玩家的魔法事件 -> 广播给其他玩家。只处理本地 PRNoel 的事件。</summary>
    public static class MagicBroadcaster
    {
        static void Send(NotifyNoelMagic magic)
        {
            if (!DB.IsMultiplayer || PolarisNoelsTools.LocalID < 0)
            {
                return;
            }
            EntityNet.SendEvent(EntityIds.ForPlayer(PolarisNoelsTools.LocalID), new EntityEvent
            {
                Type = EntityEventType.Magic,
                Magic = magic
            });
        }

        public static void OnMagicCleared(M2PrOverChargeSlot slot)
        {
            if (slot.Pr is PRNoel)
            {
                Send(new() { Type = NotifyMagicTpe.Kill });
            }
        }

        public static void OnMagicReawake(M2PrSkill skill, MGKIND kind)
        {
            if (skill.Pr is PRNoel && skill.CurMg != null)
            {
                Send(new()
                {
                    Type = NotifyMagicTpe.Reawake,
                    Kind = (int)kind,
                    T = skill.CurMg.t
                });
            }
        }

        public static void OnMagicSleep(MagicItem item)
        {
            if (item.Caster is PRNoel)
            {
                Send(new() { Type = NotifyMagicTpe.Sleep });
            }
        }

        /// <summary>火球转向。由 MgFireBall.fnManipulateFireBall 的 IL 补丁直接调用。</summary>
        public static void OnFireBallTurn(MagicItem Mg, M2MagicCaster caster)
        {
            if (caster is PRNoel)
            {
                Send(new() { Type = NotifyMagicTpe.Turn, agR = Mg.da });
            }
        }

        public static void OnWaterShardManipulate(MagicItem Mg, M2MagicCaster caster)
        {
            if (caster is not PRNoel)
            {
                return;
            }
            MgWaterShard.IdAndPhase(Mg, out int id, out int phase);
            if (phase != 500)
            {
                return;
            }
            Send(new()
            {
                Type = NotifyMagicTpe.WaterShoot,
                agR = Mg.aim_agR,
                id = id
            });
        }

        public static void OnItemBombInit(M2PrSkill skill, NelItem item, int grade)
        {
            if (skill.Pr is PRNoel)
            {
                Send(new()
                {
                    Type = NotifyMagicTpe.InitBomb,
                    Key = item.key,
                    Grade = grade
                });
            }
        }

        public static void OnItemBombRun(MagicItem Mg)
        {
            if (Mg.Caster is not PRNoel)
            {
                return;
            }
            if (Mg.Dro == null)
            {
                Send(new() { Type = NotifyMagicTpe.RemoveBomb });
                return;
            }
            Send(new()
            {
                Type = NotifyMagicTpe.UpdateBomb,
                T = Mg.t,
                Phase = Mg.phase,
                BombX = Mg.Dro.x,
                BombY = Mg.Dro.y
            });
        }
    }
}
