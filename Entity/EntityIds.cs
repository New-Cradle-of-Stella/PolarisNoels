namespace WeNeedMoreNoels
{
    /// <summary>
    /// 实体 ID = (所有者 PeerId + 1) &lt;&lt; 20 | 序号。
    /// 序号 0 固定表示"该玩家本人"，其余用于该玩家创建的敌人等实体。
    /// </summary>
    public static class EntityIds
    {
        const int SeqBits = 20;
        const int SeqMask = (1 << SeqBits) - 1;

        static int seq;

        public static int ForPlayer(int peerId) => (peerId + 1) << SeqBits;

        /// <summary>为本机创建的实体分配新 ID。</summary>
        public static int NewLocal()
        {
            int s = ++seq & SeqMask;
            if (s == 0)
            {
                s = ++seq & SeqMask;
            }
            return ((WNMNTools.LocalID + 1) << SeqBits) | s;
        }

        public static int Owner(int entityId) => (entityId >> SeqBits) - 1;

        public static bool IsPlayer(int entityId) => (entityId & SeqMask) == 0;
    }
}
