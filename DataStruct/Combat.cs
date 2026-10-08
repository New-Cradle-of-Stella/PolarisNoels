using ProtoBuf;
using System.Collections.Generic;

namespace PolarisNoels.DataStruct
{
    [ProtoContract]
    public enum CombatMode { [ProtoEnum] Attack, [ProtoEnum] Absorb, [ProtoEnum] Hp, [ProtoEnum] EnemyHpMp, [ProtoEnum] PlayerSimple, [ProtoEnum] Shield, [ProtoEnum] Mp }

    [ProtoContract]
    public enum CombatOutcome
    {
        [ProtoEnum] Applied, [ProtoEnum] NoDamage, [ProtoEnum] Stale,
        [ProtoEnum] OutOfRange, [ProtoEnum] Unavailable, [ProtoEnum] Invalid
    }

    // Only whitelisted value fields are transferred, never game objects or delegates.
    [ProtoContract]
    public class CombatAttack
    {
        [ProtoMember(1)] public Dictionary<string, double> Values = new();
        [ProtoMember(2)] public Dictionary<int, float> Status;
        [ProtoMember(3)] public float CenterX;
        [ProtoMember(4)] public float CenterY;
        [ProtoMember(5)] public float HitX;
        [ProtoMember(6)] public float HitY;
        [ProtoMember(7)] public float Radius;
        [ProtoMember(8)] public Dictionary<string, double> Magic;
        [ProtoMember(9)] public Dictionary<string, double> Ep;
        [ProtoMember(10)] public byte[] EpTargets;
        [ProtoMember(11)] public string EpKey;
        [ProtoMember(12)] public Dictionary<string, double> Beto;
        [ProtoMember(13)] public string HitEffect;
        [ProtoMember(14)] public string Sound;
        [ProtoMember(15)] public CombatRay Ray;
        [ProtoMember(16)] public int CasterKind;
        [ProtoMember(17)] public int MagicCasterKind;
        [ProtoMember(18)] public bool HasAttackFrom;
    }

    [ProtoContract]
    public class CombatRequest
    {
        [ProtoMember(1)] public long Id;
        [ProtoMember(2)] public string SenderEpoch;
        [ProtoMember(3)] public int TargetId;
        [ProtoMember(4)] public string TargetKey;
        [ProtoMember(5)] public string TargetType;
        [ProtoMember(6)] public string TargetLife;
        [ProtoMember(7)] public int SourceId;
        [ProtoMember(8)] public string SourceKey;
        [ProtoMember(9)] public string SourceType;
        [ProtoMember(10)] public string SourceLife;
        [ProtoMember(11)] public string MapKey;
        [ProtoMember(12)] public string SourceVisit;
        [ProtoMember(13)] public string TargetVisit;
        [ProtoMember(14)] public double ObservedTime;
        [ProtoMember(15)] public CombatMode Mode;
        [ProtoMember(16)] public CombatAttack Attack;
        [ProtoMember(17)] public bool Force;
        [ProtoMember(18)] public int Value;
        [ProtoMember(19)] public bool Execute = true;
        [ProtoMember(20)] public bool Mouth;
        [ProtoMember(21)] public string FadeKey;
        [ProtoMember(22)] public bool DeclineEffects;
        [ProtoMember(23)] public bool FromPress;
        [ProtoMember(24)] public int[] TargetPath;
        [ProtoMember(25)] public int[] SourcePath;
        [ProtoMember(26)] public int MpValue;
        [ProtoMember(27)] public bool ShowCounter = true;
        [ProtoMember(28)] public bool UseQuake;
        [ProtoMember(29)] public bool CalcGsaver;
        [ProtoMember(30)] public bool KeepGsaver;
        [ProtoMember(31)] public bool UseCushion = true;
    }

    [ProtoContract]
    public class CombatResult
    {
        [ProtoMember(1)] public long RequestId;
        [ProtoMember(2)] public int Requester;
        [ProtoMember(3)] public string SenderEpoch;
        [ProtoMember(4)] public int TargetId;
        [ProtoMember(5)] public string TargetKey;
        [ProtoMember(6)] public string TargetType;
        [ProtoMember(7)] public string MapKey;
        [ProtoMember(8)] public string TargetVisit;
        [ProtoMember(9)] public string Life;
        [ProtoMember(10)] public ulong Revision;
        [ProtoMember(11)] public double SampleTime;
        [ProtoMember(12)] public CombatOutcome Outcome;
        [ProtoMember(13)] public int Hp;
        [ProtoMember(14)] public int Mp;
        [ProtoMember(15)] public int HpDelta;
        [ProtoMember(16)] public int MpDelta;
        [ProtoMember(17)] public int ReturnValue;
        [ProtoMember(18)] public int HitType;
        [ProtoMember(19)] public EntityState Snapshot;
        [ProtoMember(20)] public bool MagicKilled;
        [ProtoMember(21)] public float X;
        [ProtoMember(22)] public float Y;
        [ProtoMember(23)] public int ReflectionHit;
        [ProtoMember(24)] public CombatRay Reflector;
        [ProtoMember(25)] public bool SourceParried;
        [ProtoMember(26)] public int ParryAim;
    }

    [ProtoContract]
    public class CombatPart
    {
        [ProtoMember(1)] public int[] Path;
        [ProtoMember(2)] public string Type;
        [ProtoMember(3)] public int Hp;
        [ProtoMember(4)] public int Mp;
    }
    [ProtoContract]
    public class CombatRay
    {
        [ProtoMember(1)] public float X;
        [ProtoMember(2)] public float Y;
        [ProtoMember(3)] public float DirectionX;
        [ProtoMember(4)] public float DirectionY;
        [ProtoMember(5)] public float Length;
        [ProtoMember(6)] public float Radius;
        [ProtoMember(7)] public int Power;
        [ProtoMember(8)] public int HitType;
        [ProtoMember(9)] public int WeakType;
        [ProtoMember(10)] public int MinimumPower;
    }
}
