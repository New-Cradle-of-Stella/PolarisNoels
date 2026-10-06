using System.Collections.Generic;

namespace PolarisNoels
{
    /// <summary>本场注册过的敌人必须逐个确认被击败；移除实体或切图不算击败。</summary>
    public sealed class BattleEnemyRoster
    {
        readonly HashSet<int> registered = new();
        readonly HashSet<int> defeated = new();

        public int RegisteredCount => registered.Count;
        public int RemainingCount => registered.Count - defeated.Count;

        public void Register(int id) => registered.Add(id);

        public void MarkDefeated(int id)
        {
            if (registered.Contains(id)) defeated.Add(id);
        }

        public void MarkAlive(int id) => defeated.Remove(id);

        public void Clear()
        {
            registered.Clear();
            defeated.Clear();
        }
    }
}
