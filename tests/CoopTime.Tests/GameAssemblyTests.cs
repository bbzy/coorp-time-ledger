using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using Xunit.Abstractions;

namespace CoopTime.Tests;

// Cecil reads IL as data. These checks never load or execute game assemblies.
public sealed class GameAssemblyTests
{
    public GameAssemblyTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void InstalledClockHasExactlyThePatchSitesWeExpect()
    {
        using ModuleDefinition module = ReadGame();
        TypeDefinition game = module.Types.Single(t => t.FullName == "StardewValley.Game1");
        MethodDefinition clock = game.Methods.Single(m => m.Name == "UpdateGameClock");
        Instruction[] il = clock.Body.Instructions.ToArray();
        Assert.Single(il.Where((op, i) => i > 0 && op.OpCode == OpCodes.Stsfld
            && op.Operand is FieldReference { Name: "gameTimeInterval" }
            && il[i - 1].OpCode == OpCodes.Add));
        Assert.Single(il, op => op.Operand is MethodReference { Name: "get_ExtraMillisecondsPerInGameMinute" });
        Assert.Contains(il, op => op.Operand is MethodReference { Name: "get_Milliseconds" });
        Assert.Contains(il, op => op.Operand is MethodReference { Name: "shouldTimePass" });
        Assert.Contains(il, op => op.Operand is MethodReference { Name: "get_IsClient" });
    }

    [Fact]
    public void NativePausePredicateIncludesSinglePlayerAndToolBranches()
    {
        using ModuleDefinition module = ReadGame();
        MethodDefinition pause = module.Types.Single(t => t.FullName == "StardewValley.Game1")
            .Methods.Single(m => m.Name == "shouldTimePass");
        Assert.Single(pause.Parameters);
        Assert.Equal("ignore_multiplayer", pause.Parameters[0].Name);
        Assert.Contains(pause.Body.Instructions, op => op.Operand is MethodReference { Name: "get_IsMultiplayer" });
        Assert.Contains(pause.Body.Instructions, op => op.Operand is MethodReference { Name: "get_UsingTool" });
        Assert.Contains(pause.Body.Instructions, op => op.OpCode == OpCodes.Isinst
            && op.Operand is TypeReference { Name: "BobberBar" });
        Assert.Contains(pause.Body.Instructions, op => op.Operand is FieldReference { Name: "forceTimePass" });
    }

    [Fact]
    public void OuterUpdateAndSkullCavernRulesMatchTheAdapterAssumptions()
    {
        using ModuleDefinition module = ReadGame();
        MethodDefinition update = module.Types.Single(t => t.FullName == "StardewValley.Game1")
            .Methods.Single(m => m.Name == "_update");
        foreach (string name in new[] { "currentMinigame", "activeClickableMenu", "viewingLocation", "pauseWhenOutOfFocus" })
        {
            Assert.True(update.Body.Instructions.Any(op => op.Operand is MemberReference member
                && (member.Name == name || member.Name == $"get_{name}")), $"Missing outer clock gate: {name}");
        }

        TypeDefinition mine = module.Types.Single(t => t.FullName == "StardewValley.Locations.MineShaft");
        Assert.Contains(mine.Methods.Where(m => m.HasBody), method =>
            method.Body.Instructions.Any(op => op.Operand is MethodReference { Name: "set_ExtraMillisecondsPerInGameMinute" })
            && method.Body.Instructions.Any(op => op.OpCode == OpCodes.Ldc_I4 && Equals(op.Operand, 200)));
    }

    private ModuleDefinition ReadGame()
    {
        string? directory = Environment.GetEnvironmentVariable("STARDEW_GAME_PATH");
        Assert.False(string.IsNullOrEmpty(directory), "STARDEW_GAME_PATH is required for static game checks.");
        string file = Path.Combine(directory, "Stardew Valley.dll");
        output.WriteLine($"Game DLL SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}");
        return ModuleDefinition.ReadModule(file);
    }

    private readonly ITestOutputHelper output;
}
