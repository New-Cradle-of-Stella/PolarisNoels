using HarmonyLib;
using nel;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(PRNoel), nameof(PRNoel.createAnimator))]
    public class Patch_PRNoel_createAnimator
    {
        [HarmonyPrefix]
        static bool Prefix(PRNoel __instance, ref PrAnimator Anm)
            => !NoelAnimatorFactory.TryCreate(__instance, ref Anm);
    }
}
