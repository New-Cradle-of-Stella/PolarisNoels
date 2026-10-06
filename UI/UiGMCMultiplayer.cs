using nel;
using nel.gm;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XX;

namespace PolarisNoels
{
    public class UiGMCMultiplayer : UiGMC
    {
        public UiGMCMultiplayer(UiGameMenu _GM, CATEG _categ)
            : base(_GM, _categ, true, 0, 0, 0, 0, 1f, 1f)
        {
        }

        public override bool initAppearMain()
        {
            base.initAppearMain();
            BxR.Clear();
            BxR.init();
            BxR.use_button_connection = true;
            BxR.selectable_loop = 3;
            BxR.item_margin_x_px = 60f;
            float btnW = (BxR.use_w - BxR.item_margin_x_px) / 2f - 100f;
            float btnH = 30f;
            List<aBtn> btns = [];
            BxR.alignx = ALIGN.CENTER;
            BxR.addP(new()
            {
                TxCol = ColorDefault,
                size = 30,
                text = TX.Get("multiplayer_menu_title")
            });
            BxR.addHr(new()
            {
                margin_t = 5f,
                margin_b = 5f
            });
            BxR.alignx = ALIGN.CENTER;
            BxR.addP(new()
            {
                TxCol = ColorDefault,
                size = 30,
                text = TX.Get("multiplayer_menu_utilities")
            });
            BxR.Br();
            btns.Add(BxR.addButton(new()//0
            {
                title = TX.Get("multiplayer_menu_modify_nickname"),
                w = btnW,
                h = btnH,
                fnClick = B =>
                {
                    UiBoxDesigner BxCmd = UiMenuMul.BxP;
                    BxCmd.activate();
                    IN.setZ(BxCmd.transform, BxR.transform.position.z - 1f);
                    BxCmd.Clear();
                    BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
                    BxCmd.WH(300f, 150f);
                    BxCmd.margin_in_lr = 10f;
                    BxCmd.margin_in_tb = 10f;
                    BxCmd.init();
                    BxCmd.alignx = ALIGN.CENTER;
                    var nicknameInput = BxCmd.addInput(new()
                    {
                        h = 20f
                    });
                    nicknameInput.Select(true);
                    nicknameInput.text = DB.InitConfig.nickName;
                    BxCmd.Br();
                    BxCmd.addButton(new()
                    {
                        title = TX.Get("Submit"),
                        fnClick = B =>
                        {
                            PolarisNoelsTools.SendUpdatePeerInfoToAllPeers(PolarisNoelsTools.LocalID, nicknameInput.text);
                            DB.Nickname = nicknameInput.text;
                            DB.peerConfigs[PolarisNoelsTools.LocalID].Nickname = nicknameInput.text;
                            DB.MainPRNickname.SetText(nicknameInput.text);
                            BxCmd.deactivate();
                            BxR.Focus();
                            return true;
                        }
                    });
                    BxCmd.addButton(new()
                    {
                        title = TX.Get("Cancel"),
                        fnClick = B =>
                        {
                            BxCmd.deactivate();
                            BxR.Focus();
                            return true;
                        }
                    });
                    Vector3 btnPos = B.transform.position;
                    float targetX = btnPos.x * 64f + 300f;
                    float targetY = btnPos.y * 64f;
                    BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
                    BxCmd.Focusable(true, true);
                    BxCmd.Focus();
                    return true;
                }
            }));
            btns.Add(BxR.addButton(new()//1
            {
                title = TX.Get("multiplayer_menu_modify_party"),
                w = btnW,
                h = btnH,
                fnClick = B =>
                {
                    List<KeyValuePair<int, string>> btns = [.. PolarisNoelsTools.AllNicknames, new(-1, TX.Get("multiplayer_reset")), new(0, TX.Get("Cancel"))];
                    UiBoxDesigner BxCmd = UiMenuMul.BxP;
                    BxCmd.activate();
                    IN.setZ(BxCmd.transform, BxR.transform.position.z - 1f);
                    BxCmd.Clear();
                    BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
                    BxCmd.WH(200f, 48f * btns.Count);
                    BxCmd.margin_in_lr = 10f;
                    BxCmd.margin_in_tb = 10f;
                    BxCmd.init();
                    var con = BxCmd.addButtonMultiT<aBtnNel>(new DsnDataButtonMulti
                    {
                        name = "sub_menu",
                        titles = [.. btns.Select(x => x.Value)],
                        skin = "row_center",
                        clms = 1,
                        w = BxCmd.use_w,
                        h = 30f,
                        fnClick = (aBtn BSub) =>
                        {
                            if (BSub.title != TX.Get("Cancel"))
                            {
                                int i = btns.Find(x => x.Value == BSub.title).Key;
                                if (i == -1)
                                {
                                    DB.LocalNoelParty = PolarisNoelsTools.LocalID;
                                }
                                else
                                {
                                    DB.LocalNoelParty = DB.noelIns[i].NoelInfo.PartyID;
                                }
                            }
                            BxCmd.deactivate();
                            BSub.Select(true);
                            BxR.Focus();
                            return true;
                        }
                    });
                    con.Get(0).Select(true);
                    Vector3 btnPos = B.transform.position;
                    float targetX = btnPos.x * 64f + 300f;
                    float targetY = btnPos.y * 64f;
                    BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
                    BxCmd.Focusable(true, true);
                    BxCmd.Focus();
                    return true;
                }
            }));
            BxR.Br();
            btns.Add(BxR.addButton(new()//2
            {
                title = TX.Get("multiplayer_menu_teleport"),
                w = btnW,
                h = btnH,
                fnClick = B =>
                {
                    if (B.isLocked())
                    {
                        SND.Ui.play("Locked");
                        return true;
                    }
                    OnPlayerSelect(B, i =>
                    {
                        string targetKey = DB.noelIns[i].MpKey;
                        float x = DB.noelIns[i].NoelInfo.PositionX;
                        float y = DB.noelIns[i].NoelInfo.PositionY;
                        PolarisNoelsTools.TransferMainNoel(targetKey, x, y);
                        GM.deactivate();
                    });
                    return true;
                }
            }));
            if (DB.IsInBattle)
            {
                btns[2].SetLocked(true);
            }
            else
            {
                btns[2].SetLocked(false);
            }
            UiMenuMul.SendMsgButton = BxR.addButton(new()
            {
                title = TX.Get("multiplayer_menu_sendmsg"),
                w = btnW,
                h = btnH,
                fnClick = B =>
                {
                    string[] titles = [.. msgs.Select(x => TX.Get(x)), TX.Get("Cancel")];
                    if (DB.Mute)
                    {
                        Mutted(B, UiMenuMul.BxP);
                        return true;
                    }
                    UiBoxDesigner BxCmd = UiMenuMul.BxP;
                    BxCmd.activate();
                    IN.setZ(BxCmd.transform, BxR.transform.position.z - 1f);
                    BxCmd.Clear();
                    BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
                    BxCmd.WH(200f, 48f * titles.Length);
                    BxCmd.margin_in_lr = 10f;
                    BxCmd.margin_in_tb = 10f;
                    BxCmd.init();
                    var BCon = BxCmd.addButtonMultiT<aBtnNel>(new DsnDataButtonMulti
                    {
                        name = "sub_menu",
                        titles = titles,
                        skin = "row_center",
                        clms = 1,
                        w = BxCmd.use_w,
                        h = 30f,
                        fnClick = (aBtn BSub) =>
                        {
                            if (BSub.title != TX.Get("Cancel"))
                            {
                                PolarisNoelsTools.BroadcastMsg(msgs[titles.IndexOf(BSub.title)]);
                                BxCmd.deactivate();
                                BSub.Select(true);
                                BxR.Focus();
                                GM.deactivate();
                            }
                            else
                            {
                                BxCmd.deactivate();
                                BSub.Select(true);
                                BxR.Focus();
                            }
                            return true;
                        }
                    });
                    BCon.Get(0).Select(true);
                    Vector3 btnPos = B.transform.position;
                    float targetX = btnPos.x * 64f + 300f;
                    float targetY = btnPos.y * 64f;
                    BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
                    BxCmd.Focusable(true, true);
                    BxCmd.Focus();
                    return true;
                }
            });
            btns.Add(UiMenuMul.SendMsgButton);//3
            BxR.Br();
            if (PolarisNoelsTools.Type == NetWorkType.Host)
            {
                BxR.addHr(new()
                {
                    margin_t = 5f,
                    margin_b = 5f
                });
                BxR.alignx = ALIGN.CENTER;
                BxR.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 30,
                    text = TX.Get("multiplayer_menu_host_utilities")
                });
                BxR.Br();
                btns.Add(BxR.addButton(new()//4
                {
                    title = TX.Get("multiplayer_menu_host_kick"),
                    w = btnW,
                    h = btnH,
                    fnClick = B =>
                    {
                        OnPlayerSelect(B, i =>
                        {
                            PolarisNoelsTools.Kick(i);
                            GM.deactivate();
                        });
                        return true;
                    }
                }));
                btns.Add(BxR.addButton(new()//5
                {
                    title = TX.Get("multiplayer_menu_host_mute"),
                    w = btnW,
                    h = btnH,
                    fnClick = B =>
                    {
                        OnPlayerSelect(B, i =>
                        {
                            PolarisNoelsTools.Mute(i);
                            GM.deactivate();
                        });
                        return true;
                    }
                }));
                BxR.Br();
                btns.Add(BxR.addButton(new()//6
                {
                    title = TX.Get("multiplayer_menu_host_teleportallself"),
                    w = btnW,
                    h = btnH,
                    fnClick = B =>
                    {
                        PolarisNoelsTools.SendNotifyNoelTransferToAllPeers(0);
                        GM.deactivate();
                        return true;
                    }
                }));
                btns.Add(BxR.addButton(new()//7
                {
                    title = TX.Get("multiplayer_menu_host_modifyroomconfig"),
                    w = btnW,
                    h = btnH,
                    fnClick = B =>
                    {
                        UiBoxDesigner BxCmd = UiMenuMul.BxP;
                        UiBoxDesigner BxCmdDesc = UiMenuMul.BxPD;
                        BxCmd.activate();
                        IN.setZ(BxCmd.transform, BxR.transform.position.z - 1f);
                        BxCmd.Clear();
                        BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
                        BxCmd.WH(600f, 300f);
                        BxCmd.margin_in_lr = 10f;
                        BxCmd.margin_in_tb = 10f;
                        BxCmd.init();
                        BxCmdDesc.activate();
                        IN.setZ(BxCmdDesc.transform, BxR.transform.position.z - 1f);
                        BxCmdDesc.Clear();
                        BxCmdDesc.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
                        BxCmdDesc.WH(600f, 150f);
                        BxCmdDesc.margin_in_lr = 10f;
                        BxCmdDesc.margin_in_tb = 10f;
                        BxCmdDesc.init();
                        BxCmdDesc.alignx = ALIGN.CENTER;
                        if (DB.IsInBattle)
                        {
                            BxCmdDesc.addP(new()
                            {
                                TxCol = ColorDefault,
                                size = 8f,
                                text = TX.Get("Desc_multiplayer_enemy_inbattle")
                            });
                        }
                        else
                        {
                            BxCmdDesc.addP(new()
                            {
                                TxCol = ColorDefault,
                                size = 8f,
                                text = TX.Get("Desc_multiplayer_enemy")
                            });
                        }
                        BxCmd.alignx = ALIGN.CENTER;
                        BxCmd.addP(new()
                        {
                            TxCol = ColorDefault,
                            size = 30,
                            text = TX.Get("multiplayer_menu_config_title")
                        });
                        BxCmd.addHr(new()
                        {
                            margin_t = 5f,
                            margin_b = 5f
                        });
                        BxCmd.alignx = ALIGN.CENTER;
                        List<string> counts = [];
                        for (int i = 2; i <= 16; i++)
                        {
                            if (i <= 5)
                            {
                                counts.Add(i.ToString() + " " + TX.Get("multiplayer_recommend"));
                            }
                            else if (i <= 11)
                            {
                                counts.Add(i.ToString() + " " + TX.Get("multiplayer_unstable"));
                            }
                            else
                            {
                                counts.Add(i.ToString() + " " + TX.Get("multiplayer_experimental"));
                            }
                        }
                        BxCmd.addP(new()
                        {
                            TxCol = ColorDefault,
                            size = 20f,
                            alignx = ALIGN.LEFT,
                            text = TX.Get("multiplayer_room_maxplayercount"),
                        });
                        var slider = BxCmd.addSliderCT(new()
                        {
                            name = "maxPlayer",
                            skin_title = "",
                            def = DB.MaxPlayerCount - 2,
                            mn = 0f,
                            mx = counts.Count - 1,
                            Adesc_keys = [.. counts],
                            fnChanged = (_, _, i) =>
                            {
                                DB.MaxPlayerCount = (int)i + 2;
                                return true;
                            },
                            fnBtnMeterLine = (B, index, val) =>
                            {
                                var count = (int)val + 2;
                                return count / 16f;
                            },
                        }, 200);
                        slider.Select(true);
                        BxCmd.Br();
                        BxCmd.addP(new()
                        {
                            TxCol = ColorDefault,
                            size = 20f,
                            alignx = ALIGN.LEFT,
                            text = TX.Get("multiplayer_room_enablepvp"),
                        });
                        BxCmd.addSlider(new()
                        {
                            name = "EnablePVP",
                            skin_title = "",
                            checkbox_mode = 1,
                            def = PolarisNoelsTools.EnablePVP ? 1 : 0,
                            Adesc_keys = TX.GetArray("Disabled", "Enabled"),
                            fnChanged = (_, _, i) =>
                            {
                                PolarisNoelsTools.EnablePVP = i == 1;
                                return true;
                            }
                        });
                        if (!DB.IsInBattle)
                        {
                            BxCmd.Br();
                            BxCmd.addP(new()
                            {
                                TxCol = ColorDefault,
                                size = 20f,
                                alignx = ALIGN.LEFT,
                                text = TX.Get("multiplayer_enemy_title"),
                            });
                            BxCmd.addSliderCT(new()
                            {
                                name = "EnemyMode",
                                skin_title = "",
                                title = TX.Get("multiplayer_enemy_title"),
                                mn = 0,
                                mx = 2,
                                def = (int)PolarisNoelsTools.SyncType,
                                checkbox_mode = 2,
                                Adesc_keys = TX.GetArray("multiplayer_enemy_starter", "multiplayer_enemy_smart", "multiplayer_enemy_independent"),
                                fnChanged = (_, _, i) =>
                                {
                                    PolarisNoelsTools.SyncType = (EnemySyncType)i;
                                    return true;
                                }
                            }, 150);
                        }
                        BxCmd.Br();
                        BxCmd.addButton(new()
                        {
                            title = TX.Get("Submit"),
                            fnClick = B =>
                            {
                                BxCmd.deactivate();
                                BxR.Focus();
                                return true;
                            }
                        });
                        Vector3 btnPos = B.transform.position;
                        float targetX = btnPos.x * 64f + 60f;
                        float targetY = btnPos.y * 64f + 100f;
                        BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
                        BxCmdDesc.posSetDA(targetX, targetY - BxCmd.h / 2 - BxCmdDesc.h / 2, 0, 20f, true);
                        BxCmd.Focusable(true, true);
                        BxCmd.Focus();
                        return true;
                    }
                }));
            }
            BxR.Br();
            BxR.addFocusFn(_B =>
            {
                UiMenuMul.BxP.deactivate();
                UiMenuMul.BxPD.deactivate();
                btns[3].Select(true);
                return true;
            });
            FineNav([.. btns]);
            return true;
        }

        void FineNav(aBtn[] btns)
        {
            btns[0].setNaviR(btns[1], true, true);
            btns[1].setNaviR(btns[0], true, true);
            btns[2].setNaviR(btns[3], true, true);
            btns[3].setNaviR(btns[2], true, true);
            btns[0].setNaviB(btns[2], true, true);
            btns[1].setNaviB(btns[3], true, true);
            if (btns.Length > 4)
            {
                btns[4].setNaviR(btns[5], true, true);
                btns[5].setNaviR(btns[4], true, true);
                btns[6].setNaviR(btns[7], true, true);
                btns[7].setNaviR(btns[6], true, true);

                btns[2].setNaviB(btns[4], true, true);
                btns[4].setNaviB(btns[6], true, true);
                btns[3].setNaviB(btns[5], true, true);
                btns[5].setNaviB(btns[7], true, true);

                btns[0].setNaviT(btns[6], true, true);
                btns[1].setNaviT(btns[7], true, true);
            }
            else
            {
                btns[0].setNaviT(btns[2], false, true);
                btns[1].setNaviT(btns[3], false, true);
            }
        }

        public override void initEdit()
        {
        }

        public override void quitEdit()
        {
        }

        void Mutted(aBtn B, UiBoxDesigner BxCmd)
        {
            BxCmd.activate();
            IN.setZ(BxCmd.transform, BxR.transform.position.z - 1f);
            BxCmd.Clear();
            BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
            BxCmd.WH(300f, 150f);
            BxCmd.margin_in_lr = 10f;
            BxCmd.margin_in_tb = 10f;
            BxCmd.init();
            BxCmd.alignx = ALIGN.CENTER;
            BxCmd.addP(new()
            {
                text = TX.Get("multiplayer_menu_mute"),
                TxCol = Color.HSVToRGB(0, 0.95f, 0.91f)
            });
            BxCmd.Br();
            BxCmd.addButton(new()
            {
                title = TX.Get("Submit"),
                fnClick = B =>
                {
                    BxCmd.deactivate();
                    BxR.Focus();
                    return true;
                }
            });
            Vector3 btnPos = B.transform.position;
            float targetX = btnPos.x * 64f + 300f;
            float targetY = btnPos.y * 64f;
            BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
            BxCmd.Focusable(true, true);
            BxCmd.Focus();
        }

        void OnPlayerSelect(aBtn B, Action<int> OnSelectedIndex)
        {
            List<KeyValuePair<int, string>> btns = [.. PolarisNoelsTools.AllNicknames, new(0, TX.Get("Cancel"))];
            UiBoxDesigner BxCmd = UiMenuMul.BxP;
            BxCmd.activate();
            IN.setZ(BxCmd.transform, BxR.transform.position.z - 1f);
            BxCmd.Clear();
            BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
            BxCmd.WH(200f, 48f * btns.Count);
            BxCmd.margin_in_lr = 10f;
            BxCmd.margin_in_tb = 10f;
            BxCmd.init();
            var con = BxCmd.addButtonMultiT<aBtnNel>(new DsnDataButtonMulti
            {
                name = "sub_menu",
                titles = [.. btns.Select(x => x.Value)],
                skin = "row_center",
                clms = 1,
                w = BxCmd.use_w,
                h = 30f,
                fnClick = (aBtn BSub) =>
                {
                    if (BSub.title != TX.Get("Cancel"))
                    {
                        OnSelectedIndex?.Invoke(btns.Find(x => x.Value == BSub.title).Key);
                    }
                    BxCmd.deactivate();
                    BSub.Select(true);
                    BxR.Focus();
                    return true;
                }
            });
            con.Get(0).Select(true);
            Vector3 btnPos = B.transform.position;
            float targetX = btnPos.x * 64f + 300f;
            float targetY = btnPos.y * 64f;
            BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
            BxCmd.Focusable(true, true);
            BxCmd.Focus();
        }

        static Color ColorDefault => Color.HSVToRGB(0, 0, 0.219f);

        static readonly string[] msgs = ["multiplayer_msg_greeting", "multiplayer_msg_come_here", "multiplayer_msg_pve_start", "multiplayer_msg_pve_warn", "multiplayer_msg_pve_help", "multiplayer_msg_pvp_start", "multiplayer_msg_pvp_greeting", "multiplayer_msg_sitting", "multiplayer_msg_goodbye"];
    }
}
