using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;
using UnityEngine;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.CSNetworking
{
    public class WNMNHost : MonoBehaviour
    {
        NetManager host;

        NetManager transferHost;

        private void Awake()
        {
            EventBasedNetListener listener = new();
            host = new(listener);
            listener.ConnectionRequestEvent += Listener_ConnectionRequestEvent;
            listener.PeerConnectedEvent += Listener_PeerConnectedEvent;
            listener.NetworkReceiveEvent += Listener_NetworkReceiveEvent;
            EventBasedNetListener transferListener = new();
            transferHost = new(transferListener);
            transferListener.ConnectionRequestEvent += TransferListener_ConnectionRequestEvent;
            transferListener.PeerConnectedEvent += TransferListener_PeerConnectedEvent;
            WNMNTools.LocalID = 0;
            PartyManager.Party party = PartyManager.InitNewParty(0);
            DB.partyInfos.Add(0, party);
            LocalNickname.Apply();
            WNMNTools.SetAllNickNameBgs();
        }

        private void Update()
        {
            host?.PollEvents();
            transferHost?.PollEvents();
        }

        public void StartHost(int port = 47210)
        {
            if (host is null)
            {
                Plugin.Logger.LogWarning("WNMNHost not initialized!");
                return;
            }
            host.Start(port);
            Plugin.Logger.LogInfo($"WNMNHost started, port:{port}");
            transferHost.Start(port + 1);
            Plugin.Logger.LogInfo($"WNMN Sync transfer host started, port:{port + 1}");
        }

        public void SendMute(int id)
        {
            WNMNHostMessage message = new()
            {
                MutePlayer = true,
                PlayerID = id
            };
            NetDataWriter writer = new();
            writer.Put(JsonConvert.SerializeObject(message));
            if (WNMNTools.PeerDic.TryGetValue(id, out NetPeer target))
            {
                target.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        private void TransferListener_ConnectionRequestEvent(ConnectionRequest request)
        {
            Plugin.Logger.LogInfo($"Transfer host got request: {request.RemoteEndPoint}");
            if (host.ConnectedPeersCount < DB.MaxPlayerCount - 1 /* max connections */)
                request.AcceptIfKey(DB.TRANSFER_ACCESS_KEY);
            else
                request.Reject();
        }

        private void Listener_ConnectionRequestEvent(ConnectionRequest request)
        {
            Plugin.Logger.LogInfo($"Host got request: {request.RemoteEndPoint}");
            if (host.ConnectedPeersCount < DB.MaxPlayerCount - 1 /* max connections */)
                request.AcceptIfKey(DB.CONNECTION_ACCESS_KEY);
            else
                request.Reject();
        }

        private void TransferListener_PeerConnectedEvent(NetPeer peer)
        {
            Plugin.Logger.LogInfo($"WNMNTransfer host got connection: {peer.EndPoint}");
            NetDataWriter writer = new();
            writer.Put(DB.SyncSaveContentBuffer);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo("WNMNTransfer host transfered map data");
        }

        private void Listener_PeerConnectedEvent(NetPeer peer)
        {
            Plugin.Logger.LogInfo($"WNMNMain host got connection: {peer.EndPoint}");
            NetDataWriter writer = new();
            int id = WNMNTools.Unique_ID;
            WNMNHostMessage message = new()
            {
                ClientIP = peer.EndPoint.Address.ToString(),
                HostPort = WNMNTools.peer.GetPeerPort(),
                InitID = id,
                PeerInfos = [.. DB.peerInfos],
                PeerConfigs = [.. DB.peerConfigs],
                PeerParties = [.. DB.partyInfos],
                SyncHost = WNMNTools.SimBattleSyncHost,
                SyncConnectedList = WNMNTools.SimBattleSyncList
            };
            writer.Put(JsonConvert.SerializeObject(message));
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
            WNMNTools.PeerDic[id] = peer;
        }

        private void Listener_NetworkReceiveEvent(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            Plugin.Logger.LogInfo("host got message");
            string json = reader.GetString();
            reader.Recycle();
            WNMNClientMessage message = JsonConvert.DeserializeObject<WNMNClientMessage>(json);
            WNMNTools.LocalIP = message.HostIP;
            if (!DB.peerInfos.ContainsKey(0))
            {
                DB.peerInfos.Add(0, new()
                {
                    IP = WNMNTools.LocalIP,
                    Port = WNMNTools.peer.GetPeerPort()
                });
            }
            ConnectPeerInfo info = new()
            {
                IP = peer.EndPoint.Address.ToString(),
                Port = message.Port
            };
            DB.peerInfos[message.ID] = info;
            ClientConfig config = new()
            {
                Nickname = message.NickName,
                NoelType = message.NoelType,
                NoelColor = message.NoelColor
            };
            DB.peerConfigs[message.ID] = config;
        }

        private void OnDestroy()
        {
            host?.DisconnectAll();
            host?.Stop();
            transferHost?.Stop();
            DB.InitConfig = null;
        }
    }
}
