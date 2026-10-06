using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PolarisNoels.Networking.Native
{
    /// <summary>ABI 常量。数值与 docs/native-network-layer.md §3.1 完全一致，改动需两边同步。</summary>
    public static class PnAbi
    {
        public const int Version = 1;

        public const int Ok = 0;
        public const int ErrInvalid = -1;
        public const int ErrState = -2;
        public const int ErrNotFound = -3;
        public const int ErrBuffer = -4;
        public const int ErrQueueFull = -5;
        public const int ErrInternal = -99;

        public const int ChReliable = 0;
        public const int ChSequenced = 1;
        public const int ChBulk = 2;

        public const int EvNone = 0;
        public const int EvPeerConnected = 1;
        public const int EvPeerDisconnected = 2;
        public const int EvMessage = 3;
        public const int EvBulkProgress = 4;
        public const int EvNatState = 5;
        public const int EvJoinResult = 6;
        public const int EvLog = 7;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PnConfig
    {
        public uint AbiVersion;
        public ushort BindPort;
        public ushort MaxPeers;
        public byte EnableStun;
        public byte EnableLan;
        public byte EnableUpnp;
        public byte Reserved0;
        public IntPtr StunServers;
        public uint IdleTimeoutMs;
        public uint Reserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PnEvent
    {
        public int Type;
        public int Peer;
        public int Channel;
        public int Code;
        public uint Len;
        public uint Aux0;
        public uint Aux1;
        public uint Aux2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PnPeerStats
    {
        public uint RttMs;
        public uint LossPermille;
        public ulong BytesSent;
        public ulong BytesRecv;
        public uint SendQueueBytes;
        public byte PathKind;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
    }

    /// <summary>
    /// 手写 P/Invoke（依据 §3.1 头文件）。调用约定 cdecl，DLL 名由 pn_* 未修饰符号导出。
    /// </summary>
    public static class PnNative
    {
        public const string DllName = "polaris_net";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_abi_version();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_init(ref PnConfig cfg);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void pn_shutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_host_start();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int pn_room_code(byte* buffer, uint cap, uint* outLen);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int pn_join(byte* roomCode, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_join_direct(string host, ushort port, IntPtr fingerprint32);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int pn_send(int peer, int channel, byte* data, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_disconnect(int peer, int reason);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int pn_poll(PnEvent* ev, byte* buffer, uint cap);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_peer_stats(int peer, out PnPeerStats stats);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_local_peer_id();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int pn_peer_count();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int pn_last_error(byte* buffer, uint cap, uint* outLen);
    }

    /// <summary>
    /// 原生库加载。插件目录不在 Windows 默认 DLL 搜索路径里，必须先按绝对路径 LoadLibraryW 预加载：
    /// 之后的 DllImport("polaris_net") 会命中进程里已加载的同名模块。
    /// 刻意不调用 SetDllDirectoryW —— 那会改变整个游戏进程后续所有 DLL 的搜索行为。
    /// </summary>
    public static class PnNativeLoader
    {
        public const string DllFileName = "polaris_net.dll";

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        private static IntPtr handle;
        private static string loadedPath;

        public static string LoadedPath => loadedPath;

        public static bool IsLoaded => handle != IntPtr.Zero;

        /// <summary>加载并校验 ABI。失败返回 false 并给出可诊断文本（不抛异常）。</summary>
        public static bool TryLoad(string directory, out string error)
        {
            error = null;
            if (IsLoaded)
            {
                return true;
            }
            if (string.IsNullOrEmpty(directory))
            {
                error = "plugin directory is unknown";
                return false;
            }
            string path = Path.Combine(directory, DllFileName);
            if (!File.Exists(path))
            {
                error = $"missing native library: {path} (run cargo build --release in native/polaris_net)";
                return false;
            }
            try
            {
                handle = LoadLibraryW(path);
            }
            catch (Exception e)
            {
                error = $"LoadLibraryW failed: {e.Message}";
                return false;
            }
            if (handle == IntPtr.Zero)
            {
                error = $"LoadLibraryW({path}) failed, win32 error {Marshal.GetLastWin32Error()}";
                return false;
            }
            int version;
            try
            {
                version = PnNative.pn_abi_version();
            }
            catch (Exception e)
            {
                FreeLibrary(handle);
                handle = IntPtr.Zero;
                error = $"pn_abi_version call failed: {e.Message}";
                return false;
            }
            if (version != PnAbi.Version)
            {
                FreeLibrary(handle);
                handle = IntPtr.Zero;
                error = $"ABI mismatch: library={version}, managed={PnAbi.Version}";
                return false;
            }
            loadedPath = path;
            return true;
        }

        /// <summary>按 fileName 在若干候选目录里查找（游戏插件目录 / 当前目录 / NetSmoke 输出目录）。</summary>
        public static bool TryLoadFromCandidates(string[] directories, out string error)
        {
            error = null;
            foreach (string directory in directories)
            {
                if (string.IsNullOrEmpty(directory)) continue;
                if (!File.Exists(Path.Combine(directory, DllFileName))) continue;
                return TryLoad(directory, out error);
            }
            error = $"missing native library {DllFileName} in: {string.Join("; ", directories ?? Array.Empty<string>())}";
            return false;
        }
    }

    /// <summary>UTF-8 辅助，避免 NativeTransport 里散落 Encoding 调用。</summary>
    public static class PnText
    {
        public static string Utf8(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return string.Empty;
            return Encoding.UTF8.GetString(buffer, offset, count);
        }
    }
}
