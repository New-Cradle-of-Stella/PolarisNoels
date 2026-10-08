using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using Polaris;
using System;
using System.Reflection;
using PolarisNoels.Networking;

namespace PolarisNoels
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("Polaris.Core")]
    public class Plugin : PolarisMod
    {
        public static Plugin PluginInstance;
        internal static new ManualLogSource Logger;
        /// <summary>补丁已由 PolarisMod 逐类安全应用；这里只做 Noels 自己的初始化。</summary>
        protected override void OnLoad()
        {
            // Plugin startup logic
            Logger = base.Logger;
            // 原生传输要先加载：插件目录不在 Windows DLL 搜索路径里。失败只禁用联机。
            NetworkRuntime.PreloadNative();

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

        /// <summary>标题/游戏/加载三种状态都要 poll：原生事件不能因为场景切换而积压。</summary>
        private void Update()
        {
            NetworkRuntime.Poll();
        }

        protected override void OnUnload()
        {
            StopAllCoroutines();
            NetworkRuntime.Shutdown();
        }
    }
}
