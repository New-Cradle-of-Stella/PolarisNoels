using HarmonyLib;
using m2d;
using nel;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace WeNeedMoreNoels.Patch
{//PR _Pr, M2PxlAnimatorRT _Anm, PrPoseContainer _PCon, bool execute_initS = true
    [HarmonyPatch(typeof(PrNoelAnimator), MethodType.Constructor, [typeof(PR), typeof(M2PxlAnimatorRT), typeof(PrPoseContainer), typeof(bool)])]
    public class Patch_PrNoelAnimator_ctor
    {
        [HarmonyPatch]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var match = new CodeMatcher(instructions);
            match.MatchStartForward(new CodeMatch(OpCodes.Cgt_Un));
            match.Advance(2);
            match.InsertAndAdvance([new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(nel.PrNoelAnimator), "is_noel"))]);
            return match.Instructions();
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            FieldInfo field = AccessTools.Field(typeof(nel.PrNoelAnimator), "is_noel");
            field.SetValue(__instance, __instance is PRNoel);
        }
    }
}
