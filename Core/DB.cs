using nel;
using System.Collections.Generic;
using PolarisNoels.DataStruct;
using PolarisNoels.SN;

namespace PolarisNoels
{
    public static class DB
    {
        public static int MaxPlayerCount = 5;

        public static bool PolarisNoelsUIClicking = false;

        public static NetWorkType PolarisNoelsEnterNetworkType;

        public static bool PolarisNoelsEnterNetworkTypeSelected = false;

        public static bool PolarisNoelsHostSelectSVD = false;

        public static bool PolarisNoelsClientTransferNotComplete = false;

        public static bool PolarisNoelsHostClosed = false;

        public static bool PolarisNoelsHostKicked = false;

        public static bool ShowReceiveDebug = false;



        public static PRNoel MainPR;

        public static ShadowNoelNickname MainPRNickname;

        public static ShadowNoelNickname MainPRMsg;

        public static bool ShadowAppear;

        public static string Nickname;

        public static byte[] SyncSaveContentBuffer;

        public static int LocalNoelParty;

        public static Dictionary<int, ShadowNoelInstance> noelIns = [];

        public static Dictionary<int, PartyManager.Party> partyInfos = [];

        public static Dictionary<int, ClientConfig> peerConfigs = [];

        public static List<NelEnemy> CurEnemies = [];


        public static NetWorkType networkType;

        public const string SYNC_FILE_NAME = "polarisnoels_sync.aicsave";



        public static PolarisNoelsTools.NetworkConfig InitConfig = null;

        public static bool IsMultiplayer => InitConfig != null;


        public static M2LpSummon CurSummoner;

        public static bool Mute;

        public static bool IsInBattle;




        public static HashSet<string> StartedBattleSummonerKeys = [];


        public static Dictionary<int, int> peerDelays = [];

        public static void CleanUp()
        {
            CombatSync.Reset();
            BattleSession.ResetAll();
            noelIns.Clear();
            partyInfos.Clear();
            peerConfigs.Clear();
            ShadowNoelMagic.ClearAll();
                        MainPR = null;
            MainPRMsg = null;
            MainPRNickname = null;
            Nickname = null;
            SyncSaveContentBuffer = null;
            InitConfig = null;
            EntityRegistry.Clear();
            StartedBattleSummonerKeys.Clear();
            peerDelays.Clear();
    }
    }
}
