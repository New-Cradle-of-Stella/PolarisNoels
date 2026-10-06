using evt;
using m2d;
using nel;
using XX;

namespace PolarisNoels
{
    /// <summary>跨地图传送复用原版事件加载器，资源和图集就绪后才定位角色。</summary>
    public static class MapTeleport
    {
        const string EventName = "%POLARISNOELS_TRANSFER";

        public static void Transfer(string key, float x, float y)
        {
            if (DB.MainPR?.Mp == null || DB.IsInBattle || string.IsNullOrEmpty(key)
                || float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return;

            NelM2DBase m2d = DB.MainPR.NM2D;
            if (m2d.transferring_game_stopping || EV.isActive()) return;
            Map2d destination = m2d.Get(key, no_error: true);
            if (destination == null || destination.is_whole)
            {
                Plugin.Logger.LogWarning($"teleport destination is not a playable map: {key}");
                return;
            }
            if (m2d.curMap == destination)
            {
                M2LpMapTransferBase.executeTransferFastTravel(destination, (int)x, (int)y);
                return;
            }

            // UiBenchMenu.ExecuteFastTravel 先 INIT_MAP_MATERIAL，再 WAIT_FN MAP_TRANSFER。
            // 直接调用 executeTransferFastTravel 会绕过资源加载，changeMap 后图块/碰撞数据可能未就绪。
            using STB script = TX.PopBld();
            script.AR("UIGM DEACTIVATE");
            script.AR("DENY_SKIP");
            script.AR("STOP_LETTERBOX");
            script.AR("MAPTITLE_HIDE");
            script.AR("SEND_EVENT_CORRUPTION PRE_UNLOAD");
            script.AR("VALOTIZE");
            script.Add("PIC_FILL &9 ").AddColor(NEL.FillingBgCol.rgba).Ret();
            script.AR("PIC_FADEIN &9 10");
            script.AR("WAIT 10");

            bool flushMaterials = m2d.WM.GetWholeFor(destination) != m2d.WM.CurWM || m2d.needInitMaterial(destination);
            if (flushMaterials) script.AR("ADD_MAPFLUSH_FLAG");
            script.Add("INIT_MAP_BGM '").Add(destination.key).AR("'");
            script.Add("INIT_MAP_MATERIAL_EV_FLUSHABLE '").Add(destination.key).Add("' ")
                .Add(flushMaterials ? 2 : 1).Ret();
            script.AR("ENABLE_PUTTED_MAGIC_SAVE");
            script.AR("WAIT_FN MAP_TRANSFER");
            script.Add("NEL_EXECUTE_FAST_TRAVEL '").Add(destination.key).Add("' ")
                .Add((int)x).Add(" ").Add((int)y).AR(" 40");
            script.AR("PIC_FADEOUT &9 20");
            script.AR("ALLOW_SKIP");
            script.AR("WAIT_MOVE");

            EvReader reader = new(EventName);
            reader.parseText(script.ToString());
            EV.stackReader(reader);
        }
    }
}
