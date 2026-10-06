using HarmonyLib;
using nel;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace PolarisNoels.Patch
{
    [HarmonyPatch(typeof(MgFireBall), nameof(MgFireBall.fnManipulateFireBall))]
    public class Patch_MgFireBall_fnManipulateFireBall
    {
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            var ptcMethod = AccessTools.Method(typeof(MagicItem), "PtcST");
            var notifyMethod = AccessTools.Method(typeof(MagicBroadcaster), nameof(MagicBroadcaster.OnFireBallTurn));
            codeMatcher.MatchStartForward(new CodeMatch(OpCodes.Callvirt, ptcMethod),
                                          new CodeMatch(OpCodes.Pop))
                       .Advance(2)
                       .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_1),
                                         new CodeInstruction(OpCodes.Ldarg_2),
                                         new CodeInstruction(OpCodes.Call, notifyMethod));
            return codeMatcher.Instructions();
        }
    }
}
