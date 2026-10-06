using System.Collections.Generic;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

// 测试替身（stub）：真实会话类只依赖游戏/Unity/BepInEx/DB 的这些表面。
// 这里刻意不实现任何会话逻辑（握手、就绪判定、延迟分发都在源链接的真实文件里），
// 只提供字段、空实现与可观测计数，供测试断言。
namespace PolarisNoels
{
    /// <summary>替代 BepInEx 的 ManualLogSource：记录日志文本，供「可诊断」断言使用。</summary>
    public sealed class TestLogSink
    {
        public readonly List<string> Info = new List<string>();
        public readonly List<string> Warning = new List<string>();
        public readonly List<string> Error = new List<string>();
        public readonly List<string> Debug = new List<string>();

        public void LogInfo(string message) => Info.Add(message);
        public void LogWarning(string message) => Warning.Add(message);
        public void LogError(string message) => Error.Add(message);
        public void LogDebug(string message) => Debug.Add(message);
    }

    public static class Plugin
    {
        public static readonly TestLogSink Logger = new TestLogSink();
    }

    public enum NetWorkType
    {
        Host,
        Client
    }

    public static class PolarisNoelsTools
    {
        public static int LocalID = -1;
        public static int SimBattleSyncHost = -1;
        public static List<int> SimBattleSyncList = new List<int>();
        public static NetWorkType Type;

        public static readonly List<int> CleanedUpPeers = new List<int>();
        public static int ToggleMuteCalls;
        public static int SetNicknameBgsCalls;
        public static List<KeyValuePair<int, ClientConfig>> LastGeneratedConfigs;

        public static void ToggleMute()
        {
            ToggleMuteCalls++;
            DB.Mute = !DB.Mute;
        }

        public static void GenerateAllNoels(List<KeyValuePair<int, ClientConfig>> list)
        {
            LastGeneratedConfigs = list;
        }

        public static void SetAllNickNameBgs()
        {
            SetNicknameBgsCalls++;
        }

        public static void CleanUpClient(int id)
        {
            CleanedUpPeers.Add(id);
            DB.peerConfigs.Remove(id);
            DB.partyInfos.Remove(id);
            SimBattleSyncList.Remove(id);
        }

        public class NetworkConfig
        {
            public NetWorkType Type;
            public int port;
            public string ip;
            public NoelType NoelType;
            public ColorNoelColor NoelColor;
            public bool InvisibleNickname;
            public string nickName;

            public static implicit operator ClientConfig(NetworkConfig config)
            {
                return new ClientConfig
                {
                    Nickname = config.nickName,
                    NoelType = config.NoelType,
                    NoelColor = config.NoelColor
                };
            }
        }
    }

    public static class DB
    {
        public const string SYNC_FILE_NAME = "polarisnoels_sync.aicsave";

        public static int MaxPlayerCount = 5;
        public static PolarisNoelsTools.NetworkConfig InitConfig;
        public static byte[] SyncSaveContentBuffer;
        public static int LocalNoelParty;
        public static Dictionary<int, PartyManager.Party> partyInfos = new Dictionary<int, PartyManager.Party>();
        public static Dictionary<int, ClientConfig> peerConfigs = new Dictionary<int, ClientConfig>();
        public static nel.PRNoel MainPR;
        public static bool PolarisNoelsHostKicked;
        public static bool PolarisNoelsHostClosed;
        public static bool Mute;
    }

    public static class SVD
    {
        public static string Dir;

        public static string getDir() => Dir;
    }

    public static class LocalNickname
    {
        public static int ApplyCalls;

        public static void Apply() => ApplyCalls++;
    }
}

namespace PolarisNoels.DataStruct
{
    public class ClientConfig
    {
        public string Nickname;
        public NoelType NoelType;
        public ColorNoelColor NoelColor;
    }

    public enum NoelType
    {
        Normal,
        Inverse,
        ColorNoel
    }

    public enum ColorNoelColor
    {
        Red,
        Orange,
        Yellow,
        Green,
        Cyan,
        Blue,
        Purple,
        Magenta
    }
}

namespace PolarisNoels.SN
{
    public static class PartyManager
    {
        public static Party InitNewParty(int id)
        {
            return new Party { ID = id, Name = "NoelParty#" + id };
        }

        public class Party
        {
            public int ID;
            public string Name;
        }
    }
}

// nel 命名空间里会话类/DB 用到的游戏类型：只保留字段形状，不实现游戏行为。
// NetworkRuntime.DeferGameDispatch 需要 MainPR / Mp / NM2D.transferring_game_stopping。
namespace nel
{
    public class NelM2DBase
    {
        public bool transferring_game_stopping;
        public string QuitScene;

        public void quitGame(string scene) => QuitScene = scene;
    }

    public class MpBase
    {
    }

    public class PRNoel
    {
        public NelM2DBase M2D;
        public MpBase Mp;
        public NelM2DBase NM2D;
    }
}
