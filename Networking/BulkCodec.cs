using System;

namespace PolarisNoels.Networking
{
    /// <summary>Bulk 通道载荷前缀。首字节区分「存档包」与「游戏大包(protobuf)」，避免两条业务流混淆。</summary>
    public static class BulkCodec
    {
        public const byte KindSaveArchive = 1;

        public const byte KindGameMessage = 2;

        public static byte[] Wrap(byte kind, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            byte[] result = new byte[payload.Length + 1];
            result[0] = kind;
            Buffer.BlockCopy(payload, 0, result, 1, payload.Length);
            return result;
        }

        public static bool TryUnwrap(byte[] data, out byte kind, out byte[] payload)
        {
            kind = 0;
            payload = null;
            if (data == null || data.Length < 1)
            {
                return false;
            }
            kind = data[0];
            payload = new byte[data.Length - 1];
            Buffer.BlockCopy(data, 1, payload, 0, payload.Length);
            return true;
        }
    }
}
