using System.Text;
using Newtonsoft.Json;

namespace PolarisNoels.CSNetworking
{
    /// <summary>
    /// 业务握手信封：magic "PNJ1" + 1 字节类型 + UTF-8 JSON。
    /// 之所以要有 magic：握手 JSON 与游戏 protobuf 消息共用 QUIC 的 Reliable 通道，
    /// protobuf 的 PolarisNoelsPeerMessage 首字节固定是字段 tag(0x08)，不可能与 ASCII "PNJ1" 冲突。
    /// </summary>
    public static class HandshakeCodec
    {
        public static readonly byte[] Magic = [(byte)'P', (byte)'N', (byte)'J', (byte)'1'];

        public const byte KindHostMessage = 1;

        public const byte KindClientMessage = 2;

        public static bool HasMagic(byte[] data)
        {
            if (data == null || data.Length < Magic.Length + 1)
            {
                return false;
            }
            for (int i = 0; i < Magic.Length; i++)
            {
                if (data[i] != Magic[i]) return false;
            }
            return true;
        }

        public static byte[] Encode(byte kind, object payload)
        {
            byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            byte[] result = new byte[Magic.Length + 1 + json.Length];
            System.Buffer.BlockCopy(Magic, 0, result, 0, Magic.Length);
            result[Magic.Length] = kind;
            System.Buffer.BlockCopy(json, 0, result, Magic.Length + 1, json.Length);
            return result;
        }

        public static bool TryDecode(byte[] data, out byte kind, out string json)
        {
            kind = 0;
            json = null;
            if (!HasMagic(data))
            {
                return false;
            }
            kind = data[Magic.Length];
            json = Encoding.UTF8.GetString(data, Magic.Length + 1, data.Length - Magic.Length - 1);
            return true;
        }

        public static bool TryDecode<T>(byte[] data, byte expectedKind, out T payload)
        {
            payload = default;
            if (!TryDecode(data, out byte kind, out string json) || kind != expectedKind)
            {
                return false;
            }
            payload = JsonConvert.DeserializeObject<T>(json);
            return payload != null;
        }
    }
}
