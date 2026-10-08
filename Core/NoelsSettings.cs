using Polaris.Settings;

namespace PolarisNoels
{
    /// <summary>联机相关的设置项。取代原来的 <c>Config.Bind</c>：字段本身就是值，Polaris 负责持久化并在游戏设置页的 Polaris 标签里显示。</summary>
    /// <remarks>值在所有模组 Awake 之后才由 Polaris 读入；在 <c>OnLoad</c> 里读到的是默认值，需要已读入的值请在 <c>OnReady</c> 之后用。</remarks>
    [PolarisSettingGroup("polarisnoels", "PolarisNoels")]
    internal static class NoelsSettings
    {
        [PolarisSetting("UDP 绑定端口", Desc = "0 = 随机端口。", Min = 0, Max = 65535, Step = 1)]
        public static int BindPort = 0;

        [PolarisSetting("最大玩家数", Desc = "含主机，2~8。", Min = 2, Max = 8, Step = 1)]
        public static int MaxPlayers = 5;

        [PolarisSetting("&mpconfig_enable_stun", Desc = "用 STUN 收集公网候选；关闭则只走内网/直连。")]
        public static bool EnableStun = true;

        // 下面两项只在联机中有意义，平时不占设置页的位置。
        [PolarisSetting("&Config_mpconfig_show_nicknames", Desc = "&Config_desc_mpconfig_show_nicknames", VisibleWhen = nameof(InMultiplayer))]
        public static bool ShowNicknames = true;

        [PolarisSetting("&Config_mpconfig_show_delay", Desc = "&Config_desc_mpconfig_show_delay", VisibleWhen = nameof(InMultiplayer))]
        public static bool ShowDelay = true;

        static bool InMultiplayer => DB.IsMultiplayer;
    }
}
