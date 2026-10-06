using m2d;
using nel;
using nel.mgm.smncr;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PolarisNoels.CSNetworking;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using PolarisNoels.SN;
using XX;

namespace PolarisNoels
{
    public static partial class PolarisNoelsTools
    {
        public static PolarisNoelsPeer peer;

        public static NetWorkType Type;
        public static int LocalID = -1;
        public static bool PeerIngameInited;

        public static bool ApplyingRemoteChange;
        public static bool EnablePVP;
        public static EnemySyncType SyncType;

        public static int BattleStarterID = -1;
        public static int TotalBattleNoelCount;

        public static float BattleStartT;

        public static int SimBattleSyncHost = -1;
        public static List<int> SimBattleSyncList = [];
        public static bool SimBattleSynced;

        public static bool SimBattleReady;
        public static List<int> SimBattleReadyList = [];

        public static System.Action UpdateSimUI;

        public static UiSmnCreator USC;
        public static UiSmncBattleConfirm USBC;


        public static SmncStageEditor SSE;

        public static SmncFile CurSimFile;

        public static bool IsSettingSpawnLocation;
        public static int CurrentSetID;
        public static Vector2 SettingResult;
        public static Dictionary<int, Vector2> SpawnDic = [];

        public static string GetNickname(int id)
        {
            if (id != LocalID && !DB.noelIns.ContainsKey(id))
            {
                return "";
            }
            return id == LocalID ? DB.MainPRNickname.GetCurrentText() : DB.noelIns[id].NickNameStr;
        }

        public static List<KeyValuePair<int, string>> AllNicknames        {
            get
            {
                return [.. DB.noelIns.Select(x => new KeyValuePair<int, string>(x.Key, DB.InitConfig.InvisibleNickname ? TX.Get("multiplayer_noel_nickname") + x.Key : x.Value.NickNameStr))];
            }
        }

        /// <summary>
        /// 进入游戏世界时挂上业务外壳。传输本身在标题界面就建好了，这里沿用同一条连接，不重连。
        /// </summary>
        public static void InitNetworking(NetworkConfig config)
        {
            DB.networkType = config.Type;
            Type = config.Type;
            // peer id 来自原生层：主机固定 0，客户端用入房时分配的 id。
            LocalID = config.Type == NetWorkType.Host ? 0 : (NetworkRuntime.Client?.LocalId ?? -1);
            if (GameObject.Find("NetworkPeer") != null)
            {
                Plugin.Logger.LogWarning("NetworkPeer already created");
                return;
            }
            GameObject gameObject = new("NetworkPeer");
            peer = gameObject.AddComponent<PolarisNoelsPeer>();
            DB.Nickname = config.nickName;
            if (config.Type == NetWorkType.Client)
            {
                // 影子要等本地主角存在后再生成，标题界面只记录主机下发的配置。
                NetworkRuntime.Client?.OnEnterGameWorld();
            }
        }

        public static void UpdateNoel(int id, UpdateNoelInfo info)
        {
            if (!DB.noelIns.ContainsKey(id))
            {
                return;
            }
            DB.noelIns[id].NoelInfo = info;
        }

        public static void UpdateAllNoels()
        {
            foreach (var pair in DB.noelIns)
            {
                ShadowNoelExtensions.UpdateShadowNoelInfo(pair.Key);
            }
        }

        public static void GenerateAllNoels(List<KeyValuePair<int, ClientConfig>> list)
        {
            foreach (var pair in list)
            {
                ShadowNoelExtensions.GenerateShadowNoel(pair.Value, pair.Key);
            }
        }

        public static void CleanUpClient(int id)
        {
            ShadowNoelExtensions.DisableShadowNoel(id);
            DB.noelIns.Remove(id);
            DB.partyInfos.Remove(id);
            DB.peerConfigs.Remove(id);
            DB.peerDelays.Remove(id);
            EntityRegistry.DestroyReplicasOwnedBy(id);
            SimBattleSyncList.Remove(id);
            SimBattleReadyList.Remove(id);
            if (USC != null)
            {
                UpdateSimUI?.Invoke();
            }
        }

        public static void SetAllNickNameBgs()
        {
            ShadowNoelNickname nicknameIns = DB.MainPRNickname;
            if (DB.partyInfos.TryGetValue(DB.LocalNoelParty, out var localParty))
            {
                nicknameIns?.SetBgColor(localParty.Color);
            }
            foreach (var pair in DB.noelIns)
            {
                int partyId = pair.Value.Noel != null ? pair.Value.Noel.PartyID : pair.Value.NoelInfo.PartyID;
                if (DB.partyInfos.TryGetValue(partyId, out var party))
                {
                    pair.Value.NicknameIns?.SetBgColor(party.Color);
                }
            }
        }

        public static void UpdatePeer(int id, UpdatePeerInfo info)
        {
            switch (info.Type)
            {
                case UpdatePeerType.Nickname:
                    if (DB.peerConfigs.TryGetValue(id, out var cfg))
                    {
                        cfg.Nickname = info.NickName;
                    }
                    break;
                case UpdatePeerType.Party:
                    if (DB.noelIns.TryGetValue(id, out var ins))
                    {
                        ins.NoelInfo.PartyID = info.PartyID;
                        if (ins.Noel != null) ins.Noel.PartyID = info.PartyID;
                    }
                    break;
            }
        }

        public static void TransferMainNoel(NotifyNoelTransfer transfer)
        {
            if (!DB.IsInBattle)
            {
                TransferMainNoel(transfer.Key, transfer.X, transfer.Y);
            }
        }

        public static void TransferMainNoel(string key, float x, float y)
        {
            MapTeleport.Transfer(key, x, y);
        }

        public static void Kick(int id)
        {
            NetworkRuntime.Transport?.Disconnect(id, DisconnectReason.Kicked);
        }

        public static void Mute(int id)
        {
            NetworkRuntime.Host?.SendMute(id);
        }

        public static void ToggleMute()
        {
            DB.Mute = !DB.Mute;
        }


    public static void CleanUp()
        {
            LocalID = -1;
            peer = null;
            PeerIngameInited = false;
            USC = null;
            USBC = null;
            SSE = null;
            CurSimFile = null;
            SpawnDic.Clear();
    }

        public class NetworkConfig
        {
            public NetWorkType Type;

            public int port;

            public string ip;

            public NoelType NoelType;

            public ColorNoelColor NoelColor;

            public bool InvisibleNickname;

            public string nickName;

            public static implicit operator ClientConfig(NetworkConfig config)
            {
                return new()
                {
                    Nickname = config.nickName,
                    NoelType = config.NoelType,
                    NoelColor = config.NoelColor
                };
            }
        }
    }

    public enum NetWorkType
    {
        Host,
        Client
    }
    
    public enum EnemySyncType
    {
        StarterOnly,
        SmartAverage,
        Independent
    }
}
