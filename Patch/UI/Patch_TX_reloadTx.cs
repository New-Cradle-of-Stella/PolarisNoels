using HarmonyLib;
using XX;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(TX), nameof(TX.reloadTx))]
    public class Patch_TX_reloadTx
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            TX.readTextsAt(MTRExtension.LOCALIZATION_FILE_NAME);
        }
    }
}
