using LiteNetLib;
using LiteNetLib.Utils;
using m2d;
using nel;
using nel.mgm.smncr;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeNeedMoreNoels.CSNetworking;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.Networking;
using WeNeedMoreNoels.SN;
using XX;

namespace WeNeedMoreNoels
{
    public static partial class WNMNTools
    {
        static WNMNHost host;
        static WNMNClient client;

        public static WNMNPeer peer;

        public static NetWorkType Type;
        public static int LocalID = -1;
        public static bool PeerIngameInited;

        public static string LocalIP;

        public static Dictionary<int, NetPeer> PeerDic = [];

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

        static int _unique_id;

        public static int Unique_ID
        {
            get
            {
                _unique_id++;
                return _unique_id;
            }
        }

        public static List<KeyValuePair<int, string>> AllNicknames
        {
            get
            {
                return [.. DB.noelIns.Select(x => new KeyValuePair<int, string>(x.Key, DB.InitConfig.InvisibleNickname ? TX.Get("multiplayer_noel_nickname") + x.Key : x.Value.NickNameStr))];
            }
        }

        public static void InitNetworking(NetworkConfig config)
        {
            InitNetworking(config.Type, out host, out client);
            GameObject gameObject = new("NetworkPeer");
            peer = gameObject.AddComponent<WNMNPeer>();
            int port = peer.StartPeer();
            switch (config.Type)
            {
                case NetWorkType.Host:
                    RunHost(config.port);
                    Type = NetWorkType.Host;
                    DB.peerConfigs.Add(0, new()
                    {
                        Nickname = config.nickName,
                        NoelType = config.NoelType,
                        NoelColor = config.NoelColor
                    });
                    break;
                case NetWorkType.Client:
                    ConnectHost(config.ip, config.port);
                    Type = NetWorkType.Client;
                    break;
            }
            DB.Nickname = config.nickName;
        }

        public static void InitNetworking(NetWorkType type, out WNMNHost host, out WNMNClient client)
        {
            DB.networkType = type;
            if (GameObject.Find("NetworkSource") != null)
            {
                Plugin.Logger.LogWarning("NetworkSource created");
                host = null;
                client = null;
                return;
            }
            GameObject gameObject = new("NetworkSource");
            switch (DB.networkType)
            {
                case NetWorkType.Host:
                    host = gameObject.AddComponent<WNMNHost>();
                    Type = NetWorkType.Host;
                    client = null;
                    break;
                case NetWorkType.Client:
                    client = gameObject.AddComponent<WNMNClient>();
                    Type = NetWorkType.Client;
                    host = null;
                    break;
                default:
                    host = null;
                    client = null;
                    break;
            }
        }

        public static void ConnectOtherPeer(List<KeyValuePair<int, ConnectPeerInfo>> peerList, NetPeer hostPeer, int hostPort)
        {
            if (peerList.Count == 0)
            {
                peer.ConnectPeer(hostPeer.EndPoint.Address.ToString(), hostPort);
                return;
            }
            int id = Type == NetWorkType.Host ? 0 : client.peerID;
            foreach (var pair in peerList)
            {
                if (pair.Key != id)
                {
                    peer.ConnectPeer(pair.Value.IP, pair.Value.Port);
                }
            }
        }

        public static void RunHost(int port = 47210)
        {
            if (host == null)
            {
                Plugin.Logger.LogWarning("NetworkSource not initialized");
                return;
            }
            host.StartHost(port);
        }

        public static void ConnectHost(string ip = "localhost", int port = 4721)
        {
            if (client == null)
            {
                Plugin.Logger.LogWarning("NetworkSource not initialized");
                return;
            }
            client.ConnectHost(ip, port);
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
            DB.peerInfos.Remove(id);
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
                if (DB.partyInfos.TryGetValue(pair.Value.Noel.PartyID, out var party))
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
                        ins.Noel.PartyID = info.PartyID;
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
            Map2d map = DB.MainPR.NM2D.Get(key);
            M2LpMapTransferBase.executeTransferFastTravel(map, (int)x, (int)y);
        }

        public static void Kick(int id)
        {
            if (!PeerDic.TryGetValue(id, out NetPeer target))
            {
                return;
            }
            NetDataWriter writer = new();
            writer.Put(true);
            target.Disconnect(writer);
            PeerDic.Remove(id);
        }

        public static void Mute(int id)
        {
            host.SendMute(id);
        }

        public static void ToggleMute()
        {
            DB.Mute = !DB.Mute;
        }


    public static void CleanUp()
        {
            host = null;
            client = null;
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

