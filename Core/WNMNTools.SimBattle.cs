using LiteNetLib.Utils;
using m2d;
using nel;
using nel.mgm.smncr;
using ProtoBuf;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WeNeedMoreNoels.CSNetworking;
using WeNeedMoreNoels.DataStruct;
using WeNeedMoreNoels.SN;
using XX;

namespace WeNeedMoreNoels
    {
    public static partial class WNMNTools
    {
        public static void NotifySimBattle(int id, SimBattle sim)
        {
            switch (sim.Type)
            {
                case NotifySimBattleType.StartHost:
                    SimBattleSyncHost = id;
                    break;
                case NotifySimBattleType.CloseHost:
                    UiMenuMul.BxSB?.deactivate();
                    SimBattleSyncHost = -1;
                    if (USC != null && USC.state == SimBattleLobby.State)
                    {
                        USC.changeState(UiSmnCreator.STATE.FILESEL);
                    }
                    SimBattleSyncList.Clear();
                    SimBattleReadyList.Clear();
                    break;
                case NotifySimBattleType.ConnectHost:
                    SimBattleSyncList.Add(id);
                    UpdateSimUI?.Invoke();
                    break;
                case NotifySimBattleType.DisconnectHost:
                    SimBattleSyncList.Remove(id);
                    SimBattleReadyList.Remove(id);
                    UpdateSimUI?.Invoke();
                    break;
                case NotifySimBattleType.ReadyHost:
                    SimBattleReadyList.Add(id);
                    UpdateSimUI?.Invoke();
                    break;
                case NotifySimBattleType.UnreadyHost:
                    SimBattleReadyList.Remove(id);
                    UpdateSimUI?.Invoke();
                    break;
            }
        }

        public static void OpenSmncBattle()
        {
            if (USBC is null)
            {
                USC.changeState(UiSmnCreator.STATE.BATTLE_CONFIRM);
                USBC = USC.BattleConfirm;
                USC.changeState(SimBattleLobby.State);
            }
            SmncStageEditorManager.StgObject stg = CurSimFile.Astgo[0];
            stg.x = SpawnDic.ContainsKey(LocalID) ? (int)SpawnDic[LocalID].x : (int)SpawnDic[-1].x;
            stg.y = SpawnDic.ContainsKey(LocalID) ? (int)SpawnDic[LocalID].y : (int)SpawnDic[-1].y;
            CurSimFile.Astgo[0] = stg;
            USBC.CurFile = CurSimFile;
			USBC.Record();
			uint num;
			int num2;
			if (USBC.UiDg != null)
			{
				num = USBC.CurFile.weather_bits;
				num2 = (int)USBC.CurFile.dangerousness;
			}
			else
			{
				num = 0U;
				num2 = 0;
				USBC.CurFile.fix_nattr = false;
			}
            if (USBC.LpArea.summoner_openable)
            {
                USBC.CurFile.use_seed = (USBC.decline_manage_danger ? 0U : USBC.CurFile.rand_seed);
                if (USBC.CurFile.use_seed != 0U)
                {
                    USBC.CurFile.pre_seed = USBC.CurFile.use_seed;
                    USBC.CurFile.use_seed ^= 3413251945U;
                }
                else
                {
                    USBC.CurFile.use_seed = X.xors();
                    USBC.CurFile.pre_seed = USBC.CurFile.use_seed ^ 3413251945U;
                }
                SND.Ui.play("enter", false);
                if (USBC.LpArea.auto_save_on_opening_summoner && CFG.autosave_on_scenario)
                {
                    COOK.autoSave(USBC.LpArea.nM2D, false, false);
                }
                if (USBC.BChkRestore != null && USBC.LpArea.restore_items > 0 && USBC.BChkRestore.isChecked())
                {
                    GF.setB("SMNC_RESTORE_ITEMS", true);
                }
                USBC.LpArea.Reader.fatal_key = (TX.noe(USBC.Con.fatal_key) || !USBC.fatal_playable) ? null : USBC.Con.fatal_key;
                USBC.LpArea.openSummoner(num2, num);
                DB.CurSummoner = USBC.LpArea;
                USBC?.FD_BattleConfirm(num2, num);
            }
        }

        public static void ResumeUSBCPage()
        {
            SSE.changeState(SmncStageEditor.STATE.OFFLINE);
            USC.changeState(SimBattleLobby.State);
            USBC.Bx.deactivate();
            UiMenuMul.BxSB.activate();
            UiMenuMul.BxSB.Focus();
            if (!SpawnDic.ContainsKey(CurrentSetID))
            {
                SpawnDic.Add(CurrentSetID, SettingResult);
            }
            else
            {
                SpawnDic[CurrentSetID] = SettingResult;
            }
            UpdateSimUI?.Invoke();
        }
    }
}
