using nel;
using System.Collections.Generic;

namespace PolarisNoels
{
    public static class CFGMultiplayer
    {
        public static bool showNicknames = true;
        public static bool showDelay = true;

        public static void PrepareEntries(List<CfgEntry> list)
        {
            list.Add(new CfgEntry("mpconfig_show_nicknames", 1f, () => showNicknames).DescDisEn());
            list.Add(new CfgEntry("mpconfig_show_delay", 1f, () => showDelay).DescDisEn());
            list.Add(new CfgEntry("mpconfig_enable_stun", 1f, () => Networking.NetworkRuntime.EnableStun).DescDisEn());
        }

        public static bool ChangeConfigValue(string name, float cur_value)
        {
            switch (name)
            {
                case "mpconfig_show_nicknames":
                    showNicknames = cur_value != 0;
                    return true;
                case "mpconfig_show_delay":
                    showDelay = cur_value != 0;
                    return true;
                case "mpconfig_enable_stun":
                    Networking.NetworkRuntime.EnableStun = cur_value != 0;
                    return true;
                default:
                    return false;
            }
        }
    }
}
