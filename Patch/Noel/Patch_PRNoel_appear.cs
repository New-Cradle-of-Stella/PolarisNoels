using HarmonyLib;
using nel;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(PRNoel), nameof(PRNoel.appear))]
    public class Patch_PRNoel_appear
    {
        [HarmonyPostfix]
        static void Postfix(PRNoel __instance)
            => NetworkBootstrap.OnNoelAppear(__instance);
    }
}
