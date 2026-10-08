using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using nel.title;
using Polaris;
using XX;
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

            // 标题菜单的"多人"按钮：交给 Core 的 MainMenu 统一管理按钮列表与排版（插在"设置"之前），
            // 不再自己用转译器改 initButtons，也就不会和其它加按钮的模组互相打架。
            PolarisAPI.MainMenu.AddButton("&&btn_multiplayer", OnMultiplayerButton, insertIndex: 2);

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

        /// <summary>点"多人"按钮：借用原版难度选择页的状态，由 <c>Patch_SceneTitleTemp_changeState</c> 把那一页换成联机确认页。</summary>
        private static bool OnMultiplayerButton(aBtn button)
        {
            DB.PolarisNoelsUIClicking = true;
            PolarisAPI.MainMenu.ChangeState(SceneTitleTemp.STATE.DIFF_SELECT);
            return true;
        }

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
