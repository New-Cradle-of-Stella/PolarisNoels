using nel;
using nel.title;
using System.Collections;
using PolarisNoels.CSNetworking;
using PolarisNoels.DataStruct;
using PolarisNoels.Networking;
using UnityEngine;
using XX;

namespace PolarisNoels
{
    /// <summary>
    /// 标题界面的联机流程：房间码入房、存档同步进度、连接超时与主机关闭提示。
    /// 传输层在标题界面就建立，进入游戏后沿用同一条连接（不再走「先断开再建 P2P」的老路）。
    /// </summary>
    public static class MultiplayerTitleFlow
    {
        static UiBoxDesigner BxHC;

        static UiBoxDesigner BxCC;

        static UiBoxDesigner BxCTO;

        static UiBoxDesigner BxHCI;

        static UiBoxDesigner BxHRC;

        static UiBoxDesigner BxWait;

        static FillBlock RoomCodeText;

        static FillBlock WaitText;

        static FillBlock TimeoutText;

        static aBtn submit;

        static NoelType type;

        static ColorNoelColor color;

        static LabeledInputField RoomCodeInput;

        static LabeledInputField DirectIpInput;

        static BtnContainerNumCounter<aBtnNumCounter> PortCon;

        static LabeledInputField NickNameInput;

        static SceneTitleTemp stt;

        static bool InvisibleNickname;

        /// <returns>true 表示继续执行原版 runIRD</returns>
        public static bool OnRunIRD(SceneTitleTemp instance, ref bool __result)
        {
            stt = instance;
            if (BxCmd == null && stt.BxCon != null)
            {
                BxCmd = stt.BxCon.Create("ColN", 0f, 0f, 200f, 200f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
            }
            if (DB.PolarisNoelsHostClosed)
            {
                if (stt.BxCon is null)
                {
                    __result = true;
                    return true;
                }
                DB.PolarisNoelsHostClosed = false;
                BxHCI = stt.BxCon.Create("hostClosedInfo", 0f, 0f, 380f, IN.h - 620f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                BxHCI.Focusable(false, false, null);
                BxHCI.Clear();
                BxHCI.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 40,
                    alignx = ALIGN.CENTER,
                    aligny = ALIGNY.MIDDLE,
                    text = DB.PolarisNoelsHostKicked ? TX.Get("multiplayer_host_kicked") : TX.Get("multiplayer_host_closed")
                });
                DB.PolarisNoelsHostKicked = false;
                BxHCI.activate();
                BxHCI.positionD(0f, 40f, 3, 50f);
                BxHCI.margin_in_tb = 30f;
                BxHCI.margin_in_lr = 60f;
                BxHCI.use_scroll = false;
                BxHCI.init();
                stt.remakeSumitCancelButton(true, false);
                stt.SubmitBtn.addClickFn(b =>
                {
                    BxHCI.deactivate();
                    BxHCI = null;
                    return true;
                });
                stt.DsBlack.activate();
                stt.DsBlack.init();
                __result = true;
                return false;
            }
            UpdateLiveTexts();
            if (stt.state == SceneTitleTemp.STATE.SVD_SELECT && DB.PolarisNoelsHostSelectSVD)
            {
                if (stt.EditSvd is not null && stt.EditSvd.ui_state == UiSVD.STATE.LOAD_SUCCESS)
                {
                    bool ignore_svd_cfg = stt.EditSvd.ignore_svd_cfg;
                    SVD.sFile file = SVD.GetFileB(UiSVD.last_focused_bindex, true);
                    byte[] buffer = System.IO.File.ReadAllBytes(System.IO.Path.Combine(SVD.getDir(), SVD.getFileName(file)));
                    DB.SyncSaveContentBuffer = buffer;
                    stt.EditSvd.deactivateDesigner();
                    stt.BxR.deactivate();
                    stt.BxDesc.deactivate();
                    BxHC = stt.BxCon.Create("hostConfirm", 0f, 0f, 620f, IN.h - 360f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                    BxHC.Clear();
                    CreateUI(BxHC, b =>
                    {
                        BxCmd?.deactivate();
                        PolarisNoelsTools.NetworkConfig config = new()
                        {
                            Type = NetWorkType.Host,
                            ip = "",
                            port = 0,
                            nickName = NickNameInput.text,
                            NoelType = type,
                            NoelColor = color,
                            InvisibleNickname = InvisibleNickname
                        };
                        DB.InitConfig = config;
                        if (!NetworkRuntime.StartHost(config))
                        {
                            ShowTimeout(TX.Get("multiplayer_native_missing"));
                            return true;
                        }
                        BxHC.deactivate();
                        BxHC = null;
                        CreateHostRoomCodeBox(stt, file, ignore_svd_cfg);
                        return true;
                    }, b =>
                    {
                        BxHC.deactivate();
                        BxHC = null;
                        stt.changeState(SceneTitleTemp.STATE.TOP);
                        return true;
                    }, true, out submit);
                    BxHC.activate();
                    BxHC.Focusable(true, true, null);
                    BxHC.Focus();
                    BxHC.use_scroll = false;
                    BxHC.init();
                    stt.DsBlack.Clear();
                    stt.DsBlack.alpha = 0f;
                    stt.TxOnePoint.text_content = "";
                }
            }
            else if (stt.state == SceneTitleTemp.STATE.TOP)
            {
                BxCmd?.deactivate();
            }
            if (BxHCI is not null || BxHC is not null || BxCC is not null || BxHRC is not null || BxWait is not null || DB.PolarisNoelsClientTransferNotComplete)
            {
                __result = true;
                return false;
            }
            return true;
        }

        /// <summary>房间码/进度这些只有在设计师存在时才能刷新。</summary>
        static void UpdateLiveTexts()
        {
            if (RoomCodeText != null && BxHRC != null)
            {
                RoomCodeText.text_content = TX.Get("multiplayer_room_code") + ": " + (NetworkRuntime.Transport?.RoomCode ?? TX.Get("multiplayer_room_code_collecting")) + NatText();
            }
            if (WaitText != null && BxWait != null)
            {
                ClientSession session = NetworkRuntime.Client;
                string state = session == null
                    ? TX.Get("multiplayer_waiting")
                    : !session.JoinAccepted ? TX.Get("multiplayer_joining")
                    : !session.HostHandshakeDone ? TX.Get("multiplayer_waiting_host")
                    : TX.Get("multiplayer_receiving_save") + " " + session.SaveProgressText();
                WaitText.text_content = state + NatText();
            }
        }
        static string NatText() => string.IsNullOrEmpty(NetworkRuntime.NatType) ? "" : "\nNAT: " + NetworkRuntime.NatType;

        static void CreateHostRoomCodeBox(SceneTitleTemp stt, SVD.sFile file, bool ignore_svd_cfg)
        {
            BxHRC = stt.BxCon.Create("hostRoomCode", 0f, 0f, 620f, IN.h - 420f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
            BxHRC.Clear();
            BxHRC.alignx = ALIGN.CENTER;
            BxHRC.addP(new()
            {
                TxCol = ColorDefault,
                size = 26f,
                text = TX.Get("multiplayer_room_code_hint")
            });
            BxHRC.Br();
            RoomCodeText = BxHRC.addP(new()
            {
                TxCol = ColorDefault,
                size = 24f,
                text = TX.Get("multiplayer_room_code") + ": " + TX.Get("multiplayer_room_code_collecting")
            });
            BxHRC.Br();
            BxHRC.addButton(new()
            {
                title = TX.Get("multiplayer_copy_room_code"),
                fnClick = B =>
                {
                    string code = NetworkRuntime.Transport?.RoomCode;
                    if (!string.IsNullOrEmpty(code))
                    {
                        GUIUtility.systemCopyBuffer = code;
                    }
                    return true;
                }
            });
            BxHRC.Br();
            BxHRC.addButton(new()
            {
                title = TX.Get("Submit"),
                fnClick = B =>
                {
                    EnterGame(stt, file, ignore_svd_cfg);
                    return true;
                }
            });
            BxHRC.Focusable(true, true, null);
            BxHRC.Focus();
            BxHRC.use_scroll = false;
            BxHRC.init();
            stt.DsBlack.Clear();
            stt.DsBlack.alpha = 0f;
            stt.TxOnePoint.text_content = "";
        }

        public static void AfterRunIRD(SceneTitleTemp stt)
        {
            if (DB.PolarisNoelsEnterNetworkTypeSelected)
            {
                DB.PolarisNoelsEnterNetworkTypeSelected = false;
                stt.BxDiff.deactivate();
                if (DB.PolarisNoelsEnterNetworkType == NetWorkType.Host)
                {
                    stt.changeState(SceneTitleTemp.STATE.SVD_SELECT);
                    DB.PolarisNoelsHostSelectSVD = true;
                }
                else
                {
                    BxCC = stt.BxCon.Create("clientConfirm", 0f, 0f, 620f, IN.h - 360f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                    BxCC.Clear();
                    CreateUI(BxCC, b =>
                    {
                        string roomCode = RoomCodeInput.text?.Trim();
                        string directHost = DirectIpInput.text?.Trim();
                        bool useDirect = !string.IsNullOrEmpty(directHost);
                        if (string.IsNullOrEmpty(roomCode) && !useDirect)
                        {
                            ShowTimeout(TX.Get("multiplayer_join_bad_code"));
                            return true;
                        }
                        BxCmd?.deactivate();
                        PolarisNoelsTools.NetworkConfig config = new()
                        {
                            Type = NetWorkType.Client,
                            ip = useDirect ? directHost : "",
                            port = useDirect ? PortCon.cnt_val : 0,
                            nickName = NickNameInput.text,
                            NoelType = type,
                            NoelColor = color,
                            InvisibleNickname = InvisibleNickname
                        };
                        DB.InitConfig = config;
                        DB.PolarisNoelsClientTransferNotComplete = true;
                        BtnCC = b;
                        b.SetLocked(true);
                        if (!NetworkRuntime.StartClient(config, roomCode, useDirect ? directHost : null, useDirect ? PortCon.cnt_val : 0))
                        {
                            DB.PolarisNoelsClientTransferNotComplete = false;
                            ShowTimeout(TX.Get("multiplayer_native_missing"));
                            return true;
                        }
                        BxCC.deactivate();
                        BxCC = null;
                        CreateClientWaitBox(stt);
                        Plugin.PluginInstance.StartCoroutine(WaitForReady(stt));
                        return true;
                    }, b =>
                    {
                        BxCC.deactivate();
                        BxCC = null;
                        return true;
                    }, false, out submit);
                    BxCC.activate();
                    BxCC.Focusable(true, true, null);
                    BxCC.Focus();
                    BxCC.use_scroll = false;
                    BxCC.init();
                    stt.DsBlack.Clear();
                    stt.DsBlack.alpha = 0f;
                    stt.TxOnePoint.text_content = "";
                }
            }
        }

        static void CreateClientWaitBox(SceneTitleTemp stt)
        {
            BxWait = stt.BxCon.Create("clientWaiting", 0f, 0f, 620f, IN.h - 420f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
            BxWait.Clear();
            BxWait.alignx = ALIGN.CENTER;
            BxWait.addP(new()
            {
                TxCol = ColorDefault,
                size = 26f,
                text = TX.Get("multiplayer_waiting")
            });
            BxWait.Br();
            WaitText = BxWait.addP(new()
            {
                TxCol = ColorDefault,
                size = 22f,
                text = TX.Get("multiplayer_joining")
            });
            BxWait.Br();
            BxWait.addP(new()
            {
                TxCol = ColorDefault,
                size = 18f,
                text = TX.Get("multiplayer_room_code_no_rejoin_hint")
            });
            BxWait.Br();
            BxWait.addButton(new()
            {
                title = TX.Get("Cancel"),
                fnClick = B =>
                {
                    CancelClientWait(stt);
                    return true;
                }
            });
            BxWait.Focusable(true, true, null);
            BxWait.Focus();
            BxWait.use_scroll = false;
            BxWait.init();
            stt.DsBlack.Clear();
            stt.DsBlack.alpha = 0f;
            stt.TxOnePoint.text_content = "";
        }

        static aBtn BtnCC;

        /// <summary>建立连接阶段的超时。</summary>
        const float ConnectTimeout = 60f;

        /// <summary>已受理入房后，等待握手 + 存档传完的超时。存档数 MB + 高延迟时给得很宽松。</summary>
        const float TransferTimeout = 120f;

        static IEnumerator WaitForReady(SceneTitleTemp stt)
        {
            float linkAt = -1f;
            float startedAt = Time.time;
            while (true)
            {
                ClientSession session = NetworkRuntime.Client;
                if (session == null)
                {
                    FailClientWait(stt, TX.Get("multiplayer_connect_timeout"));
                    yield break;
                }
                if (session.Ready)
                {
                    EnterGame(stt, new SVD.sFile(-2, true), true);
                    yield break;
                }
                if (session.LastJoinError > 0)
                {
                    FailClientWait(stt, JoinErrorText(session.LastJoinError) + NatText());
                    yield break;
                }
                if (session.JoinAccepted && linkAt < 0f)
                {
                    linkAt = Time.time;
                }
                float elapsed = Time.time - (linkAt >= 0f ? linkAt : startedAt);
                if (elapsed > (linkAt >= 0f ? TransferTimeout : ConnectTimeout))
                {
                    FailClientWait(stt, TX.Get("multiplayer_connect_timeout"));
                    yield break;
                }
                yield return null;
            }
        }

        static string JoinErrorText(int code) => (NetJoinError)code switch
        {
            NetJoinError.BadCode => TX.Get("multiplayer_join_bad_code"),
            NetJoinError.PunchTimeout => TX.Get("multiplayer_join_punch_timeout"),
            NetJoinError.HandshakeFailed => TX.Get("multiplayer_join_handshake_failed"),
            NetJoinError.RoomFull => TX.Get("multiplayer_join_room_full"),
            _ => TX.Get("multiplayer_connect_timeout")
        };

        static void CancelClientWait(SceneTitleTemp stt)
        {
            DB.PolarisNoelsClientTransferNotComplete = false;
            DB.InitConfig = null;
            NetworkRuntime.Shutdown();
            BxWait?.deactivate();
            BxWait = null;
            WaitText = null;
            BtnCC?.SetLocked(false);
            stt.changeState(SceneTitleTemp.STATE.TOP);
        }

        static void FailClientWait(SceneTitleTemp stt, string reason)
        {
            Plugin.Logger.LogWarning($"client join failed: {reason}");
            CancelClientWait(stt);
            ShowTimeout(reason);
        }

        static void ShowTimeout(string text)
        {
            if (BxCTO == null)
            {
                BxCTO = stt.BxCon.Create("clientTimeOut", 0f, 0f, 380f, (IN.h - 620f) * 1.5f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                BxCTO.alignx = ALIGN.CENTER;
                TimeoutText = BxCTO.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 40,
                    text = text
                });
                BxCTO.Br();
                BxCTO.alignx = ALIGN.CENTER;
                BxCTO.addButton(new()
                {
                    title = TX.Get("Submit"),
                    fnClick = B =>
                    {
                        BxCTO.deactivate();
                        BxCTO = null;
                        TimeoutText = null;
                        submit?.SetLocked(false);
                        submit?.Select(true);
                        return true;
                    }
                });
                BxCTO.Focusable(true, true);
            }
            else if (TimeoutText != null)
            {
                TimeoutText.text_content = text;
            }
            BxCTO.activate();
            BxCTO.Focus();
        }

        /// <summary>标题 → 游戏：写档与加载目标在这里落地，连接不断开。</summary>
        static void EnterGame(SceneTitleTemp stt, SVD.sFile file, bool ignore_svd_cfg)
        {
            DB.PolarisNoelsClientTransferNotComplete = false;
            RoomCodeText = null;
            WaitText = null;
            BxHRC?.deactivate();
            BxHRC = null;
            BxWait?.deactivate();
            BxWait = null;
            COOK.clear(false);
            COOK.save_failure_announce = "";
            COOK.setLoadTarget(file, ignore_svd_cfg);
            stt.changeState(SceneTitleTemp.STATE.START_GAME);
        }

        static void CreateUI(UiBoxDesigner designer, FnBtnBindings submitFn, FnBtnBindings cancel, bool isHost, out aBtn submitBtn)
        {
            designer.selectable_loop = 3;
            designer.alignx = ALIGN.CENTER;
            designer.addP(new()
            {
                TxCol = ColorDefault,
                size = 30f,
                text = isHost ? TX.Get("multiplayer_host_title") : TX.Get("multiplayer_client_title")
            });
            designer.addHr(new()
            {
                margin_t = 5f,
                margin_b = 5f
            });
            if (isHost)
            {
                designer.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 22f,
                    text = TX.Get("multiplayer_room_code_hint")
                });
            }
            else
            {
                designer.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 22f,
                    text = TX.Get("multiplayer_room_code")
                });
                RoomCodeInput = designer.addInput(new()
                {
                    h = 30f,
                    label = TX.Get("multiplayer_room_code") + ":"
                });
                designer.Br();
                designer.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 18f,
                    text = TX.Get("multiplayer_direct_hint")
                });
                DirectIpInput = designer.addInput(new()
                {
                    h = 30f,
                    label = "IP:"
                });
                designer.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 20f,
                    text = TX.Get("multiplayer_con")
                });
                PortCon = designer.addNumCounterT<aBtnNumCounter>(new()
                {
                    h = 30f,
                    digit = 5,
                    maxval = 65535
                });
                PortCon.setValue(47210);
                RoomCodeInput.setNaviR(DirectIpInput, false, true);
                DirectIpInput.setNaviL(RoomCodeInput, false, true);
                DirectIpInput.setNaviR(PortCon.Get(0), false, true);
                PortCon.Get(0).setNaviL(DirectIpInput, false, true);
            }
            designer.Br();
            designer.alignx = ALIGN.CENTER;
            designer.addP(new()
            {
                TxCol = ColorDefault,
                size = 20f,
                text = TX.Get("multiplayer_nickname")
            });
            NickNameInput = designer.addInput(new()
            {
                h = 20f
            });
            if (!isHost)            {
                PortCon.Get(4).setNaviR(NickNameInput, false, true);
                NickNameInput.setNaviT(RoomCodeInput);
            }
            designer.Br();
            designer.alignx = ALIGN.CENTER;
            designer.addP(new()
            {
                TxCol = ColorDefault,
                size = 20f,
                text = TX.Get("multiplayer_select_noel")
            });
            string[] noels = [TX.Get("multiplayer_noel"), TX.Get("multiplayer_noel_inverse"), TX.Get("multiplayer_noel_red"), TX.Get("multiplayer_noel_orange"), TX.Get("multiplayer_noel_yellow"), TX.Get("multiplayer_noel_green"), TX.Get("multiplayer_noel_cyan"), TX.Get("multiplayer_noel_blue"), TX.Get("multiplayer_noel_purple"), TX.Get("multiplayer_noel_magenta")];
            var slider = designer.addSliderCT(new()
            {
                mn = 0,
                mx = 9,
                checkbox_mode = 2,
                Adesc_keys = noels,
                fnChanged = (_b, p_v, c_v) =>
                {
                    Preview.GetComponent<NoelPreview>().noelType = c_v == 0 ? NoelType.Normal : (c_v == 1 ? NoelType.Inverse : NoelType.ColorNoel);
                    Preview.GetComponent<NoelPreview>().color = (ColorNoelColor)(c_v - 2);
                    if (c_v > 1)
                    {
                        type = NoelType.ColorNoel;
                        color = (ColorNoelColor)(c_v - 2);
                        return true;
                    }
                    type = (NoelType)c_v;
                    return true;
                }
            }, 180);
            designer.Br();
            designer.addP(new()
            {
                TxCol = ColorDefault,
                size = 20f,
                text = TX.Get("multiplayer_invisible_nickname")
            });
            string[] array = TX.GetArray("Disabled", "Enabled");
            var slider1 = designer.addSliderCT(new()
            {
                checkbox_mode = 1,
                Adesc_keys = array,
                fnChanged = (_b, p_v, c_v) =>
                {
                    InvisibleNickname = c_v == 1;
                    return true;
                }
            });
            designer.Br();
            designer.alignx = ALIGN.CENTER;
            designer.item_margin_x_px = 0f;
            float btnW = (designer.use_w - designer.item_margin_x_px) / 2f - 100f;
            float btnH = 30f;
            submitBtn = designer.addButton(new()
            {
                title = "&&Submit",
                w = btnW,
                h = btnH,
                fnClick = submitFn
            });
            designer.addP(new()
            {
                text = "   "
            });
            var cancelBtn = designer.addButton(new()
            {
                title = "&&Cancel",
                w = btnW,
                h = btnH,
                fnClick = cancel
            });
            submitBtn.setNaviR(cancelBtn, true, true);
            cancelBtn.setNaviR(submitBtn, true, true);
            cancelBtn.setNaviT(slider1, false, true);
            designer.Br();
            BxCmd.activate();
            BxCmd.Clear();
            BxCmd.getBox().frametype = UiBox.FRAMETYPE.ONELINE;
            BxCmd.WH(150f, 300f);
            BxCmd.margin_in_lr = 10f;
            BxCmd.margin_in_tb = 10f;
            BxCmd.init();
            Preview = new();
            Preview.AddComponent<SpriteRenderer>();
            Preview.AddComponent<NoelPreview>();
            Preview.SetActive(false);
            BxCmd.addGameObject(Preview, "preview");
            Preview.SetActive(true);
            Preview.transform.position = BxCmd.transform.position;
            Preview.transform.position += new Vector3(-0.6f, -1.6f);
            Preview.transform.localScale *= 2;
            Vector3 btnPos = slider.transform.position;
            float targetX = btnPos.x * 64f + 430f;
            float targetY = btnPos.y * 64f;
            BxCmd.posSetDA(targetX, targetY, 0, 20f, true);
            BxCmd.Focusable(false, false);
            submitBtn.Select(true);
        }

        static Color ColorDefault => Color.HSVToRGB(0, 0, 0.219f);

        static UiBoxDesigner BxCmd;

        static GameObject Preview;
    }
}
