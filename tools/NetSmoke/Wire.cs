using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PolarisNoels.NetSmoke
{
    /// <summary>冒烟协议：控制消息是 UTF-8 JSON（首字节 '{'），二进制测试载荷是确定性模式。</summary>
    public static class Wire
    {
        public static byte[] Json(string text) => Encoding.UTF8.GetBytes(text);

        /// <summary>返回控制消息的 "t" 字段；不是 JSON 控制消息时返回 null。</summary>
        public static string Type(byte[] data)
        {
            if (data == null || data.Length == 0 || data[0] != (byte)'{')
            {
                return null;
            }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(data);
                return doc.RootElement.TryGetProperty("t", out JsonElement t) ? t.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static int GetInt(byte[] data, string key, int fallback = -1)
        {
            using JsonDocument doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty(key, out JsonElement value) ? value.GetInt32() : fallback;
        }

        public static string GetString(byte[] data, string key)
        {
            using JsonDocument doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty(key, out JsonElement value) ? value.GetString() : null;
        }

        public static bool GetBool(byte[] data, string key)
        {
            using JsonDocument doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.True;
        }

        public static byte[] Pattern(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = (byte)((i * 31 + 7) & 0xFF);
            }
            return data;
        }

        public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

        public static bool VerifyPattern(byte[] data, int expectedLength)
        {
            if (data == null || data.Length != expectedLength)
            {
                return false;
            }
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != (byte)((i * 31 + 7) & 0xFF))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
