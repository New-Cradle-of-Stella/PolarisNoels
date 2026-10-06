using LiteNetLib;
using System.IO;
using System.IO.Compression;

namespace WeNeedMoreNoels.Networking
{
    /// <summary>针对高延迟网络的 LiteNetLib 参数。所有 NetManager 创建后都应调用 <see cref="Apply"/>。</summary>
    public static class NetTuning
    {
        public static void Apply(NetManager manager)
        {
            // 默认 5 秒无响应就断线；高延迟 / 丢包时太容易误判
            manager.DisconnectTimeout = 20000;
            // 连接请求每 500ms 重试一次，最多 20 次（共约 10 秒）
            manager.ReconnectDelay = 500;
            manager.MaxConnectAttempts = 20;
            manager.PingInterval = 1000;
        }
    }

    /// <summary>
    /// 同步存档的传输打包。存档可能有数 MB，且可靠通道的发送窗口很小（64 包），
    /// 高延迟下传输会非常慢，所以先压缩。首字节标记是否压缩。
    /// </summary>
    public static class SaveTransfer
    {
        const byte Raw = 0;
        const byte Gzip = 1;

        public static byte[] Pack(byte[] data)
        {
            using MemoryStream compressed = new();
            compressed.WriteByte(Gzip);
            using (GZipStream gzip = new(compressed, CompressionMode.Compress, true))
            {
                gzip.Write(data, 0, data.Length);
            }
            if (compressed.Length < data.Length + 1)
            {
                return compressed.ToArray();
            }
            byte[] raw = new byte[data.Length + 1];
            raw[0] = Raw;
            System.Buffer.BlockCopy(data, 0, raw, 1, data.Length);
            return raw;
        }

        public static byte[] Unpack(byte[] packed)
        {
            if (packed.Length == 0 || packed[0] == Raw)
            {
                byte[] raw = new byte[System.Math.Max(0, packed.Length - 1)];
                System.Buffer.BlockCopy(packed, 1, raw, 0, raw.Length);
                return raw;
            }
            using MemoryStream source = new(packed, 1, packed.Length - 1);
            using GZipStream gzip = new(source, CompressionMode.Decompress);
            using MemoryStream result = new();
            gzip.CopyTo(result);
            return result.ToArray();
        }
    }
}
