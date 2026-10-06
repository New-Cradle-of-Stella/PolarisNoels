using m2d;
using nel;
using PixelLiner.PixelLinerLib;
using ProtoBuf;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using PolarisNoels.SN;

namespace PolarisNoels
    {
    public static partial class PolarisNoelsTools
    {
        public static void BroadcastMsg(string id)
        {
            SendMsg(LocalID, id);
            SendNotifyShortMsgToAllPeers(LocalID, id);
        }

        public static void GetItem(int partyID, string key, int count, int grade)
        {
            if (partyID != DB.LocalNoelParty)
            {
                return;
            }
            NelItem item = NelItem.GetById(key);
            if (item is not null)
            {
                ApplyingRemoteChange = true;
                try { DB.MainPR.NM2D.IMNG.getItem(item, count, grade); }
                finally { ApplyingRemoteChange = false; }
            }
        }

        public static void LoseItem(int partyID, string key, int count, int grade)
        {
            if (partyID != DB.LocalNoelParty)
            {
                return;
            }
            NelItem item = NelItem.GetById(key);
            if (item is not null)
            {
                ApplyingRemoteChange = true;
                try { DB.MainPR.NM2D.IMNG.reduceItem(item, count, grade); }
                finally { ApplyingRemoteChange = false; }
            }
        }

        public static void GetCoin(int partyID, CoinStorage.CTYPE type, int count)
        {
            if (partyID != DB.LocalNoelParty)
            {
                return;
            }
            ApplyingRemoteChange = true;
            try { CoinStorage.addCount(count, type); }
            finally { ApplyingRemoteChange = false; }
        }

        public static void LoseCoin(int partyID, CoinStorage.CTYPE type, int count)
        {
            if (partyID != DB.LocalNoelParty)
            {
                return;
            }
            ApplyingRemoteChange = true;
            try { CoinStorage.reduceCount(count, type); }
            finally { ApplyingRemoteChange = false; }
        }

        public static bool HasSyncEnemy()
        {
            return EntityRegistry.HasEnemyReplica();
        }

        public static int GetBattleNoelCounts(M2LpSummon summon)
        {
            return DB.noelIns.Where(x => x.Value.Enabled).Select(x => x.Value.Noel.IsNearLpSummon(summon)).Count(x => x) + 1;
        }

        public static void CheckEnemyEmptyAndEndBattle()
        {
            if (DB.IsInBattle && !HasSyncEnemy() && Time.time - BattleStartT > 1f)
            {
                ShadowNoelExtensions.EndCurMapBattle();
            }
        }
    }
}
