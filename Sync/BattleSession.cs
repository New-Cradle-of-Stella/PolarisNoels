using nel;
using UnityEngine;

namespace PolarisNoels
{
    /// <summary>一场联机战斗（召唤器）的开始与结束时的状态维护。</summary>
    public static class BattleSession
    {
        /// <summary>召唤器即将开启。返回 false 表示这场战斗已经开始过，应拦截。</summary>
        public static bool BeforeOpen(M2LpSummon summon)
        {
            PolarisNoelsTools.TotalBattleNoelCount = PolarisNoelsTools.GetBattleNoelCounts(summon);
            return !DB.StartedBattleSummonerKeys.Contains(summon.key);
        }

        /// <summary>召唤器已开启：确定战斗发起者并通知其他玩家。</summary>
        public static void AfterOpen(M2LpSummon summon)
        {
            if (PolarisNoelsTools.BattleStarterID == -1)
            {
                PolarisNoelsTools.BattleStarterID = PolarisNoelsTools.LocalID;
                if (PolarisNoelsTools.SimBattleReady)
                {
                    PolarisNoelsTools.SendSimBattleStartToAllPeers(PolarisNoelsTools.LocalID);
                }
                else
                {
                    PolarisNoelsTools.SendBattleStartToAllPeers(summon.key, PolarisNoelsTools.LocalID);
                }
            }
            PolarisNoelsTools.BattleStartT = Time.time;
            DB.IsInBattle = true;
            DB.CurSummoner = summon;
            DB.CurEnemies.Clear();
            DB.StartedBattleSummonerKeys.Add(summon.key);
        }

        /// <summary>召唤器关闭：清理同步状态与模拟战斗大厅状态。</summary>
        public static void AfterClose(M2LpSummon summon, bool defeated)
        {
            if (defeated)
            {
                PolarisNoelsTools.SendBattleEndToAllPeers(summon.key, PolarisNoelsTools.LocalID);
                DB.IsInBattle = false;
            }
            EntityRegistry.ForgetEnemies();
            DB.StartedBattleSummonerKeys.Remove(summon.key);
            PolarisNoelsTools.BattleStarterID = -1;
            PolarisNoelsTools.SimBattleSyncList.Clear();
            PolarisNoelsTools.SimBattleReadyList.Clear();
            PolarisNoelsTools.SimBattleSyncHost = -1;
            PolarisNoelsTools.SimBattleReady = false;
        }
    }
}
