using HarmonyLib;
using nel.title;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(SceneTitleTemp), nameof(SceneTitleTemp.runIRD))]
    public class Patch_SceneTitleTemp_runIRD
    {
        [HarmonyPrefix]
        static bool Prefix(SceneTitleTemp __instance, ref bool __result)
            => MultiplayerTitleFlow.OnRunIRD(__instance, ref __result);

        [HarmonyPostfix]
        static void Postfix(SceneTitleTemp __instance)
            => MultiplayerTitleFlow.AfterRunIRD(__instance);
    }
}
