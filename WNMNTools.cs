using LiteNetLib;
using LiteNetLib.Utils;
using m2d;
using nel;
using nel.mgm.smncr;
using PixelLiner.PixelLinerLib;
using ProtoBuf;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WeNeedMoreNoels.CSNetworking;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.Networking;
using WeNeedMoreNoels.SN;
using XX;

namespace WeNeedMoreNoels
{
    public static class WNMNTools
    {
        static WNMNHost host;
        static WNMNClient client;

        public static WNMNPeer peer;

        public static NetWorkType Type;
        public static int LocalID = -1;
        public static bool Inited;
        public static bool PeerInited;
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

        public static aBtn USBCB;

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
            Inited = true;
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

        public static void SendInitToAllPeers(int id)
        {
            if (peer == null || !DB.partyInfos.TryGetValue(LocalID, out var localParty))
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.InitNoel,
                PeerId = id,
                InitNoelConfig = new()
                {
                    Id = id,
                    ClientConfig = DB.InitConfig,
                    PartyConfig = new()
                    {
                        ID = LocalID,
                        A = DB.partyInfos[LocalID].Color.a,
                        R = DB.partyInfos[LocalID].Color.r,
                        G = DB.partyInfos[LocalID].Color.g,
                        B = DB.partyInfos[LocalID].Color.b,
                        Name = DB.partyInfos[LocalID].Name
                    }
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendUpdateToAllPeers(int id)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.UpdateNoelInfo,
                PeerId = id,
                UpdateNoelInfo = ShadowNoelExtensions.GetSendInfo()
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendDamageToAllPeers(int id, NotifyNoelDamage dmg)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelDamage,
                PeerId = id,
                NotifyNoelDamage = dmg
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendMagicToAllPeers(int id, NotifyNoelMagic mg)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelMagic,
                PeerId = id,
                NotifyNoelMagic = mg
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendBattleStartToAllPeers(string key, int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelStartBattle,
                PeerId = id,
                Battle = new()
                {
                    key = key,
                    isSim = false
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }
        

        public static void SendSimBattleStartToAllPeers(int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelStartBattle,
                PeerId = id,
                Battle = new()
                {
                    isSim = true,
                    SpawnPoints = SpawnDic.Select(x => (x.Key, (DataStruct.Vector2Int)x.Value)).ToDictionary(x => x.Key, x => x.Item2)
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendBattleEndToAllPeers(string key, int id)
        {
            if (DB.InitConfig is null)
            {
                return;
            }
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelEndBattle,
                PeerId = id,
                Battle = new()
                {
                    key = key
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendUpdatePeerInfoToAllPeers(int id, string nickname)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.UpdatePeerInfo,
                PeerId = id,
                UpdatePeerInfo = new()
                {
                    Type = UpdatePeerType.Nickname,
                    NickName = nickname
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendUpdatePeerInfoToAllPeers(int id, int party)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.UpdatePeerInfo,
                PeerId = id,
                UpdatePeerInfo = new()
                {
                    Type = UpdatePeerType.Party,
                    PartyID = party
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendNotifyNoelTransferToAllPeers(int id)
        {
            DB.MainPR.getPosition(out float x, out float y);
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyNoelTransfer,
                PeerId = id,
                NotifyNoelTransfer = new()
                {
                    Key = DB.MainPR.Mp.key,
                    X = x,
                    Y = y
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendNotifyShortMsgToAllPeers(int id, string key)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyShortMsg,
                PeerId = id,
                NotifyShortMsg = new()
                {
                    ID = id,
                    key = key
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendGetItemToAllPeers(string key, int count, int grade)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyGetItem,
                PeerId = LocalID,
                NotifyItemChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    key = key,
                    count = count,
                    grade = grade
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendLoseItemToAllPeers(string key, int count, int grade)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyLoseItem,
                PeerId = LocalID,
                NotifyItemChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    key = key,
                    count = count,
                    grade = grade
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendGetCoinToAllPeers(CoinStorage.CTYPE type, int count)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyGetCoin,
                PeerId = LocalID,
                NotifyCoinChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    coinType = type,
                    count = count,
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendLoseCoinToAllPeers(CoinStorage.CTYPE type, int count)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyLoseCoin,
                PeerId = LocalID,
                NotifyCoinChanged = new()
                {
                    PartyID = DB.LocalNoelParty,
                    coinType = type,
                    count = count,
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendNotifyEnemySummonToAllPeers(string enm_id, int sync_id, bool isBoss = false)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyEnemyUpdate,
                PeerId = LocalID,
                NotifyEnemyUpdate = new()
                {
                    SyncID = sync_id,
                    Type = NotifyEnemyType.Summon,
                    Summon = new()
                    {
                        Key = enm_id,
                        isBoss = isBoss
                    }
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendNotifySimBattleToAllPeers(SimBattle battle)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifySimBattle,
                PeerId = LocalID,
                SimBattle = battle
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendSimBattleSync(int id)
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifySimBattleSync,
                PeerId = id,
                SyncSimBattle = new()
                {
                    SyncID = LocalID
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void SendBackSimBattleSyncData(int id)
        {
            ByteArray array = new(0U);
            array.writeMultiByte("tigrina chan no hutomomo tyokkei 440m by hashinomizuha", "utf-8");
            array.writeByte(13);
            USC.CurFile.writeBinaryTo(array);
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifySimBattleSync,
                PeerId = id,
                SyncSimBattle = new()
                {
                    SyncID = -1,
                    SyncSimBattleData = array.bytes
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo($"Transfered sync smncFile to peer:{id}");
        }


        public static void UpdateRoomConfigToAllPeers()
        {
            WNMNPeerMessage messageSend = new()
            {
                Type = WNMNPeerMessageType.NotifyRoomUpdate,
                PeerId = LocalID,
                NotifyRoomUpdate = new()
                {
                    EnablePVP = EnablePVP,
                    SyncType = SyncType
                }
            };
            using MemoryStream stream = new();
            Serializer.Serialize(stream, messageSend);
            byte[] buffer = stream.ToArray();
            peer.SendToAll(buffer, DeliveryMethod.ReliableOrdered);
        }

        public static void UpdateRoomConfig(NotifyRoomUpdate update)
        {
            EnablePVP = update.EnablePVP;
            SyncType = update.SyncType;
        }

        public static void CleanUpClient(int id)
        {
            ShadowNoelExtensions.DisableShadowNoel(id);
            DB.noelIns.Remove(id);
            DB.partyInfos.Remove(id);
            DB.peerInfos.Remove(id);
            DB.peerConfigs.Remove(id);
            DB.peerDelays.Remove(id);
            CleanUpPeerEnemy(id);
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

        public static void BroadcastMsg(string id)
        {
            SendMsg(LocalID, id);
            SendNotifyShortMsgToAllPeers(LocalID, id);
        }

        public static void SendMsg(int id, string txtID)
        {
            if (id == LocalID)
            {
                DB.MainPRMsg.ShowMsg(txtID);
            }
            else
            {
                if (DB.noelIns.TryGetValue(id, out var ins))
                {
                    ins.Noel.MsgIns?.ShowMsg(txtID);
                }
            }
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

        public static void NotifyFireBallTurn(MagicItem Mg, M2MagicCaster _Mv)
        {
            if (_Mv is not PRNoel)
            {
                return;
            }
            NotifyNoelMagic mg = new()
            {
                Type = NotifyMagicTpe.Turn,
                agR = Mg.da
            };
            SendMagicToAllPeers(LocalID, mg);
        }

        public static void NotifyEnemyUpdate(NotifyEnemyUpdate update, int peerID)
        {
            switch (update.Type)
            {
                case NotifyEnemyType.Summon:
                    NotifySummonEnemy(update.Summon.Key, update.SyncID, peerID, update.Summon.isBoss);
                    break;
                case NotifyEnemyType.InfoUpdate:
                    UpdateEnemyInfo(update.SyncID, update.Info);
                    break;
                case NotifyEnemyType.Dead:
                    CleanUpEnemy(update.SyncID);
                    break;
                case NotifyEnemyType.NotifyDamage:
                    NotifyEnemyDamage(update.SyncID, update.Damage);
                    break;
            }
        }

        public static void NotifySummonEnemy(string key, int sync_id, int peer_id, bool isBoss)
        {
            if (isBoss)
            {
                if (BattleStarterID != LocalID)
                {
                    GameObject gameObject = new();
                    var sideCar = gameObject.AddComponent<AddSyncSideCar>();
                    sideCar.syncID = sync_id;
                    sideCar.peerID = peer_id;
                }
            }
            else
            {
                NelEnemy nelEnemy = NDAT.createByKey(DB.MainPR.Mp, key, "-Summonned-" + DB.MainPR.Mp.key + "-{sync}" + sync_id.ToString());
                DB.MainPR.Mp.assignMover(nelEnemy);
                DB.CurEnemies.Add(nelEnemy);
                EnemySynchronizerSyncClient client = nelEnemy.gameObject.AddComponent<EnemySynchronizerSyncClient>();
                client.SyncID = sync_id;
                client.PeerID = peer_id;
                DB.SyncClients[sync_id] = client;
                if (!DB.peerClients.ContainsKey(peer_id))
                {
                    DB.peerClients.Add(peer_id, []);
                }
                DB.peerClients[peer_id].Add(client);
            }
        }

        public static void NotifySimBattle(int id, SimBattle sim)
        {
            switch (sim.Type)
            {
                case NotifySimBattleType.StartHost:
                    SimBattleSyncHost = id;
                    break;
                case NotifySimBattleType.CloseHost:
                    UiMenuMul.BxSB?.deactivate();
                    SimBattleSyncHost = -1;
                    if (USC != null && (int)USC.state == 9)
                    {
                        USC.changeState(UiSmnCreator.STATE.FILESEL);
                    }
                    SimBattleSyncList.Clear();
                    SimBattleReadyList.Clear();
                    break;
                case NotifySimBattleType.ConnectHost:
                    SimBattleSyncList.Add(id);
                    UpdateSimUI?.Invoke();
                    break;
                case NotifySimBattleType.DisconnectHost:
                    SimBattleSyncList.Remove(id);
                    SimBattleReadyList.Remove(id);
                    UpdateSimUI?.Invoke();
                    break;
                case NotifySimBattleType.ReadyHost:
                    SimBattleReadyList.Add(id);
                    UpdateSimUI?.Invoke();
                    break;
                case NotifySimBattleType.UnreadyHost:
                    SimBattleReadyList.Remove(id);
                    UpdateSimUI?.Invoke();
                    break;
            }
        }

        public static void UpdateEnemyInfo(int syncID, UpdateEnemyInfo info)
        {
            if (!DB.SyncClients.ContainsKey(syncID))
            {
                return;
            }
            DB.SyncClients[syncID].UpdateEnemyInfo(info);
        }

        public static void CleanUpEnemy(int syncID)
        {
            if (!DB.SyncClients.TryGetValue(syncID, out EnemySynchronizerSyncClient client) || client == null)
            {
                return;
            }
            if (client.Alive)
            {
                NelEnemy enemy = client.GetComponent<NelEnemy>();
                DB.MainPR.Mp.removeMover(enemy);
                enemy.destruct();
                Object.DestroyImmediate(enemy);
            }
        }

        public static void NotifyEnemyDamage(int syncID, NotifyEnemyDamage damage)
        {
            if (DB.SyncHosts.TryGetValue(syncID, out var syncHost) && syncHost != null)
            {
                syncHost.DamageEnemy(damage.hp, damage.mp);
            }
        }

        public static bool HasSyncEnemy()
        {
            return DB.SyncClients.Select(x => x.Value != null).Any(x => x == true);
        }

        public static int GetBattleNoelCounts(M2LpSummon summon)
        {
            return DB.noelIns.Where(x => x.Value.Enabled).Select(x => x.Value.Noel.IsNearLpSummon(summon)).Count(x => x) + 1;
        }

        public static void CleanUpPeerEnemy(int peerID)
        {
            if (!DB.peerClients.ContainsKey(peerID))
            {
                return;
            }
            List<EnemySynchronizerSyncClient> list = DB.peerClients[peerID];
            foreach (EnemySynchronizerSyncClient client in list)
            {
                CleanUpEnemy(client.SyncID);
            }
            list.Clear();
            DB.peerClients.Remove(peerID);
        }

        public static void CheckEnemyEmptyAndEndBattle()
        {
            if (DB.IsInBattle && !HasSyncEnemy() && Time.time - BattleStartT > 1f)
            {
                ShadowNoelExtensions.EndCurMapBattle();
            }
        }

        public static void OpenSmncBattle()
        {
            if (USBC is null)
            {
                USC.changeState(UiSmnCreator.STATE.BATTLE_CONFIRM);
                USBC = USC.BattleConfirm;
                USC.changeState((UiSmnCreator.STATE)9);
            }
            SmncStageEditorManager.StgObject stg = CurSimFile.Astgo[0];
            stg.x = SpawnDic.ContainsKey(LocalID) ? (int)SpawnDic[LocalID].x : (int)SpawnDic[-1].x;
            stg.y = SpawnDic.ContainsKey(LocalID) ? (int)SpawnDic[LocalID].y : (int)SpawnDic[-1].y;
            CurSimFile.Astgo[0] = stg;
            USBC.CurFile = CurSimFile;
			USBC.Record();
			uint num;
			int num2;
			if (USBC.UiDg != null)
			{
				num = USBC.CurFile.weather_bits;
				num2 = (int)USBC.CurFile.dangerousness;
			}
			else
			{
				num = 0U;
				num2 = 0;
				USBC.CurFile.fix_nattr = false;
			}
            if (USBC.LpArea.summoner_openable)
            {
                USBC.CurFile.use_seed = (USBC.decline_manage_danger ? 0U : USBC.CurFile.rand_seed);
                if (USBC.CurFile.use_seed != 0U)
                {
                    USBC.CurFile.pre_seed = USBC.CurFile.use_seed;
                    USBC.CurFile.use_seed ^= 3413251945U;
                }
                else
                {
                    USBC.CurFile.use_seed = X.xors();
                    USBC.CurFile.pre_seed = USBC.CurFile.use_seed ^ 3413251945U;
                }
                SND.Ui.play("enter", false);
                if (USBC.LpArea.auto_save_on_opening_summoner && CFG.autosave_on_scenario)
                {
                    COOK.autoSave(USBC.LpArea.nM2D, false, false);
                }
                if (USBC.BChkRestore != null && USBC.LpArea.restore_items > 0 && USBC.BChkRestore.isChecked())
                {
                    GF.setB("SMNC_RESTORE_ITEMS", true);
                }
                USBC.LpArea.Reader.fatal_key = (TX.noe(USBC.Con.fatal_key) || !USBC.fatal_playable) ? null : USBC.Con.fatal_key;
                USBC.LpArea.openSummoner(num2, num);
                DB.CurSummoner = USBC.LpArea;
                USBC?.FD_BattleConfirm(num2, num);
            }
        }

        public static void ResumeUSBCPage()
        {
            SSE.changeState(SmncStageEditor.STATE.OFFLINE);
            USC.changeState((UiSmnCreator.STATE)9);
            USBC.Bx.deactivate();
            UiMenuMul.BxSB.activate();
            UiMenuMul.BxSB.Focus();
            if (!SpawnDic.ContainsKey(CurrentSetID))
            {
                SpawnDic.Add(CurrentSetID, SettingResult);
            }
            else
            {
                SpawnDic[CurrentSetID] = SettingResult;
            }
            UpdateSimUI?.Invoke();
        }

        public static Color GetOutlineColor(Color fillColor, float shift = 0.2f)
        {
            Color.RGBToHSV(fillColor, out float h, out float s, out float v);
            if (v > 0.5f)
            {
                v -= shift;
            }
            else
            {
                v += shift;
            }
            v = Mathf.Clamp01(v);
            return Color.HSVToRGB(h, s, v);
        }

    public static void CleanUp()
        {
            host = null;
            client = null;
            Inited = false;
            LocalID = -1;
            peer = null;
            PeerIngameInited = false;
            USC = null;
            USBC = null;
            USBCB = null;
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
