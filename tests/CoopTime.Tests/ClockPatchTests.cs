using System.Reflection;
using System.Reflection.Emit;
using CoopTime.Mod;
using HarmonyLib;
using Xunit;

namespace CoopTime.Tests;

public sealed class ClockPatchTests
{
    [Fact]
    public void RewriterChangesOnlyTwoSitesAndPreservesOriginalInstructions()
    {
        List<CodeInstruction> source = Source();
        IReadOnlyList<CodeInstruction> patched = Rewrite(source);
        Assert.Equal(source.Count, patched.Count);
        Assert.Equal(OpCodes.Add, source[2].opcode);
        Assert.True(source[6].Calls(Method(nameof(SourceCadence))));
        Assert.True(patched[2].Calls(Method(nameof(AdjustedInterval))));
        Assert.True(patched[6].Calls(Method(nameof(SinglePlayerCadence))));
        for (int i = 0; i < source.Count; i++)
        {
            Assert.NotSame(source[i], patched[i]);
            if (i != 2 && i != 6)
            {
                Assert.Equal(source[i].opcode, patched[i].opcode);
                Assert.Equal(source[i].operand, patched[i].operand);
            }
        }
    }

    [Fact]
    public void LabelsAndExceptionBlocksSurviveWithoutSharingMutableLists()
    {
        List<CodeInstruction> source = Source();
        DynamicMethod generator = new("Labels", typeof(void), Type.EmptyTypes);
        source[2].labels.Add(generator.GetILGenerator().DefineLabel());
        source[2].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        IReadOnlyList<CodeInstruction> patched = Rewrite(source);
        Assert.Equal(source[2].labels, patched[2].labels);
        Assert.Equal(source[2].blocks, patched[2].blocks);
        Assert.NotSame(source[2].labels, patched[2].labels);
        Assert.NotSame(source[2].blocks, patched[2].blocks);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("no-add")]
    [InlineData("no-cadence")]
    [InlineData("double-add")]
    [InlineData("double-cadence")]
    [InlineData("wrong-field")]
    public void UnsupportedShapesFailBeforeMutatingInput(string shape)
    {
        List<CodeInstruction> source = Source();
        switch (shape)
        {
            case "empty": source.Clear(); break;
            case "no-add": source[2] = new CodeInstruction(OpCodes.Sub); break;
            case "no-cadence": source[6] = new CodeInstruction(OpCodes.Nop); break;
            case "double-add": source.AddRange(Source().Take(4)); break;
            case "double-cadence": source.Add(new CodeInstruction(OpCodes.Call, Method(nameof(SourceCadence)))); break;
            case "wrong-field": source[3] = new CodeInstruction(OpCodes.Stsfld, typeof(int).GetField(nameof(int.MinValue))); break;
        }

        (OpCode OpCode, object? Operand)[] before = source.Select(op => (op.opcode, (object?)op.operand)).ToArray();
        Assert.Throws<InvalidOperationException>(() => Rewrite(source));
        Assert.Equal(before, source.Select(op => (op.opcode, (object?)op.operand)).ToArray());
    }

    [Theory]
    [InlineData(100, 10, 125)]
    [InlineData(0, 0, 0)]
    [InlineData(3000, 16, 3040)]
    [InlineData(6, 3, 13)]
    public void RewrittenSyntheticClockActuallyExecutesWithValidStack(int interval, int elapsed, int expected)
    {
        IReadOnlyList<CodeInstruction> patched = Rewrite(Source());
        DynamicMethod method = new("SyntheticClock", typeof(int), new[] { typeof(int), typeof(int) });
        ILGenerator il = method.GetILGenerator();
        LocalBuilder slot = il.DeclareLocal(typeof(int));
        foreach (CodeInstruction op in patched)
        {
            // Replace the synthetic field slot with a local; no process-global state is written.
            if (op.opcode == OpCodes.Stsfld)
            {
                il.Emit(OpCodes.Stloc, slot);
            }
            else if (op.opcode == OpCodes.Ldsfld)
            {
                il.Emit(OpCodes.Ldloc, slot);
            }
            else if (op.operand is MethodInfo target)
            {
                il.Emit(op.opcode, target);
            }
            else
            {
                il.Emit(op.opcode);
            }
        }

        Func<int, int, int> evaluate = method.CreateDelegate<Func<int, int, int>>();
        Assert.Equal(expected, evaluate(interval, elapsed));
    }

    public static int SourceCadence(int value) => value;
    public static int SinglePlayerCadence(int value) => value * 2;
    public static int AdjustedInterval(int interval, int elapsed) => interval + elapsed / 2;

    private static FieldInfo Slot => typeof(int).GetField(nameof(int.MaxValue))!;
    private static MethodInfo Method(string name) => typeof(ClockPatchTests).GetMethod(name)!;
    private static IReadOnlyList<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source) =>
        GameClockRewriter.Rewrite(source, Slot, Method(nameof(SourceCadence)),
            Method(nameof(AdjustedInterval)), Method(nameof(SinglePlayerCadence)));

    private static List<CodeInstruction> Source() => new()
    {
        new(OpCodes.Ldarg_0), new(OpCodes.Ldarg_1), new(OpCodes.Add), new(OpCodes.Stsfld, Slot),
        new(OpCodes.Ldsfld, Slot), new(OpCodes.Ldarg_1), new(OpCodes.Call, Method(nameof(SourceCadence))),
        new(OpCodes.Add), new(OpCodes.Ret)
    };
}
