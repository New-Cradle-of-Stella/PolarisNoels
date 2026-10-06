using nel;
using nel.gm;
using XX;

namespace PolarisNoels
{
    public static class UiMenuMul
    {
        public static bool IsMulCata;

        public static UiBoxDesigner BxP;
        public static UiBoxDesigner BxPD;

        public static aBtn SendMsgButton;

        public static UiBoxDesigner BxSB;
        public static UiBoxDesigner BxSSI;

        public static UiBoxDesigner BxSS;

        public static UiBoxDesigner BxSL;

        /// <summary>在游戏菜单左侧分类栏末尾加入「联机」分类按钮。</summary>
        public static void AddCategoryButton(UiGameMenu menu)
        {
            Designer bxCategory = menu.BxCategory;
            DsnDataButton dsn = new()
            {
                name = "categ_10",
                skin = "ui_category",
                skin_title = TX.Get("multiplayer_cata"),
                w = bxCategory.use_w,
                h = (bxCategory.h - bxCategory.margin_in_tb) / 11f - 8f,
                hover_to_select = true,
                fnClick = B =>
                {
                    UiMenuMul.IsMulCata = true;
                    menu.initCategoryEdit((CATEG)10, true);
                    UiMenuMul.SendMsgButton.Select(true);
                    return true;
                },
                fnOut = B =>
                {
                    menu.fnOutCategory(B);
                    return true;
                },
                fnHover = B => {
                    menu.appearCategory((CATEG)10);
                    return true;
                }
            };
            bxCategory.addButton(dsn);
        }
    }
}
