using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels
{
    /// <summary>
    /// 玩家模块。
    /// Authority（本机 PRNoel）：每帧写出快照，受到副本转来的伤害时结算。
    /// Replica（远程玩家的 ShadowNoel）：记录最新快照，把魔法事件交给 <see cref="ShadowNoelMagic"/>。
    /// </summary>
    public class NoelModule : IEntityModule
    {
        NetEntity entity;

        public void Attach(NetEntity entity)
        {
            this.entity = entity;
        }

        public void Write(EntityState state)
        {
            if (entity.IsAuthority && DB.MainPR != null && DB.MainPR.AnmN != null)
            {
                state.Noel = ShadowNoelExtensions.GetSendInfo();
            }
        }

        public void Read(EntityState state)
        {
            // 快照写入玩家记录，由 UpdateAllNoels 按所在地图决定是否显示、如何应用
            if (!entity.IsAuthority && state.Noel != null)
            {
                PolarisNoelsTools.UpdateNoel(entity.OwnerPeer, state.Noel);
            }
        }

        public void OnEvent(EntityEvent ev)
        {
            switch (ev.Type)
            {
                case EntityEventType.Damage when entity.IsAuthority && ev.Damage != null:
                    ShadowNoelExtensions.DamageLocalNoel(ev.Damage);
                    break;
                case EntityEventType.Magic when !entity.IsAuthority && ev.Magic != null:
                    if (entity.TryGetComponent(out ShadowNoel noel))
                    {
                        noel.Magic.Apply(ev.Magic);
                    }
                    break;
            }
        }

        public void Tick()
        {
        }
    }
}
