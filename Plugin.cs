using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using System;
using System.Reflection;
using PolarisNoels.Networking;

namespace PolarisNoels
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin PluginInstance;
        internal static new ManualLogSource Logger;
        private Harmony _harmony;

        private void Awake()
        {
            // Plugin startup logic
            Logger = base.Logger;
            // 原生传输要先加载：插件目录不在 Windows DLL 搜索路径里。失败只禁用联机。
            NetworkRuntime.BindPort = Config.Bind("Network", "BindPort", 0, "UDP 绑定端口，0 = 随机端口").Value;
            NetworkRuntime.ConfiguredMaxPeers = Config.Bind("Network", "MaxPlayers", 5, "最大玩家数（含主机），2~8").Value;
            NetworkRuntime.EnableStun = Config.Bind("Network", "EnableStun", true, "是否用 STUN 收集公网候选（关闭则只走内网/直连）").Value;
            NetworkRuntime.PreloadNative();
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            PatchAllSafe();

            Logger.LogMessage(Environment.NewLine + LOGO_PLUGIN +
                              Environment.NewLine + $"Version {Assembly.GetExecutingAssembly().GetName().Version}" +
                              Environment.NewLine + "Created by Alon_, Created at 2026-4-13, Happy birthday to myself");
            MTRExtension.Load();
            SimBattleLobby.Init();
            ReceiveMessageManager.Init();

            PluginInstance = this;
        }

        public const string LOGO_PLUGIN =
            """
            ██████╗  ██████╗ ██╗      █████╗ ██████╗ ██╗███████╗
            ██╔══██╗██╔═══██╗██║     ██╔══██╗██╔══██╗██║██╔════╝
            ██████╔╝██║   ██║██║     ███████║██████╔╝██║███████╗
            ██╔═══╝ ██║   ██║██║     ██╔══██║██╔══██╗██║╚════██║
            ██║     ╚██████╔╝███████╗██║  ██║██║  ██║██║███████║
            ╚═╝      ╚═════╝ ╚══════╝╚═╝  ╚═╝╚═╝  ╚═╝╚═╝╚══════╝

            ███╗   ██╗ ██████╗ ███████╗██╗     ███████╗
            ████╗  ██║██╔═══██╗██╔════╝██║     ██╔════╝
            ██╔██╗ ██║██║   ██║█████╗  ██║     ███████╗
            ██║╚██╗██║██║   ██║██╔══╝  ██║     ╚════██║
            ██║ ╚████║╚██████╔╝███████╗███████╗███████║
            ╚═╝  ╚═══╝ ╚═════╝ ╚══════╝╚══════╝╚══════╝
            """;

        /// <summary>
        /// 逐个补丁类应用，单个补丁失败只记录日志，不会中断后续补丁与插件初始化。
        /// </summary>
        private void PatchAllSafe()
        {
            int failed = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    failed++;
                    Logger.LogError($"Patch failed: {type.FullName}: {e.InnerException?.Message ?? e.Message}");
                }
            }
            if (failed > 0)
            {
                Logger.LogWarning($"{failed} patch class(es) failed to apply, see errors above.");
            }
        }

        /// <summary>标题/游戏/加载三种状态都要 poll：原生事件不能因为场景切换而积压。</summary>
        private void Update()
        {
            NetworkRuntime.Poll();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            NetworkRuntime.Shutdown();
            _harmony?.UnpatchSelf();
        }
    }
}
