using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CoopTime.Mod;

internal static class GameClockRewriter
{
    public static IReadOnlyList<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,
        FieldInfo interval, MethodInfo extra, MethodInfo adjust, MethodInfo cadence)
    {
        List<CodeInstruction> original = instructions.ToList();
        int[] additions = Enumerable.Range(1, Math.Max(0, original.Count - 1))
            .Where(i => original[i].opcode == OpCodes.Stsfld && Equals(original[i].operand, interval)
                && original[i - 1].opcode == OpCodes.Add).ToArray();
        int[] cadences = Enumerable.Range(0, original.Count).Where(i => original[i].Calls(extra)).ToArray();
        if (additions.Length != 1 || cadences.Length != 1)
        {
            throw new InvalidOperationException($"Unsupported UpdateGameClock IL: {additions.Length} increments, {cadences.Length} cadences.");
        }

        List<CodeInstruction> result = original.Select(op => new CodeInstruction(op)).ToList();
        result[additions[0] - 1].opcode = OpCodes.Call;
        result[additions[0] - 1].operand = adjust;
        result[cadences[0]].opcode = OpCodes.Call;
        result[cadences[0]].operand = cadence;
        return result;
    }
}
