using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace WeNeedMoreNoels.Patch
{
    [HarmonyPatch(typeof(nel.title.SceneTitleTemp), "initButtons")]
    public class Patch_SceneTitleTemp_initButtons
    {
        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var field = AccessTools.Field(__instance.GetType(), "Atop_btn_keys");
            var oldArray = field.GetValue(__instance) as string[];
            string newStr = "&&btn_multiplayer";
            string[] newArray = new string[oldArray.Length + 1];
            oldArray.CopyTo(newArray, 0);
            newArray[newArray.Length - 1] = newArray[newArray.Length - 2];
            newArray[newArray.Length - 2] = newArray[newArray.Length - 3];
            newArray[newArray.Length - 3] = newStr;
            field.SetValue(__instance, newArray);
        }

        /// <summary>
        /// 顶部按钮由 4 个增加到 5 个。HarmonyX 会把 ldc.i4.4 展开为 ldc.i4 4，
        /// 所以这里按"值"而不是按具体操作码匹配。
        /// </summary>
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            bool clmsDone = false, widthDone = false, poolDone = false;
            for (int i = 1; i < list.Count; i++)
            {
                if (!clmsDone && StoresField(list[i], "DsnDataRadio", "clms") && IsInt(list[i - 1], 4))
                {
                    SetInt(list[i - 1], 5);
                    clmsDone = true;
                }
                else if (!widthDone && StoresField(list[i], "DsnDataRadio", "w") && i >= 2 && IsFloat(list[i - 2], 4f))
                {
                    list[i - 2].operand = 5f;
                    widthDone = true;
                }
                else if (!poolDone && StoresField(list[i], "BtnContainer", "APool") && i >= 2 && IsInt(list[i - 2], 4))
                {
                    SetInt(list[i - 2], 5);
                    poolDone = true;
                }
            }
            if (!clmsDone || !widthDone || !poolDone)
            {
                Plugin.Logger.LogWarning($"initButtons transpiler incomplete: clms={clmsDone} width={widthDone} pool={poolDone}");
                var sample = list.Find(c => c.opcode == OpCodes.Stfld && c.operand != null);
                Plugin.Logger.LogWarning($"stfld operand sample: {sample?.operand?.GetType().FullName} / {sample?.operand}");
            }
            return list;
        }

        /// <summary>
        /// 按"类型名::字段名"匹配 stfld。HarmonyX 的操作数可能是 FieldInfo，
        /// 也可能是 Cecil 的 FieldReference，两者都转成文本比较。
        /// </summary>
        static bool StoresField(CodeInstruction ci, string typeName, string fieldName)
        {
            if (ci.opcode != OpCodes.Stfld || ci.operand == null)
            {
                return false;
            }
            if (ci.operand is System.Reflection.FieldInfo fi)
            {
                return fi.Name == fieldName && fi.DeclaringType != null && fi.DeclaringType.Name.StartsWith(typeName);
            }
            string text = ci.operand.ToString();
            return text.Contains(typeName) && text.EndsWith("::" + fieldName);
        }

        static bool IsInt(CodeInstruction ci, int value)
        {
            if (ci.opcode == OpCodes.Ldc_I4_0) return value == 0;
            if (ci.opcode == OpCodes.Ldc_I4_1) return value == 1;
            if (ci.opcode == OpCodes.Ldc_I4_2) return value == 2;
            if (ci.opcode == OpCodes.Ldc_I4_3) return value == 3;
            if (ci.opcode == OpCodes.Ldc_I4_4) return value == 4;
            if (ci.opcode == OpCodes.Ldc_I4_5) return value == 5;
            return (ci.opcode == OpCodes.Ldc_I4 || ci.opcode == OpCodes.Ldc_I4_S) && System.Convert.ToInt32(ci.operand) == value;
        }

        static bool IsFloat(CodeInstruction ci, float value)
        {
            return ci.opcode == OpCodes.Ldc_R4 && (float)ci.operand == value;
        }

        static void SetInt(CodeInstruction ci, int value)
        {
            ci.opcode = OpCodes.Ldc_I4;
            ci.operand = value;
        }
    }
}
