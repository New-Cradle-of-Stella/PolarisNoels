using nel;
using ProtoBuf;
using System.Collections.Generic;
using UnityEngine;

namespace PolarisNoels.DataStruct
{
    [ProtoContract]
    public class UpdateNoelInfo
    {
        [ProtoMember(1)]
        public float PositionX;
        [ProtoMember(2)]
        public float PositionY;
        [ProtoMember(3)]
        public bool IsCrouch;
        [ProtoMember(4)]
        public string Pose;
        [ProtoMember(5)]
        public int Aim;
        [ProtoMember(6)]
        public int Hp;
        [ProtoMember(7)]
        public int Mp;
        [ProtoMember(8)]
        public int State;
        [ProtoMember(9)]
        public int CaneItemId;
        [ProtoMember(10)]
        public int CaneGrade;
        [ProtoMember(11)]
        public int PartyID;
        [ProtoMember(12)]
        public string MpKey;
        [ProtoMember(13)]
        public bool ChantMagic;
        [ProtoMember(14)]
        public float MagicAgR;
        [ProtoMember(15)]
        public float MagicHold;
        [ProtoMember(16)]
        public float MagicT;
        [ProtoMember(17)]
        public int MagicHoldAim;
        [ProtoMember(18)]
        public bool IsEvadeO;
        [ProtoMember(19)]
        public float EvadeT;
        [ProtoMember(20)]
        public bool IsAtkO;
        [ProtoMember(21)]
        public float ShieldShiftX;
        [ProtoMember(22)]
        public float ShieldShiftY;
        [ProtoMember(23)]
        public float ShieldScale;
        [ProtoMember(24)]
        public float ShieldPow;
        [ProtoMember(25)]
        public int ShieldState;
        [ProtoMember(26)]
        public float HoldT;
    }

    [ProtoContract] 
    public class NotifyNoelDamage
    {
        [ProtoMember(1)]
        public int Hp;
        [ProtoMember(2)]
        public int Mp;
    }

    [ProtoContract]
    public class NotifyNoelMagic
    {
        [ProtoMember(1)]
        public NotifyMagicTpe Type;
        [ProtoMember(2)]
        public int Kind;
        [ProtoMember(3)]
        public float T;
        [ProtoMember(4)]
        public float agR;
        [ProtoMember(5)]
        public int id;
        [ProtoMember(6)]
        public string Key;
        [ProtoMember(7)]
        public int Grade;
        [ProtoMember(8)]
        public int Phase;
        [ProtoMember(9)]
        public float BombX;
        [ProtoMember(10)]
        public float BombY;
    }

    [ProtoContract]
    public enum NotifyMagicTpe
    {
        [ProtoEnum]
        Reawake,
        [ProtoEnum]
        Sleep,
        [ProtoEnum]
        Kill,
        [ProtoEnum]
        Turn,
        [ProtoEnum]
        WaterShoot,
        [ProtoEnum]
        InitBomb,
        [ProtoEnum]
        UpdateBomb,
        [ProtoEnum]
        RemoveBomb
    }

    [ProtoContract]
    public class UpdatePeerInfo
    {
        [ProtoMember(1)]
        public UpdatePeerType Type;
        [ProtoMember(2)]
        public string NickName;
        [ProtoMember(3)]
        public int PartyID;
        [ProtoMember(4)]
        public string MapKey;
        [ProtoMember(5)]
        public float PositionX;
        [ProtoMember(6)]
        public float PositionY;
    }

    [ProtoContract]
    public enum UpdatePeerType
    {
        Nickname,
        Party,
        Map
    }

    [ProtoContract]
    public class NotifyNoelTransfer
    {
        [ProtoMember(1)]
        public string Key;
        [ProtoMember(2)]
        public float X;
        [ProtoMember(3)]
        public float Y;
    }

    [ProtoContract]
    public class NotifyShortMsg
    {
        [ProtoMember(1)]
        public int ID;
        [ProtoMember(2)]
        public string key;
    }

    [ProtoContract]
    public class NotifyItemChanged
    {
        [ProtoMember(1)]
        public int PartyID;
        [ProtoMember(2)]
        public string key;
        [ProtoMember(3)]
        public int count;
        [ProtoMember(4)]
        public int grade;
    }

    [ProtoContract]
    public class NotifyCoinChanged
    {
        [ProtoMember(1)]
        public int PartyID;
        [ProtoMember(2)]
        public CoinStorage.CTYPE coinType;
        [ProtoMember(3)]
        public int count;
    }

    [ProtoContract]
    public class NotifyRoomUpdate
    {
        [ProtoMember(1)]
        public bool EnablePVP;
        [ProtoMember(2)]
        public EnemySyncType SyncType;
    }

    [ProtoContract]
    public class UpdateEnemyInfo
    {
        [ProtoMember(1)]
        public float PositionX;
        [ProtoMember(2)]
        public float PositionY;
        [ProtoMember(3)]
        public string Pose;
        [ProtoMember(4)]
        public int Aim;
        [ProtoMember(5)]
        public int Hp;
        [ProtoMember(6)]
        public int Mp;
        [ProtoMember(7)]
        public int State;
        [ProtoMember(8)]
        public float T;
    }

    [ProtoContract]
    public class BattleInfo
    {
        [ProtoMember(1)]
        public string key;
        [ProtoMember(2)]
        public bool isSim;
        [ProtoMember(3)]
        public Dictionary<int, Vector2Int> SpawnPoints;
    }

    [ProtoContract]
    public class Vector2Int
    {
        [ProtoMember(1)]
        public int x;
        [ProtoMember(2)]
        public int y;

        public static implicit operator Vector2Int(Vector2 v)
        {
            return new()
            {
                x = (int)v.x,
                y = (int)v.y
            };
        }
    }

    [ProtoContract]
    public class SimBattle
    {
        [ProtoMember(1)]
        public NotifySimBattleType Type;
    }

    [ProtoContract]
    public class SimBattleSync
    {
        [ProtoMember(1)]
        public int SyncID;
        [ProtoMember(2)]
        public byte[] SyncSimBattleData;
    }

    [ProtoContract]
    public enum NotifySimBattleType
    {
        [ProtoEnum]
        StartHost,
        [ProtoEnum]
        CloseHost,
        [ProtoEnum]
        ConnectHost,
        [ProtoEnum]
        DisconnectHost,
        [ProtoEnum]
        ReadyHost,
        [ProtoEnum]
        UnreadyHost
    }

}
