using WeNeedMoreNoels.DataStruct;

namespace WeNeedMoreNoels
{
    /// <summary>
    /// 实体同步模块：负责 Mover 上的某一块状态（位置/血量、魔法……）。
    /// 同一个模块类同时服务 Authority（写）与 Replica（读）两个方向。
    /// </summary>
    public interface IEntityModule
    {
        void Attach(NetEntity entity);

        /// <summary>Authority：把当前状态写进快照。</summary>
        void Write(EntityState state);

        /// <summary>Replica：把收到的快照应用到本地 Mover。</summary>
        void Read(EntityState state);

        /// <summary>收到一个一次性事件。模块自行判断自己的角色是否关心该事件。</summary>
        void OnEvent(EntityEvent ev);

        /// <summary>Replica 每帧调用，用于需要持续维持的表现（例如半透明）。</summary>
        void Tick();
    }
}
