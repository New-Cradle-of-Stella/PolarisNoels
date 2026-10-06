using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using System;
using System.Reflection;
using WeNeedMoreNoels.Networking;

namespace WeNeedMoreNoels
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
            ██╗    ███╗   ███╗   ████╗   ██╗
            ██║    ████╗  ████╗ ██████╗  ██║
            ██║ █╗ ██╔██╗ ██╔████╔██╔██╗ ██║
            ██║███╗██║╚██╗██║╚██╔╝██║╚██╗██║
            ╚███╔███╔╝ ╚████║ ╚═╝ ██║ ╚████║
             ╚══╝╚══╝   ╚═══╝     ╚═╝  ╚═══╝
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

        private void OnDestroy()
        {
            StopAllCoroutines();
            _harmony?.UnpatchSelf();
        }
    }
}
