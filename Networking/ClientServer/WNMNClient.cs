using LiteNetLib;
using LiteNetLib.Utils;
using nel;
using Newtonsoft.Json;
using System.Linq;
using UnityEngine;
using WeNeedMoreNoels.SN;

namespace WeNeedMoreNoels.CSNetworking
{
    public class WNMNClient : MonoBehaviour
    {
        NetManager client;

        NetPeer hostPeer;

        public int peerID;

        private void Awake()
        {
            EventBasedNetListener listener = new();
            client = new(listener);
            listener.PeerConnectedEvent += Listener_PeerConnectedEvent;
            listener.NetworkReceiveEvent += Listener_NetworkReceiveEvent;
            listener.PeerDisconnectedEvent += Listener_PeerDisconnectedEvent;
        }

        private void Update()
        {
            client?.PollEvents();
        }

        private void Listener_PeerConnectedEvent(NetPeer peer)
        {
            hostPeer = peer;
        }

        public void ConnectHost(string ip = "localhost", int port = 4721)
        {
            client.Start();
            client.Connect(ip, port, DB.CONNECTION_ACCESS_KEY);
        }

        private void Listener_NetworkReceiveEvent(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            Plugin.Logger.LogInfo(peer.EndPoint.Address.ToString());
            string json = reader.GetString();
            WNMNHostMessage message = JsonConvert.DeserializeObject<WNMNHostMessage>(json);
            reader.Recycle();
            if (message.MutePlayer)
            {
                WNMNTools.ToggleMute();
                return;
            }
            peerID = message.InitID;
            WNMNTools.LocalID = message.InitID;
            WNMNTools.SimBattleSyncHost = message.SyncHost;
            WNMNTools.SimBattleSyncList.Clear();
            if (message.SyncConnectedList != null)
            {
                WNMNTools.SimBattleSyncList.AddRange(message.SyncConnectedList);
            }
            DB.LocalNoelParty = message.InitID;
            DB.partyInfos = message.PeerParties.Select(x => x.Value).ToDictionary(x => x.ID);
            PartyManager.Party party = PartyManager.InitNewParty(message.InitID);
            DB.partyInfos[message.InitID] = party;
            WNMNTools.LocalIP = message.ClientIP;
            WNMNTools.ConnectOtherPeer(message.PeerInfos, peer, message.HostPort);
            WNMNTools.GenerateAllNoels(message.PeerConfigs);
            NetDataWriter writer = new();
            WNMNClientMessage message1 = new()
            {
                ID = peerID,
                HostIP = peer.EndPoint.Address.ToString(),
                Port = WNMNTools.peer.GetPeerPort(),
                NickName = DB.InitConfig.nickName,
                NoelType = DB.InitConfig.NoelType,
                Party = party
            };
            writer.Put(JsonConvert.SerializeObject(message1));
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo("client sent message");
            LocalNickname.Apply();
            WNMNTools.SetAllNickNameBgs();
        }

        private void Listener_PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (disconnectInfo.AdditionalData.AvailableBytes != 0 && disconnectInfo.AdditionalData.GetBool())
            {
                DB.WNMNHostKicked = true;
            }
            DB.WNMNHostClosed = true;
            if (DB.MainPR != null)
            {
                ((NelM2DBase)DB.MainPR.M2D).quitGame("SceneTitle");
            }
        }

        private void OnDestroy()
        {
            if (client == null)
            {
                return;
            }
            if (hostPeer != null)
            {
                NetDataWriter writer = new();
                writer.Put(peerID);
                client.DisconnectPeer(hostPeer, writer);
            }
            client.Stop();
        }
    }
}
