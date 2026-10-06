using HarmonyLib;
using nel;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiSVD), nameof(UiSVD.fnClickSelecting))]
    public class Patch_UiSVD_fnClickSelecting
    {
        [HarmonyPrefix]
        static bool Prefix(aBtn B)
        {
            string title = B.title;
            if (DB.IsMultiplayer && (title == "&&SVD_cmd_load" || title == "&&SVD_cmd_load_do"))
            {
                SND.Ui.play("Locked");
                return false;
            }
            return true;
        }
    }
}
