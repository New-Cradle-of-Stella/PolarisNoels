using nel;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PolarisNoels
{
    /// <summary>发起者管理整场战斗；原版判定整场结束且名册全部击败后才能结算。</summary>
    public static class BattleSession
    {
        static readonly BattleEnemyRoster roster = new();
        static readonly Dictionary<int, NelEnemy> enemies = new();
        static readonly Dictionary<string, int> announcedBattles = new();
        static int pendingStarter = -1;
        static bool applyingRemoteEnd;
        static bool interrupted;

        public static bool IsAuthority => DB.IsInBattle && PolarisNoelsTools.BattleStarterID == PolarisNoelsTools.LocalID;
        public static bool IsClosing => applyingRemoteEnd || interrupted;
        public static int RemainingEnemies => roster.RemainingCount;

        public static bool BeforeOpen(M2LpSummon summon)
        {
            return !DB.IsMultiplayer || (!DB.StartedBattleSummonerKeys.Contains(summon.key)
                && (!DB.IsInBattle || DB.CurSummoner == summon));
        }

        // activateInner 是普通战斗、救援与模拟战斗共用的入口，早于敌人生成。
        public static void BeforeActivate(M2LpSummon summon)
        {
            if (!DB.IsMultiplayer || summon == null) return;
            int starter = pendingStarter >= 0 ? pendingStarter : PolarisNoelsTools.LocalID;
            // 原版也会关闭旧召唤器；先关闭，防止其回调把新一场的名册清掉。
            EnemySummoner.ActiveScript?.close(true, false);
            roster.Clear();
            enemies.Clear();
            applyingRemoteEnd = interrupted = false;
            PolarisNoelsTools.BattleStarterID = starter;
            PolarisNoelsTools.TotalBattleNoelCount = PolarisNoelsTools.GetBattleNoelCounts(summon);
            PolarisNoelsTools.BattleStartT = Time.time;
            DB.IsInBattle = true;
            DB.CurSummoner = summon;
            DB.CurEnemies.Clear();
            DB.StartedBattleSummonerKeys.Add(summon.key);
        }

        public static void OpenRemote(int starter, Action open)
        {
            pendingStarter = starter;
            try { open(); }
            finally { pendingStarter = -1; }
        }

        public static void AfterOpen(M2LpSummon summon)
        {
            if (!DB.IsMultiplayer || DB.CurSummoner != summon) return;
            if (summon.Reader == null || !summon.Reader.isActive())
            {
                Reset();
                return;
            }
            if (!IsAuthority) return;
            if (PolarisNoelsTools.SimBattleReady)
                PolarisNoelsTools.SendSimBattleStartToAllPeers(PolarisNoelsTools.LocalID);
            else
                PolarisNoelsTools.SendBattleStartToAllPeers(summon.key, PolarisNoelsTools.LocalID);
        }

        public static void RegisterEnemy(int id, NelEnemy enemy)
        {
            if (!IsAuthority || IsClosing || enemy == null) return;
            roster.Register(id);
            enemies[id] = enemy;
        }

        // HP 归零才算击败。销毁、脱离地图或断线本身不能让发起者报告胜利。
        public static void ObserveEnemy(NelEnemy enemy)
        {
            if (!IsAuthority || enemy == null || enemy.hp > 0) return;
            if (enemy.TryGetComponent<NetEntity>(out var entity) && enemies.TryGetValue(entity.Id, out var registered)
                && ReferenceEquals(registered, enemy)) roster.MarkDefeated(entity.Id);
        }

        public static void Update()
        {
            if (!IsAuthority || IsClosing) return;
            foreach (var pair in enemies)
            {
                if (pair.Value == null) continue;
                if (pair.Value.hp <= 0) roster.MarkDefeated(pair.Key);
                else roster.MarkAlive(pair.Key);
            }
        }

        public static bool CanClose(M2LpSummon summon, bool defeated)
        {
            if (!DB.IsMultiplayer || DB.CurSummoner != summon || !DB.IsInBattle || !defeated) return true;
            if (interrupted) return false;
            if (applyingRemoteEnd) return true;
            Update();
            return IsAuthority && roster.RemainingCount == 0;
        }

        public static bool CanClose(EnemySummoner summon, bool defeated)
            => DB.CurSummoner?.Reader != summon || CanClose(DB.CurSummoner, defeated);

        public static void ReceiveEnd(int starter, string key, bool aborted)
        {
            if (!DB.IsInBattle || starter != PolarisNoelsTools.BattleStarterID || DB.CurSummoner?.key != key)
            {
                if (key != null && announcedBattles.TryGetValue(key, out int owner) && owner == starter)
                {
                    announcedBattles.Remove(key);
                    DB.StartedBattleSummonerKeys.Remove(key);
                }
                return;
            }
            applyingRemoteEnd = true;
            Finish(!aborted);
        }

        public static void RememberRemoteBattle(int starter, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            announcedBattles[key] = starter;
            DB.StartedBattleSummonerKeys.Add(key);
        }

        public static void OnPeerDisconnected(int peer)
        {
            bool starterLeft = DB.IsInBattle && peer == PolarisNoelsTools.BattleStarterID;
            if (starterLeft) interrupted = true;
            var keys = new List<string>();
            foreach (var pair in announcedBattles) if (pair.Value == peer) keys.Add(pair.Key);
            foreach (string key in keys)
            {
                announcedBattles.Remove(key);
                DB.StartedBattleSummonerKeys.Remove(key);
            }
            try { EntityRegistry.KillEnemiesOwnedBy(peer); }
            finally { if (starterLeft) Finish(false); }
        }

        static void Finish(bool defeated)
        {
            var summon = DB.CurSummoner;
            try
            {
                if (summon != null)
                {
                    EntityRegistry.KillEnemiesOwnedBy(PolarisNoelsTools.BattleStarterID);
                    // 走原版关闭流程。false 恢复封路/界面，不写胜利与奖励。
                    if (summon.Reader != null) summon.Reader.close(true, defeated);
                    else summon.closeSummoner(defeated, out _);
                }
            }
            finally { Reset(); }
        }

        public static void AfterClose(M2LpSummon summon, bool defeated)
        {
            if (!DB.IsMultiplayer || DB.CurSummoner != summon || !DB.IsInBattle) return;
            if (IsAuthority && !applyingRemoteEnd && !interrupted)
                PolarisNoelsTools.SendBattleEndToAllPeers(summon.key, PolarisNoelsTools.LocalID, defeated);
            Reset();
        }

        public static void Reset()
        {
            if (DB.CurSummoner != null)
            {
                announcedBattles.Remove(DB.CurSummoner.key);
                DB.StartedBattleSummonerKeys.Remove(DB.CurSummoner.key);
            }
            DB.IsInBattle = false;
            DB.CurSummoner = null;
            DB.CurEnemies.Clear();
            roster.Clear();
            enemies.Clear();
            EntityRegistry.ForgetEnemies();
            EntityFactory.ClearBattleBindings();
            PolarisNoelsTools.BattleStarterID = -1;
            PolarisNoelsTools.BattleStartT = 0;
            PolarisNoelsTools.TotalBattleNoelCount = 0;
            PolarisNoelsTools.SimBattleSyncList.Clear();
            PolarisNoelsTools.SimBattleReadyList.Clear();
            PolarisNoelsTools.SimBattleSyncHost = -1;
            PolarisNoelsTools.SimBattleReady = false;
            applyingRemoteEnd = interrupted = false;
        }

        public static void ResetAll()
        {
            Reset();
            announcedBattles.Clear();
            pendingStarter = -1;
        }
    }
}
