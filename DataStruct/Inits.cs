using ProtoBuf;

namespace PolarisNoels.DataStruct
{
    [ProtoContract]
    public class ClientConfig
    {
        [ProtoMember(1)]
        public string Nickname;
        [ProtoMember(2)]
        public NoelType NoelType;
        [ProtoMember(3)]
        public ColorNoelColor NoelColor;
    }

    [ProtoContract]
    public class PartyConfig
    {
        [ProtoMember(1)]
        public int ID;
        [ProtoMember(2)]
        public float R;
        [ProtoMember(3)]
        public float G;
        [ProtoMember(4)]
        public float B;
        [ProtoMember(5)]
        public float A;
        [ProtoMember(6)]
        public string Name;
    }

    [ProtoContract]
    public class IniConfig
    {
        [ProtoMember(1)]
        public int Id;
        [ProtoMember(2)]
        public ClientConfig ClientConfig;
        [ProtoMember(3)]
        public PartyConfig PartyConfig;
    }

    [ProtoContract]
    public enum NoelType
    {
        [ProtoEnum]
        Normal,
        [ProtoEnum]
        Inverse,
        [ProtoEnum]
        ColorNoel
    }

    [ProtoContract]
    public enum ColorNoelColor
    {
        [ProtoEnum]
        Red,
        [ProtoEnum]
        Orange,
        [ProtoEnum]
        Yellow,
        [ProtoEnum]
        Green,
        [ProtoEnum]
        Cyan,
        [ProtoEnum]
        Blue,
        [ProtoEnum]
        Purple,
        [ProtoEnum]
        Magenta
    }
}
