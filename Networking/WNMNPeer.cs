using LiteNetLib;
using LiteNetLib.Utils;
using ProtoBuf;
using System.IO;
using UnityEngine;
using WeNeedMoreNoels.DataStruct;

namespace WeNeedMoreNoels.Networking
{
    public class WNMNPeer : MonoBehaviour
    {
        NetManager localPeer;

        private void Awake()
        {
            EventBasedNetListener listener = new();
            localPeer = new(listener);
            listener.ConnectionRequestEvent += Listener_ConnectionRequestEvent;
            listener.PeerConnectedEvent += Listener_PeerConnectedEvent;
            listener.NetworkReceiveEvent += Listener_NetworkReceiveEvent;
            listener.PeerDisconnectedEvent += Listener_PeerDisconnectedEvent;
        }

        private void Listener_PeerConnectedEvent(NetPeer peer)
        {
            Plugin.Logger.LogInfo($"peer connected: {peer.EndPoint.ToString()}");
        }

        private void Update()
        {
            if (DB.MainPR == null || DB.MainPR.Mp == null)
            {
                return;
            }
            WNMNTools.SendUpdateToAllPeers(WNMNTools.LocalID);
            if (WNMNTools.Type == NetWorkType.Host)
            {
                WNMNTools.UpdateRoomConfigToAllPeers();
            }
            localPeer?.PollEvents();
            WNMNTools.UpdateAllNoels();
            WNMNTools.SetAllNickNameBgs();
            WNMNTools.CheckEnemyEmptyAndEndBattle();
        }

        private void Listener_ConnectionRequestEvent(ConnectionRequest request)
        {
            Plugin.Logger.LogInfo($"peer got request: {request.RemoteEndPoint}");
            request.AcceptIfKey(DB.P2P_ACCESS_KEY);
        }

        private void Listener_NetworkReceiveEvent(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            byte[] receivedData = new byte[reader.UserDataSize];
            reader.GetBytes(receivedData, reader.UserDataSize);
            reader.Recycle();
            WNMNPeerMessage message;
            try
            {
                using MemoryStream stream = new(receivedData);
                message = Serializer.Deserialize<WNMNPeerMessage>(stream);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning($"bad peer message dropped: {e.Message}");
                return;
            }
            foreach (PeerReceiveMessageBase receive in ReceiveMessageManager.GetAllReceives(message))
            {
                try
                {
                    if (receive.CheckMessage(message))
                    {
                        receive.ReceiveMessage(message);
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Logger.LogError($"{receive.GetType().Name} failed: {e}");
                }
            }
            DB.peerDelays[message.PeerId] = peer.Ping;
        }

        private void Listener_PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (disconnectInfo.AdditionalData.AvailableBytes == 0)
            {
                return;
            }
            int id = disconnectInfo.AdditionalData.GetInt();
            WNMNTools.CleanUpClient(id);
        }

        public void SendToAll(byte[] content, DeliveryMethod delivery)
        {
            NetDataWriter writer = new();
            writer.Put(content);
            localPeer.SendToAll(writer, delivery);
        }

        public void ConnectPeer(string ip, int port)
        {
            Plugin.Logger.LogInfo($"peer connect {ip}:{port}");
            localPeer.Connect(ip, port, DB.P2P_ACCESS_KEY);
            WNMNTools.PeerInited = true;
        }

        public int StartPeer()
        {
            localPeer.Start();
            return GetPeerPort();
        }

        public int GetPeerPort()
        {
            return localPeer.LocalPort;
        }

        private void OnDestroy()
        {
            NetDataWriter writer = new();
            writer.Put(WNMNTools.LocalID);
            foreach (NetPeer peer in localPeer)
            {
                localPeer.DisconnectPeer(peer, writer);
            }
            localPeer.Stop();
            DB.InitConfig = null;
            DB.noelIns.Clear();
            DB.partyInfos.Clear();
            DB.peerInfos.Clear();
            DB.peerConfigs.Clear();
            DB.peerDelays.Clear();
            DB.CleanUp();
            WNMNTools.CleanUp();
        }
    }
}
