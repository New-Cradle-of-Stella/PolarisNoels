using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using nel.gm;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(UiGameMenu), "remakeLeftCategories")]
    public class Patch_UiGameMenu_remakeLeftCategories
    {
        [HarmonyPostfix]
        private static void Postfix(UiGameMenu __instance)
        {
            if (!DB.IsMultiplayer)
            {
                return;
            }
            UiMenuMul.AddCategoryButton(__instance);  
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            CodeMatcher codeMatcher = new(instructions, null);
            codeMatcher.MatchStartForward(
            [
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Sub),
                new CodeMatch(OpCodes.Ldc_R4)
            ])
                .Advance(3)
                .SetOperandAndAdvance(11f);
            return codeMatcher.Instructions();
        }
    }
}
