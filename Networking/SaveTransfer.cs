using PolarisNoels.Networking;

namespace PolarisNoels
{
    /// <summary>
    /// 旧的 NetTuning 已随 LiteNetLib 删除；这里只保留存档打包（gzip），供原生 BULK 通道使用。
    /// 存档可能有数 MB，BULK 通道负责带宽，压缩只为了少传字节。
    /// 首字节标记是否压缩。
    /// </summary>
    public static class SaveTransfer
    {
        const byte Raw = 0;
        const byte Gzip = 1;

        public static byte[] Pack(byte[] data)
        {
            using System.IO.MemoryStream compressed = new();
            compressed.WriteByte(Gzip);
            using (System.IO.Compression.GZipStream gzip = new(compressed, System.IO.Compression.CompressionMode.Compress, true))
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
            const int limit = 64 * 1024 * 1024;
            if (packed == null || packed.Length == 0 || packed.Length > limit || (packed[0] != Raw && packed[0] != Gzip))
                throw new System.IO.InvalidDataException("invalid archive format or size");
            if (packed[0] == Raw)
            {
                int length = packed == null ? 0 : System.Math.Max(0, packed.Length - 1);
                byte[] raw = new byte[length];
                if (length > 0)
                {
                    System.Buffer.BlockCopy(packed, 1, raw, 0, length);
                }
                return raw;
            }
            using System.IO.MemoryStream source = new(packed, 1, packed.Length - 1);
            using System.IO.Compression.GZipStream gzip = new(source, System.IO.Compression.CompressionMode.Decompress);
            using System.IO.MemoryStream result = new();
            byte[] chunk = new byte[64 * 1024];
            int read;
            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (result.Length + read > limit) throw new System.IO.InvalidDataException("unpacked archive exceeds 64MB");
                result.Write(chunk, 0, read);
            }
            return result.ToArray();
        }
    }
}
