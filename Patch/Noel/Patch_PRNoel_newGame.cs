using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(PRNoel), nameof(PRNoel.newGame))]
    public class Patch_PRNoel_newGame
    {
        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            DB.MainPR = (PRNoel)__instance;
        }
    }
}
