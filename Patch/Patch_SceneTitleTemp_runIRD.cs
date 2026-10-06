using HarmonyLib;
using LiteNetLib;
using nel;
using nel.title;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using UnityEngine;
using WeNeedMoreNoels.DataStruct;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SceneTitleTemp), nameof(SceneTitleTemp.runIRD))]
    public class Patch_SceneTitleTemp_runIRD
    {
        static UiBoxDesigner BxHC;

        static UiBoxDesigner BxHBP;

        static UiBoxDesigner BxCC;

        static UiBoxDesigner BxCTO;

        static UiBoxDesigner BxHCI;

        static aBtn submit;

        static NoelType type;

        static ColorNoelColor color;

        static LabeledInputField IpInput;

        static BtnContainerNumCounter<aBtnNumCounter> PortCon;

        static LabeledInputField NickNameInput;

        static NetManager client;

        static SceneTitleTemp stt;

        static bool InvisibleNickname;

        static aBtn BtnCC;

        static bool Connected;

        [HarmonyPrefix]
        static bool Prefix(object __instance, ref bool __result)
        {
            client?.PollEvents();
            stt = (SceneTitleTemp)__instance;
            if (BxCmd == null && stt.BxCon != null)
            {
                BxCmd = stt.BxCon.Create("ColN", 0f, 0f, 200f, 200f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
            }
            if (DB.WNMNHostClosed)
            {
                if (stt.BxCon is null)
                {
                    __result = true;
                    return true;
                }
                DB.WNMNHostClosed = false;
                BxHCI = stt.BxCon.Create("hostClosedInfo", 0f, 0f, 380f, IN.h - 620f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                BxHCI.Focusable(false, false, null);
                BxHCI.Clear();
                BxHCI.addP(new()
                {
                    TxCol = ColorDefault,
                    size = 40,
                    alignx = ALIGN.CENTER,
                    aligny = ALIGNY.MIDDLE,
                    text = DB.WNMNHostKicked ? TX.Get("multiplayer_host_kicked") : TX.Get("multiplayer_host_closed")
                });
                DB.WNMNHostKicked = false;
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
            if (stt.state == SceneTitleTemp.STATE.SVD_SELECT && DB.WNMNHostSelectSVD)
            {
                if (stt.EditSvd is not null && stt.EditSvd.ui_state == UiSVD.STATE.LOAD_SUCCESS)
                {
                    bool ignore_svd_cfg = stt.EditSvd.ignore_svd_cfg;
                    SVD.sFile file = SVD.GetFileB(UiSVD.last_focused_bindex, true);
                    byte[] buffer = File.ReadAllBytes(Path.Combine(SVD.getDir(), SVD.getFileName(file)));
                    DB.SyncSaveContentBuffer = buffer;
                    stt.EditSvd.deactivateDesigner();
                    stt.BxR.deactivate();
                    stt.BxDesc.deactivate();
                    BxHC = stt.BxCon.Create("hostConfirm", 0f, 0f, 620f, IN.h - 360f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                    BxHC.Clear();
                    BxHBP = stt.BxCon.Create("hostBusyPort", 0f, 0f, 380f, (IN.h - 620f) * 1.5f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                    IN.setZ(BxHBP.transform, BxHC.transform.position.z - 1f);
                    BxHBP.alignx = ALIGN.CENTER;
                    BxHBP.addP(new()
                    {
                        TxCol = ColorDefault,
                        size = 40,
                        text = TX.Get("multiplayer_busy_port")
                    });
                    BxHBP.Br();
                    BxHBP.alignx = ALIGN.CENTER;
                    BxHBP.addButton(new()
                    {
                        title = TX.Get("Submit"),
                        fnClick = B =>
                        {
                            BxHBP.deactivate();
                            submit.Select(true);
                            return true;
                        }
                    });
                    BxHBP.Focusable(true, true);
                    BxHBP.deactivate();
                    CreateUI(BxHC, b =>
                    {
                        //端口检查
                        IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                        var busyPort = properties.GetActiveTcpListeners().Any(x => x.Port == PortCon.cnt_val);
                        busyPort |= properties.GetActiveUdpListeners().Any(x => x.Port == PortCon.cnt_val);
                        if (busyPort)
                        {
                            BxHBP.Focus();
                            BxHBP.activate();
                            return true;
                        }
                        BxCmd?.deactivate();
                        WNMNTools.NetworkConfig config = new()
                        {
                            Type = NetWorkType.Host,
                            ip = "",
                            port = PortCon.cnt_val,
                            nickName = NickNameInput.text,
                            NoelType = type,
                            NoelColor = color,
                            InvisibleNickname = InvisibleNickname
                        };
                        DB.InitConfig = config;
                        BxHC.deactivate();
                        BxHC = null;
                        COOK.clear(false);
                        COOK.save_failure_announce = "";
                        COOK.setLoadTarget(file, ignore_svd_cfg);
                        stt.changeState(SceneTitleTemp.STATE.START_GAME);
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
            if (BxHCI is not null || BxHC is not null || BxCC is not null || DB.WNMNClientTransferNotComplete)
            {
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            SceneTitleTemp stt = (SceneTitleTemp)__instance;
            if (DB.WNMNEnterNetworkTypeSelected)
            {
                DB.WNMNEnterNetworkTypeSelected = false;
                stt.BxDiff.deactivate();
                if (DB.WNMNEnterNetworkType == NetWorkType.Host)
                {
                    stt.changeState(SceneTitleTemp.STATE.SVD_SELECT);
                    DB.WNMNHostSelectSVD = true;
                }
                else
                {
                    BxCC = stt.BxCon.Create("clientConfirm", 0f, 0f, 620f, IN.h - 360f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                    BxCC.Clear();
                    CreateUI(BxCC, b =>
                    {
                        BxCTO = stt.BxCon.Create("clientTimeOut", 0f, 0f, 380f, (IN.h - 620f) * 1.5f, 0, 0f, UiBoxDesignerFamily.MASKTYPE.BOX);
                        IN.setZ(BxCTO.transform, BxCC.transform.position.z - 1f);
                        BxCTO.alignx = ALIGN.CENTER;
                        BxCTO.addP(new()
                        {
                            TxCol = ColorDefault,
                            size = 40,
                            text = TX.Get("multiplayer_connect_timeout")
                        });
                        BxCTO.Br();
                        BxCTO.alignx = ALIGN.CENTER;
                        BxCTO.addButton(new()
                        {
                            title = TX.Get("Submit"),
                            fnClick = B =>
                            {
                                BxCTO.deactivate();
                                submit.SetLocked(false);
                                submit.Select(true);
                                return true;
                            }
                        });
                        BxCTO.Focusable(true, true);
                        BxCTO.deactivate();
                        BtnCC = b;
                        b.SetLocked(true);
                        EventBasedNetListener listener = new();
                        client = new(listener);
                        listener.NetworkReceiveEvent += (peer, reader, deliveryMethod) =>
                        {
                            BxCmd?.deactivate();
                            WNMNTools.NetworkConfig config = new()
                            {
                                Type = NetWorkType.Client,
                                ip = IpInput.text,
                                port = PortCon.cnt_val,
                                nickName = NickNameInput.text,
                                NoelType = type,
                                NoelColor = color,
                                InvisibleNickname = InvisibleNickname
                            };
                            DB.InitConfig = config;
                            Plugin.Logger.LogInfo("Client received sync save data.");
                            byte[] receivedData = new byte[reader.UserDataSize];
                            reader.GetBytes(receivedData, reader.UserDataSize);
                            File.WriteAllBytes(SVD.getDir() + "\\" + DB.SYNC_FILE_NAME, receivedData);
                            SVD.sFile file = new(-2, true);//-2为同步存档
                            BxCC.deactivate();
                            BxCC = null;
                            DB.WNMNClientTransferNotComplete = false;
                            client.DisconnectAll();
                            client.Stop();
                            COOK.clear(false);
                            COOK.save_failure_announce = "";
                            COOK.setLoadTarget(file, true);
                            stt?.changeState(SceneTitleTemp.STATE.START_GAME);
                            reader.Recycle();
                            Connected = true;
                        };
                        client.Start();
                        client.Connect(IpInput.text, PortCon.cnt_val + 1, DB.TRANSFER_ACCESS_KEY);
                        Plugin.PluginInstance.StartCoroutine(CheckTimeout(5, client));
                        Plugin.Logger.LogInfo($"Starting connect {IpInput.text}:{PortCon.cnt_val}");
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

        static IEnumerator CheckTimeout(float t, NetManager client)
        {
            float curt = Time.time;
            while (Time.time - curt < t)
            {
                yield return 0;
            }
            if (!Connected)
            {
                client.Stop();
                BxCTO.activate();
                BxCTO.Focus();
            }
        }

        static void CreateUI(UiBoxDesigner designer, FnBtnBindings submit, FnBtnBindings cancel, bool isHost, out aBtn submitBtn)
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
                    size = 30f,
                    text = TX.Get("multiplayer_host_port")
                });
                designer.addP(new()
                {
                    text = " "
                });
                PortCon = designer.addNumCounterT<aBtnNumCounter>(new()
                {
                    h = 30f,
                    digit = 5,
                    maxval = 99999
                });
                PortCon.Get(0).setNaviL(NickNameInput, false, true);
            }
            else
            {
                IpInput = designer.addInput(new()
                {
                    h = 30f,
                    label = "IP:"
                });
                IpInput.text = "localhost";
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
                IpInput.setNaviR(PortCon.Get(0), false, true);
                PortCon.Get(0).setNaviL(IpInput, false, true);
            }
            PortCon.setValue(47210);
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
            PortCon.Get(4).setNaviR(NickNameInput, false, true);
            NickNameInput.setNaviT(PortCon.Get(0));
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
                fnClick = submit
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

    public enum MultiPlayerSTATE
    {
        IDLE,
        Type,
        Host_SVD,
        Host_Confirm,
        Client_Confirm
    }
}
